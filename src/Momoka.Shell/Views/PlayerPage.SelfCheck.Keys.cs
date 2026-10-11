using System.Text;
using Momoka.Playback;
using Momoka.Shell.Interop;
using Windows.System;

namespace Momoka.Shell.Views;

/// <summary>
/// 播放器可重绑快捷键的自检（「参考上图在设置中新增快捷键功能」，2026-09-08）。放在单独一个文件里，避开当前
/// 工作树里没提交的那批（<c>PlayerPage.SelfCheck.Input.cs</c> 等）。
/// <para>
/// 同一张表的另一半在 <see cref="ProbeQuietKeys"/>：哪几个动作按下去**不该**把控件叫出来（用户令 2026-10-07 /
/// 2026-10-10）。一个管「这颗键有没有人接」，一个管「接了之后屏上该不该动」。
/// </para>
/// </summary>
public sealed partial class PlayerPage
{
    /// <summary>
    /// 快捷键派发这条线，屏上看不出对错、编译看不出、也没法在这台机器上用真键盘自动按一遍（注不进按键），
    /// 所以拿这一关盯三件事：
    /// <list type="bullet">
    /// <item>Core 那张表（<see cref="ShortcutCatalog.Actions"/>）里每个动作，页面这张 动作→处理器 表
    /// （<see cref="ShortcutHandlers"/>）里都有一条 —— 少一条就是那颗键按下去没反应，一个静默死键。</item>
    /// <item>每个动作的默认键都按得出来：<see cref="KeyStrokeInterop"/> 能从某个 <see cref="VirtualKey"/> 产出
    /// 这个 token，否则那是个谁也按不出的默认键（方括号那两颗尤其要盯 —— 它们没有 VirtualKey 名字，靠原始码）。</item>
    /// <item>默认键之间不相撞（<see cref="ShortcutCatalog.DefaultsAreUnique"/>）—— 撞了装机就带着一个
    /// <see cref="ShortcutCatalog.Rebind"/> 拦不住的冲突。</item>
    /// </list>
    /// 全在内存里比，不碰 mpv、不真播：建 <see cref="ShortcutHandlers"/> 只读键、不执行处理器。
    /// </summary>
    internal (bool Ok, string Detail) ProbeShortcuts()
    {
        var actions = ShortcutCatalog.Actions;
        var handlerIds = ShortcutHandlers.Keys.ToHashSet(StringComparer.Ordinal);

        var missing = actions.Where(action => !handlerIds.Contains(action.Id)).Select(action => action.Id).ToList();
        var extra = handlerIds.Where(id => actions.All(action => action.Id != id)).ToList();

        // KeyStrokeInterop 能产出的全部 token —— 扫 0..0xFF 而不是 Enum.GetValues：方括号（219/221）没有
        // VirtualKey 名字，不在枚举成员里，只有连着数字扫才盖得到。默认键的 Key 必须落在这个集合里，否则没有
        // 一个物理键按得出它，那颗键从装机起就是死的。
        var producible = new HashSet<string>(StringComparer.Ordinal);
        for (var code = 0; code <= 0xFF; code++)
            if (KeyStrokeInterop.Token((VirtualKey)code) is { } token) producible.Add(token);

        var unreachable = actions.Where(action => !producible.Contains(action.Default.Key)).Select(action => action.Id).ToList();
        var unique = ShortcutCatalog.DefaultsAreUnique();

        // 空格拦截和 chrome 焦点归还（2026-09-13「新增按空格暂停」）这两条 Root 接线：注册若被拆掉，
        // 焦点被控件拿走之后空格就又哑了 —— 键盘到不了页面那一路，屏上最静默的一种死法。
        // 2026-09-15 加键盘兜底那半：窗口的 WH_KEYBOARD 钩子没装上，焦点掉出岛（被别的应用抢过前台
        // 再回来、焦点落在宿主/视频子窗口上）之后 Esc/空格照样哑，同样只有这里能喊出来。
        var armed = SpaceAndFocusWiringArmed;
        var fallback = _window?.KeyboardFallbackInstalled ?? true;

        var ok = missing.Count == 0 && extra.Count == 0 && unreachable.Count == 0 && unique && armed && fallback;

        var detail = new StringBuilder($"{actions.Count} 个动作、处理器 {handlerIds.Count} 个");
        if (missing.Count > 0) detail.Append($"；缺处理器：{string.Join('、', missing)}");
        if (extra.Count > 0) detail.Append($"；多出处理器：{string.Join('、', extra)}");
        if (unreachable.Count > 0) detail.Append($"；默认键按不出：{string.Join('、', unreachable)}");
        detail.Append(unique ? "；默认键互不相同" : "；默认键有相撞");
        detail.Append(armed ? "；空格接线在" : "；空格接线被拆");
        detail.Append(fallback ? "；键盘兜底在" : "；键盘兜底缺");

        return (ok, detail.ToString());
    }

    /// <summary>
    /// 键盘命令要不要把整套控件叫出来（<see cref="WakesChrome"/> 那张表）：用户令 2026-10-07「暂停时不要自动
    /// 显示播放控件」，2026-10-10「集成模式下快进或者快退的时候不要自动显示播放控件 / 开始播放暂停播放的时候
    /// 也不要 / 退出全屏的时候也不要」（问实了进全屏同样不给）。
    /// <para>
    /// 坏起来的样子是静默的：按一下 ← 或空格，屏上冒出一整排控件挡在画面中间 —— 不报错、不崩，截图里也分不出
    /// 是这一次按出来的还是上一次留下的。所以两半都问：<b>表</b>（每个动作此刻的取舍，含「有没有新动作没被
    /// 归置」）与<b>接线</b>（真按键走 <see cref="Dispatch"/>，读回来的 wake 必须与表一致 —— 表对了而调用点
    /// 又硬写一个 true，只有这一问拦得住）。
    /// </para>
    /// <para>
    /// 真按的只有<b>快退／快进那四颗</b>与<b>空格</b>：它们的处理器只发一条 mpv 命令（自检没有播放，命令在
    /// <c>PlaybackService</c> 里落进空气），按下去不动窗口、不改屏上的任何东西。F 与 Esc 不真按 —— F 会真的
    /// 切窗口，Esc 一旦不满足「正全屏」那条前置就会落到「停止播放」上；两者与上面这几颗取的是同一句
    /// <see cref="WakesChrome"/>（Esc 退全屏那一支写的就是 <c>WakesChrome("toggle-fullscreen")</c>），
    /// 表那一半已经把它们的取舍钉住。
    /// </para>
    /// <para>
    /// 按哪几颗<b>按当刻的绑定算</b>：用户把 ← 改绑到别的动作上时，这一关跟着改后的动作问；改绑到的动作不在
    /// 「按下去没有副作用」那一小撮里（比如改成了下一集）就跳过并写明。设置不同不该让这一关变红，所以「一颗
    /// 都没真按」不算失败 —— 但会写进读数，谁看谁知道这一趟有没有接线证据。
    /// </para>
    /// </summary>
    internal (bool Ok, string Detail) ProbeQuietKeys()
    {
        if (!Attached) return (false, "播放层未接线");

        // 不给控件宽限的那几个动作。次序照 ShortcutCatalog.Actions 里那几行，读数读起来顺。
        string[] quiet =
        [
            "toggle-pause", "seek-backward", "seek-forward", "seek-backward-long", "seek-forward-long",
            "toggle-fullscreen"
        ];

        var actions = ShortcutCatalog.Actions;
        var wrong = new List<string>();

        var missing = quiet.Where(id => actions.All(action => action.Id != id)).ToList();
        if (missing.Count > 0) wrong.Add($"表里没有这些动作：{string.Join('、', missing)}");

        foreach (var action in actions)
        {
            var want = !quiet.Contains(action.Id, StringComparer.Ordinal);
            if (WakesChrome(action.Id) != want) wrong.Add($"{action.Id} 的取舍不对");
        }

        // 接线那一半。按下去没有副作用的那一小撮：四条 seek 与 toggle-pause 的处理器只发一条 mpv 命令。
        string[] drivable =
            ["toggle-pause", "seek-backward", "seek-forward", "seek-backward-long", "seek-forward-long"];

        var driven = new List<string>();
        var skipped = new List<string>();

        foreach (var key in new[] { VirtualKey.Space, VirtualKey.Left, VirtualKey.Right, VirtualKey.Down, VirtualKey.Up })
        {
            if (KeyStrokeInterop.Token(key) is not { } token)
            {
                skipped.Add($"{key}（拼不出 token）");
                continue;
            }

            // 与 ShortcutCommand 里那一份同一句：按下这一刻的修饰键从系统读（KeyRoutedEventArgs 不带）。
            var stroke = new KeyStroke(token, Native.CtrlHeld, Native.AltHeld, Native.ShiftHeld);
            var action = ShortcutCatalog.Lookup(ViewModel.ShortcutBindings, stroke);

            if (action is null) { skipped.Add($"{key}（此刻没绑动作）"); continue; }
            if (!drivable.Contains(action, StringComparer.Ordinal)) { skipped.Add($"{key}（此刻绑在 {action} 上）"); continue; }

            if (!Dispatch(key, out var wake)) { wrong.Add($"{key} 按下去没人接"); continue; }
            if (wake != WakesChrome(action)) wrong.Add($"{key}（{action}）给宽限={wake}，与表不符");

            driven.Add($"{key}→{action}：{(wake ? "叫控件" : "不叫")}");
        }

        var detail = new StringBuilder($"{actions.Count} 个动作，{quiet.Length} 个不给控件宽限"
            + $"（{string.Join('、', quiet.Select(ShortcutCatalog.Label))}），其余 {actions.Count - quiet.Length} 个照旧");

        detail.Append(driven.Count == 0
            ? "；一颗都没真按（当刻绑定都落在别的动作上），接线这半这一趟没有证据"
            : $"；真按 {driven.Count} 颗：{string.Join('、', driven)}");

        if (skipped.Count > 0) detail.Append($"；跳过 {string.Join('、', skipped)}");
        if (wrong.Count > 0) detail.Append($"；不符：{string.Join('、', wrong)}");

        return (wrong.Count == 0, detail.ToString());
    }
}
