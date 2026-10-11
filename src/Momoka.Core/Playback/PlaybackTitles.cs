using Momoka.Emby;

namespace Momoka.Playback;

/// <summary>
/// 播放页左上角第二行那句文件信息 —— 「文件大小 · 分辨率 · 视频编码 · 音频格式 · 组名」。这一句就是
/// <see cref="MediaSource.ToFileInfoLabel"/>，详情页那行「视频：…」用的是同一条（用户令 2026-10-09
/// 「这里显示的视频信息改为 文件大小 分辨率 视频编码 音频格式 组名，播放页面标题的也改成一样的」，在
/// 2026-09-28「下方那一栏改为分辨率+视频编码+音频格式+组名」前置了文件大小）。
/// <para>
/// 留这一层是为了给「播放页第二行」一个说得出口的名字：集成模式喂给 <c>PlayerViewModel.Subtitle</c>，
/// 独占模式经 <c>momoka-subline</c> 消息推给 uosc 顶栏的副标题（alt title），两处都走这里，改动一处即可
/// 同时改两条管线。空串＝集成模式副标题框收起（见 <c>SubtitleVisibility</c>）。
/// </para>
/// </summary>
public static class PlaybackTitles
{
    /// <summary>播放页左上角第二行的文件信息，与详情页那行同源（<see cref="MediaSource.ToFileInfoLabel"/>）。</summary>
    public static string Subline(MediaSource? source) => source?.ToFileInfoLabel() ?? "";
}
