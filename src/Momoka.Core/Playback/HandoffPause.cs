namespace Momoka.Playback;

/// <summary>
/// 合并的窗口切换共享一次暂停归属；不能把自己的暂停当作用户原先暂停。
/// <para>
/// 这一笔账只回答一件事：<b>为了这一趟切换，我们是不是亲手把片子冻住了</b>。冻过就欠用户一次放开，
/// 而「这一趟还算不算数」与「这一部片子还在不在」是两个问题 —— 前者决定画面要不要继续摆，后者才决定
/// 暂停该不该还。把两者混成一条判据，切换半路被作废时这笔账就被静悄悄抹掉，片子停在暂停上再没人放开
/// （2026-10-22 用户报「视频暂停着、点画面没反应」，日志里那趟退全屏的切换停在半路，位置 28:04 一直到
/// 关闭都没再动过）。
/// </para>
/// </summary>
public sealed class HandoffPause
{
    private bool _owned;

    /// <summary>取得这一趟的暂停责任。<paramref name="paused"/> 为真＝用户自己就停着，一根手指都不碰。</summary>
    public bool Acquire(bool paused)
    {
        if (_owned || paused) return false;
        _owned = true;
        return true;
    }

    /// <summary>
    /// 这一趟的暂停账收尾。
    /// </summary>
    /// <param name="pending">
    /// 还有下一趟接着冻（快速反向时同一个实例会被连着用几趟）：账留在自己手里，这一趟不还。
    /// </param>
    /// <param name="playbackCurrent">
    /// 冻住的那一场播放<b>还是现在这一场</b>。为假＝已经换片或停了 —— 这时发 <c>pause=no</c> 会落到
    /// 别人头上，所以只把账抹掉、不还。
    /// </param>
    /// <returns>True 表示调用方现在就该发「恢复播放」。</returns>
    public bool Release(bool pending, bool playbackCurrent)
    {
        if (pending) return false;

        var resume = _owned && playbackCurrent;
        _owned = false;
        return resume;
    }
}
