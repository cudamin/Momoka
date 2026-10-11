namespace Momoka.Mpv;

using System.Globalization;
using Momoka.Configuration;

/// <summary>
/// The handful of mpv options the client always sets, whatever is on the settings page.
/// <para>
/// This is what took over from the globally-active part of the user's <c>mpv.conf</c>. Both backends
/// now start mpv with the config files switched off — <c>--no-config</c> for mpv.exe, and libmpv is
/// already <c>config=no</c> by default — so anything that used to arrive from that file has to be
/// named here or it is simply gone. Only a few lines qualify: the rest of mpv.conf was either window
/// and session behaviour the client owns itself, or picture settings that belong on the settings page
/// where the user can see them.
/// </para>
/// <para>
/// Emitted first, so every one of these can be overridden by 视频输出 / 音频输出, by a shader group, or
/// by 附加参数 — on the command line and on repeated libmpv option calls alike, the last value wins.
/// </para>
/// </summary>
public static class MpvBaseline
{
    /// <summary>设置页「截图格式」下拉的候选（用户令 2026-10-11：「PNG（默认）排第一个 jpg第二webp第三，
    /// 另外两个删掉」—— jxl/avif 从候选退役，存过的值由 Normalize 归一回默认）。jpeg 是 jpg 的别名，不单列；
    /// 随包内核实测：这几个都在 <c>option-info/screenshot-format/choices</c> 里。</summary>
    public static readonly MpvChoice[] ScreenshotFormats =
    [
        new("png", "PNG（默认）"),
        new("jpg", "JPG"),
        new("webp", "WebP")
    ];

    /// <summary>
    /// 截图画质这一组，从设置解析出来的下发形态。解析规则住在这里的理由与 <see cref="ScreenshotTemplate"/>
    /// 相同：起播（<see cref="Build"/>）与正在播的实时推送（<c>PlaybackService.ApplyScreenshotOptionsAsync</c>）
    /// 是同一份选项表的两次消费 —— 一处规则、两处同源，谁也不会悄悄和谁分家。
    /// <para>
    /// 夹取与归一是**双层**的：存储端 <c>SettingsMigration.Normalize</c> 在每次读盘时做一遍，这一头的
    /// <see cref="From"/> 再做一遍 —— 后者管的是没经过读盘管线的设置对象（测试、新 new 出来的）。两处用同一套
    /// 范围常量，手改设置文件写出的越界值到不了 mpv：外置 mpv.exe 对超范围的值是拒绝起播，不是忽略。
    /// </para>
    /// </summary>
    /// <param name="Format">mpv 拼写的格式名；认不出的值回装机默认 <see cref="DefaultFormat"/>。</param>
    /// <param name="WebpLossless">WebP 无损。开启时 mpv 自己忽略下面两个数（手册原话「当 lossless=no 时生效」）。</param>
    /// <param name="WebpQuality">有损 WebP 的质量，0–100。</param>
    /// <param name="WebpCompression">WebP 压缩等级，0–6；有损时也影响画质。</param>
    /// <param name="JpegQuality">JPG 的质量，0–100（mpv 自己的默认 90，装机默认 100 = 参考预设的取值）。</param>
    /// <param name="TagColorspace">色彩空间标记。关着（预设）或格式承接不了时，mpv 转 sRGB 落盘。</param>
    /// <remarks>
    /// 随包内核实测（2026-10-11，<c>work/probe-screenshot.py</c>，vo=null 合成片、无窗口无网络）：webp 与
    /// jpeg-quality 都在随包内核里（质量/压缩/tag 默认 75/4/yes，jpeg-quality 默认 90、范围 0–100），
    /// webp 85/6 无损关落盘成功。外置 mpv.exe 端这组选项随 PlayerOptions 进命令行。
    /// </remarks>
    public sealed record ScreenshotOptions(
        string Format,
        bool WebpLossless,
        int WebpQuality,
        int WebpCompression,
        int JpegQuality,
        bool TagColorspace)
    {
        /// <summary>装机默认格式（用户令 2026-10-11「默认要用png」）。目录里没有「不设置」这一档：
        /// 认不出的值回这里，而不是悄悄换成 mpv 出厂的 jpg。参考预设的 webp 仍是候选之一，
        /// 那一组的三条选项只在它被选中时下发。</summary>
        public const string DefaultFormat = "png";

        /// <summary>WebP 三条选项只认这个拼写；设置页显隐与下发规则都用它。</summary>
        public const string WebpFormat = "webp";

        /// <summary>JPG 质量那一条只认这个拼写（jpeg 是别名，候选目录不收）。</summary>
        public const string JpegFormat = "jpg";

        public const int MinimumQuality = 0;
        public const int MaximumQuality = 100;
        public const int MinimumCompression = 0;
        public const int MaximumCompression = 6;

        /// <summary>设置页滑块夹不到的手写值在这里收口；Normalize 存储端同一条规则。</summary>
        public static string NormalizeFormat(string? value)
        {
            var trimmed = (value ?? "").Trim();
            foreach (var choice in ScreenshotFormats)
            {
                if (string.Equals(choice.Value, trimmed, StringComparison.OrdinalIgnoreCase)) return choice.Value;
            }

            return DefaultFormat;
        }

        public static ScreenshotOptions From(MpvSettings settings) => new(
            NormalizeFormat(settings.ScreenshotFormat),
            settings.ScreenshotWebpLossless,
            Math.Clamp(settings.ScreenshotWebpQuality, MinimumQuality, MaximumQuality),
            Math.Clamp(settings.ScreenshotWebpCompression, MinimumCompression, MaximumCompression),
            Math.Clamp(settings.ScreenshotJpegQuality, MinimumQuality, MaximumQuality),
            settings.ScreenshotTagColorspace);

        /// <summary>
        /// 这一组落到 mpv 的选项表。WebP 三条只随 webp 格式发、JPG 质量只随 jpg 格式发 —— 发给别的格式是噪音；
        /// 而无损开着时质量与压缩**照发**：mpv 那一刻忽略它们，但推送端「不推＝保持我上一分钟发的那条」，少发
        /// 一条，用户把无损拨回去的下一张截图就会带着旧质量落盘。
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> ToMpvOptions()
        {
            var options = new List<KeyValuePair<string, string>>(5) { new("screenshot-format", Format) };
            if (string.Equals(Format, WebpFormat, StringComparison.Ordinal))
            {
                options.Add(new("screenshot-webp-lossless", WebpLossless ? "yes" : "no"));
                options.Add(new("screenshot-webp-quality", WebpQuality.ToString(CultureInfo.InvariantCulture)));
                options.Add(new("screenshot-webp-compression", WebpCompression.ToString(CultureInfo.InvariantCulture)));
            }
            else if (string.Equals(Format, JpegFormat, StringComparison.Ordinal))
            {
                options.Add(new("screenshot-jpeg-quality", JpegQuality.ToString(CultureInfo.InvariantCulture)));
            }

            options.Add(new("screenshot-tag-colorspace", TagColorspace ? "yes" : "no"));
            return options;
        }
    }
    /// <summary>
    /// What one screenshot is called. Not a format string to fill in — a constant: every part mpv used to
    /// need the client for, mpv now writes itself, at the moment the shutter fires rather than at launch.
    /// <para>
    /// <c>%{media-title}</c> is the film's name, and it is <b>the same string the OSD shows</b>:
    /// the property resolves <c>force-media-title</c> first, which both backends get from
    /// <see cref="Momoka.Playback.PlaybackRequest.Title"/> (the headline — 单集＝「剧名 SxxExx 集名」，电影＝片名),
    /// falls back through container metadata, and only then to the URL. mpv sanitizes template
    /// expansions into legal filename characters on its own (a title's <c>: / ?</c> become <c>_</c>),
    /// verified against the shipped DLL — so a shot of an episode with a colon in its name lands on disk
    /// instead of failing, and an inline episode switch needs no rewrite of anything: the next shot reads
    /// the title the switch just pushed. That is the whole reason the client no longer composes the
    /// template in C# from <see cref="Emby.DownloadPlan.Safe"/>.
    /// </para>
    /// <para>
    /// The timecode is <c>%wH.%wM.%wS.%wT</c> — playback position with milliseconds. The reference preset
    /// writes <c>%P</c>, which is the same thing spelled <c>HH:MM:SS.mmm</c>, and colons cannot be in a
    /// Windows filename (mpv would turn them into underscores); the dots say the same thing and stay legal
    /// everywhere. <c>%n</c> is not decoration: mpv never overwrites a screenshot, and it only goes
    /// looking for a free name when the template carries a sequence number — without one, a paused film's
    /// timecode (milliseconds included) does not move, so 截图 — 屏上这一帧 followed by 截图 — 原始画面
    /// would resolve to one name twice and the second shot would simply not be written.
    /// </para>
    /// <para>
    /// No path on the front, although the reference preset carries one: template path and
    /// <c>screenshot-directory</c> are the two alternatives mpv offers, and this client already owns the
    /// directory half — 设置 → 播放行为 → 截图保存目录, resolved and applied per launch and pushed live to a
    /// running film on change. Baking <c>~~/files/screen</c> here would take the decision away from that
    /// setting and point <c>~~</c> at whatever counts as mpv's config directory under <c>--no-config</c>.
    /// </para>
    /// </summary>
    public const string ScreenshotTemplate = "%{media-title}-%wH.%wM.%wS.%wT-%n";

    /// <param name="shaderCacheDirectory">
    /// Where mpv may cache compiled shaders. Null skips the option, which is only right for a test:
    /// without it every playback recompiles the whole chain, which on an iGPU is seconds of black screen.
    /// </param>
    /// <param name="screenshotDirectory">
    /// Where 截图 land. Null skips all the screenshot options, which is only right for a test — see
    /// <see cref="ScreenshotTemplate"/> for why an unset directory is the reason this client had no
    /// screenshot feature at all until now.
    /// </param>
    /// <param name="screenshot">
    /// The 截图 picture options, resolved from settings by <see cref="ScreenshotOptions.From"/>. The
    /// planner hands them in on every plan; nothing here reads AppSettings itself, so a test can pin the
    /// exact emission rule against a hand-built record.
    /// </param>
    /// <param name="fontsDirectory">
    /// Where this client's bundled 字幕字体 would live (assets/fonts, copied next to the exe). mpv's
    /// <c>sub-fonts-dir</c> — font files there are used for subtitles without being installed into
    /// Windows. Nothing is bundled today (v19's default, Microsoft YaHei UI Semibold, is a system font),
    /// so this points at an empty or absent folder, which mpv tolerates; the option stays so a font
    /// dropped into that folder is picked up without a code change. Null skips it, which is what a test wants.
    /// </param>
    public static IReadOnlyList<KeyValuePair<string, string>> Build(
        ScreenshotOptions screenshot,
        string? shaderCacheDirectory = null,
        string? screenshotDirectory = null,
        string? fontsDirectory = null)
    {
        var options = new List<KeyValuePair<string, string>>(9)
        {
            // The client's seek bar hands mpv exact timestamps. Without this a click lands on the
            // preceding keyframe, up to several seconds from where the user aimed.
            new("hr-seek", "yes"),

            // Everything here is streamed over HTTP from Emby, and the tracks are chosen by the
            // client. Left on, mpv would probe the URL's directory for sibling subtitle and audio
            // files on every launch: a burst of 404s against the server that can never find anything.
            new("sub-auto", "no"),
            new("audio-file-auto", "no"),

            // The client asks Emby which display profile a file has and decides tone mapping from
            // that. An ICC profile picked up from the desktop would silently override it, and on
            // Windows that profile is usually whatever the monitor's driver dropped there rather than
            // a measurement. This is the floor, not a verdict: 设置 → 视频输出 → 自动 ICC 校色
            // (VideoSettings.IccProfileAuto) is emitted after this list and lifts it to yes.
            new("icc-profile-auto", "no"),

            // mpv 自带的统计脚本（内置 stats.lua）关掉。它只有英文，而这里要的是参考项目那一套统计项的
            // 中文版，那份覆盖脚本由 MpvStats 后装（两条 libmpv 管线走运行期 load-script，外置 mpv.exe 走
            // --script=）。不关就会有两份脚本同时申请 stats/display-stats 这个名字。
            new("load-stats-overlay", "no"),

            // 统计面板现在是 mpv 画的 OSD（见 MpvStats），而它通篇是中文。mpv 的默认 OSD 字体是
            // sans-serif，在 Windows 上落到 Arial 那一支、靠字形回退去借中文字体；这里直接点名一款系统一定
            // 有的中文字体，省掉那次回退。独占模式的 Lua UI 也写同一个值（MpvUi 的 osd-font），这里只是让
            // 集成模式同样有 —— 一处取值，两处引用同一个常量。
            new("osd-font", MpvUi.OsdFont)
        };

        if (!string.IsNullOrWhiteSpace(shaderCacheDirectory))
        {
            options.Add(new KeyValuePair<string, string>("gpu-shader-cache-dir", shaderCacheDirectory));
        }

        if (!string.IsNullOrWhiteSpace(fontsDirectory))
        {
            options.Add(new KeyValuePair<string, string>("sub-fonts-dir", fontsDirectory));
        }

        if (!string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            options.Add(new KeyValuePair<string, string>("screenshot-directory", screenshotDirectory));
            options.AddRange(screenshot.ToMpvOptions());
            options.Add(new KeyValuePair<string, string>("screenshot-template", ScreenshotTemplate));
        }

        return options;
    }
}
