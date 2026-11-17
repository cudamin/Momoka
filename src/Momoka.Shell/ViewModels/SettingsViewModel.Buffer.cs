using Momoka.Configuration;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 设置 → 视频输出 →「缓冲」那张卡：内网、外网各一组「大小 + 时长」。
/// <para>
/// 2026-11-17 按用户令从「基础输出」里拆出来单独一张卡，位置在「补帧」上面；四行照参考图的写法，每行说明
/// 开头就写「当前：…（默认 …）」—— 从前那一行写的是「0 使用默认值」，屏上读不出此刻到底缓冲多少，也读不出
/// 默认是多少。
/// </para>
/// <para>
/// 说明里的「当前」由行自己改：数字框改完写进设置、重新读回之后，那一行把自己的说明重述一遍
/// （<see cref="SettingRow.Restate"/>，和「视频同步」那一行同一个用法）。不这么做的话，拖完/填完，说明还
/// 停在上一个数上 —— 那正是这张卡要治的那种骗人。
/// </para>
/// </summary>
public sealed partial class SettingsViewModel
{
    private const string BufferTitle = "缓冲";

    private SettingSection BufferCard()
    {
        var note = "服务器地址在本机或局域网（localhost、私有地址、.local 或不带点的主机名）时用「内网」一组，"
            + "其余地址用「外网」一组，起播时按地址自动判定。缓冲大小是前向缓冲上限，后向取其一半；"
            + "实际缓冲取大小与时长二者的较小值。调高只增加内存占用，不能提高网速。";
        return new SettingSection(VideoCategory, BufferTitle, NextPlayback + " " + note,
        [
            BufferSizeRow(lan: true),
            BufferSecondsRow(lan: true),
            BufferSizeRow(lan: false),
            BufferSecondsRow(lan: false)
        ]);
    }

    /// <summary>一行缓冲大小（MB）。读写那一对指向内网或外网那一格，说明里的当前值跟着它自己走。</summary>
    private SettingNumberRow BufferSizeRow(bool lan)
    {
        var buffer = Settings.Buffer;
        Func<int> read = lan ? () => buffer.LanMegabytes : () => buffer.WanMegabytes;
        Action<int> write = lan ? value => buffer.LanMegabytes = value : value => buffer.WanMegabytes = value;

        SettingNumberRow? row = null;
        row = Number(lan ? "内网缓冲大小（MB）" : "外网缓冲大小（MB）",
            BufferSettings.MinimumMegabytes, BufferSettings.MaximumMegabytes, read, write,
            BufferSizeNote(lan, read()), () => row?.Restate(BufferSizeNote(lan, read())),
            "demuxer-max-bytes、demuxer-max-back-bytes");
        return row;
    }

    /// <summary>一行缓冲时长（秒）。同一形状，只是换算到 mpv 的 <c>cache-secs</c>。</summary>
    private SettingNumberRow BufferSecondsRow(bool lan)
    {
        var buffer = Settings.Buffer;
        Func<int> read = lan ? () => buffer.LanSeconds : () => buffer.WanSeconds;
        Action<int> write = lan ? value => buffer.LanSeconds = value : value => buffer.WanSeconds = value;

        SettingNumberRow? row = null;
        row = Number(lan ? "内网缓冲时长（秒）" : "外网缓冲时长（秒）",
            BufferSettings.MinimumSeconds, BufferSettings.MaximumSeconds, read, write,
            BufferSecondsNote(lan, read()), () => row?.Restate(BufferSecondsNote(lan, read())),
            "cache-secs");
        return row;
    }

    private static string BufferSizeNote(bool lan, int megabytes) =>
        $"当前：{megabytes}MB（默认 {BufferSettings.DefaultMegabytes}MB）。{ScopeSentence(lan)}；前向缓冲上限，后向取其一半";

    private static string BufferSecondsNote(bool lan, int seconds) =>
        $"当前：{seconds}s（默认 {BufferSettings.DefaultSeconds}s）。{ScopeSentence(lan)}；实际缓冲取大小与时长二者的较小值";

    private static string ScopeSentence(bool lan) => lan
        ? "服务器在本机或局域网时使用"
        : "服务器地址不在本机或局域网时使用";
}
