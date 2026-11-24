using Microsoft.UI.Xaml;
using Momoka.Playback;
using Momoka.Shell.Interop;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 长按 ← / →（2026-11-24 用户令「在快退和快进下方新增，快进跨度（倍），长按箭头以这个倍速播放」）。
/// <para>
/// 用户答的是「单击跳秒，按住约半秒转倍速；→ 倍速正放，← 按同一倍率连续快退；两条管线都做」。所以这里
/// 只做三件事：按下的第一下照旧跳一次秒数；<see cref="SeekHold.HoldMilliseconds"/> 之后如果那颗键还
/// 按着，→ 把速度切到设置里的倍率、← 开始按倍率往回推；松开（或窗口失焦、停止播放）时把速度还原。
/// </para>
/// <para>
/// <b>为什么按键状态是问 OS 而不是记账。</b>集成模式有 KeyUp 可听，内置独占窗口却只有 mpv 转发的
/// <c>momoka-shortcut</c>（按下与自动重复都算），永远等不到一个松开的消息。两条管线只有「拿着当刻
/// 绑定去问 <see cref="Native.KeyDown(int)"/>」这一个共同的答案；它同时把「焦点跑掉了、KeyUp 没送到」
/// 这种会卡在倍速上的坏法也一并接住 —— 键真的不在了，轮询下一拍就会收尾。
/// </para>
/// <para>
/// 自动重复的消息在待判定期与长按中都直接吞掉：它们只在证明「键还按着」，而这件事已经由轮询回答，
/// 再让每一条重复都跳一次秒数，就退回用户要改掉的那个「按住一路狂跳」。
/// </para>
/// </summary>
public sealed partial class PlayerViewModel
{
    private DispatcherTimer? _seekHoldPoll;
    private string? _seekHoldPending;   // 按下了，还没到长按判定时间
    private string? _seekHoldActive;    // 已经在长按：→ 倍速 / ← 连续快退
    private int _seekHoldKey;           // 这一按对应的物理键，问 GetAsyncKeyState 用
    private long _seekHoldPressedAt;
    private long _seekHoldNextRewind;
    private double _seekHoldPreviousSpeed = 1;

    /// <summary>
    /// 每按下一次快退／快进都走这里：集成模式的 KeyDown（含系统自动重复）、独占窗口的每条
    /// <c>momoka-shortcut</c> 同键消息。第一次按下照旧跳秒并开始等长按；重复消息在待判定期与长按中
    /// 都只是「键还按着」的又一次证明，吞掉即可。
    /// </summary>
    internal void NoteSeekKeyPress(string action, int keyCode, bool repeated = false)
    {
        if (!SeekHold.IsSeek(action)) return;

        // 没有播放、或这颗键的绑定拼不出一个物理键码（自定义 Vk、媒体键之外的怪键）：不猜长按，
        // 保持用户已经熟悉的行为 —— 按一下跳一次。独占窗口没有松开消息，只能在键码可判时做长按。
        if (keyCode == 0 || !_playback.IsPlaying)
        {
            if (action == SeekHold.Forward) SeekForward();
            else SeekBackward();
            return;
        }

        if (_seekHoldActive == action)
        {
            // 同一个动作在长按中又来了一条：自动重复吞掉；WasKeyDown=false 说明上一按的 KeyUp 漏了，
            // 这一下是新按下 —— 先收掉旧长按，再照第一次按下那样跳一次并重新等判定。
            if (repeated) return;
            EndSeekHold();
        }
        else if (_seekHoldPending == action)
        {
            // 同一条键的自动重复：跳秒已经发生过一次，这里只让它继续等长按判定。
            if (repeated) return;
            // 新的一次按下（集成模式的 WasKeyDown=false）：上一按已经松开，只是轮询还没跑到。
            ClearSeekHoldPending();
        }
        else if (_seekHoldPending is not null || _seekHoldActive is not null)
        {
            // 换了一颗方向键／换了一个方向：先把上一按的倍速／快退收掉。
            EndSeekHold();
        }

        if (action == SeekHold.Forward) SeekForward();
        else SeekBackward();
        _seekHoldPending = action;
        _seekHoldKey = keyCode;
        _seekHoldPressedAt = Now;
        SeekHoldPoll.Start();
    }

    /// <summary>集成模式的 KeyUp：松开就立刻收尾，不必等下一拍轮询。</summary>
    internal void ReleaseSeekKey(int keyCode)
    {
        if (keyCode == 0 || keyCode != _seekHoldKey) return;
        if (_seekHoldActive is not null) EndSeekHold();
        else ClearSeekHoldPending();
    }

    /// <summary>
    /// 别的快捷键按下、停止播放、页面退出时调用：长按中的 → 先还原速度，待判定的只清状态（它本来也
    /// 只跳过一次秒）。
    /// </summary>
    internal void CancelSeekHold()
    {
        if (_seekHoldActive is not null) EndSeekHold();
        else ClearSeekHoldPending();
    }

    /// <summary>独占窗口的箭头消息：动作当刻绑在哪颗键，就拿哪颗键去问 OS。拼不出键码时按「判不了」处理。</summary>
    internal void NoteNativeSeek(string action)
    {
        var stroke = ShortcutCatalog.Resolve(ShortcutBindings).GetValueOrDefault(action);
        NoteSeekKeyPress(action, stroke is { IsEmpty: false } ? ShortcutKeyCatalog.KeyCode(stroke.Key) : 0,
            repeated: true);
    }

    private DispatcherTimer SeekHoldPoll => _seekHoldPoll ??= CreateSeekHoldPoll();

    private DispatcherTimer CreateSeekHoldPoll()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SeekHold.PollMilliseconds) };
        timer.Tick += (_, _) => OnSeekHoldPoll();
        return timer;
    }

    private void OnSeekHoldPoll()
    {
        if (_seekHoldPending is { } pending)
        {
            if (!Native.KeyDown(_seekHoldKey))
            {
                ClearSeekHoldPending();
                return;
            }
            if (!SeekHold.IsHeld(_seekHoldPressedAt, Now)) return;
            BeginSeekHold(pending);
        }

        if (_seekHoldActive is not { } active) return;

        if (!Native.KeyDown(_seekHoldKey))
        {
            EndSeekHold();
            return;
        }

        // → 已经在倍速播放，不需要再做什么；← 每 200ms 按倍率往回推一步。
        if (active != SeekHold.Backward || Now < _seekHoldNextRewind) return;
        _seekHoldNextRewind = Now + SeekHold.RewindStepMilliseconds;
        SeekBy(-SeekHold.RewindStepSeconds(Settings.Playback.SeekBackwardHoldMultiplier));
    }

    private void BeginSeekHold(string action)
    {
        _seekHoldPending = null;
        _seekHoldActive = action;
        if (action == SeekHold.Forward)
        {
            // 松手回到按下之前的速度，不是回到 1×：用户可能正停在 1.5×，按住 → 只是临时快进。
            _seekHoldPreviousSpeed = SpeedValue;
            SetSpeed(Settings.Playback.SeekForwardHoldMultiplier);
        }
        else
        {
            _seekHoldNextRewind = Now + SeekHold.RewindStepMilliseconds;
        }
    }

    private void EndSeekHold()
    {
        var active = _seekHoldActive;
        _seekHoldActive = null;
        _seekHoldPending = null;
        _seekHoldKey = 0;
        if (active == SeekHold.Forward) SetSpeed(_seekHoldPreviousSpeed, notice: false);
        _seekHoldPoll?.Stop();
    }

    private void ClearSeekHoldPending()
    {
        _seekHoldPending = null;
        _seekHoldKey = 0;
        if (_seekHoldActive is null) _seekHoldPoll?.Stop();
    }
}
