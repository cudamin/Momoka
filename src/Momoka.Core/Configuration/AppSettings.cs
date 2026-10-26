using System.Text.Json.Serialization;
using Momoka.Emby;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Configuration;

/// <summary>
/// The persisted shape of settings.json. Unlike v1 this type is a plain document:
/// it no longer doubles as the live "currently signed-in" state, which is what made
/// the old <c>Activate</c>/<c>UpdateActiveAccount</c> dance so easy to get wrong.
/// </summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 26;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Stable per-install id so Emby treats this client as one device across restarts.</summary>
    public string DeviceId { get; set; } = "";

    public List<ServerProfile> Servers { get; set; } = [];

    /// <summary>
    /// 聚合模式：开着时多台服务器、多个账号可以同时在线，主页、搜索、收藏与跨服务器版本选择覆盖全部来源；
    /// 关着（装机默认）时同一时间只保留一个在线来源，登录另一台服务器或账号会替换当前这一个。
    /// 无论开关如何，切换都不会删除服务器、账号、令牌或密码。
    /// </summary>
    public bool AggregateMode { get; set; }

    public string? LastServerId { get; set; }

    public string? LastAccountId { get; set; }

    public MpvSettings Mpv { get; set; } = new();

    public PlaybackSettings Playback { get; set; } = new();

    public VideoSettings Video { get; set; } = new();

    public AudioSettings Audio { get; set; } = new();

    public ShaderAutomationSettings Shaders { get; set; } = new();

    public UiSettings Ui { get; set; } = new();

    public ShortcutSettings Shortcuts { get; set; } = new();

    public DanmakuSettings Danmaku { get; set; } = new();

    public SubtitleTranslationSettings SubtitleTranslation { get; set; } = new();

    /// <summary>
    /// The MoviePilot service, if the user has one. Sits last because it is the newest and least central
    /// thing here — the whole app works without it, and it is off by default.
    /// </summary>
    public MoviePilotSettings MoviePilot { get; set; } = new();

    public ServerProfile? FindServer(string? id) =>
        id is null ? null : Servers.FirstOrDefault(server => server.Id == id);

    public ServerProfile? ResolveLastServer() => FindServer(LastServerId) ?? Servers.FirstOrDefault();

    public AccountProfile? ResolveLastAccount(ServerProfile? server = null)
    {
        server ??= ResolveLastServer();
        if (server is null) return null;
        return server.Accounts.FirstOrDefault(account => account.Id == LastAccountId) ?? server.Accounts.FirstOrDefault();
    }

    public void Remember(ServerProfile server, AccountProfile? account)
    {
        LastServerId = server.Id;
        LastAccountId = account?.Id;
    }

    public AppSettings EnsureDeviceId()
    {
        if (string.IsNullOrWhiteSpace(DeviceId)) DeviceId = Guid.NewGuid().ToString("N");
        return this;
    }
}

/// <summary>
/// 播放器键盘快捷键，只记用户动过的那几个 —— 见 <see cref="ShortcutCatalog"/> 的类注释（缺键=装机
/// 默认、空串=显式解绑、否则是一个 token 串）。装机是空字典。这个类只是个哑数据袋，判断全在 <c>ShortcutCatalog</c>。
/// </summary>
public sealed class ShortcutSettings
{
    /// <summary>动作 Id → 组合键 token（<see cref="ShortcutCatalog.Serialize"/> 那份写法）。</summary>
    public Dictionary<string, string> Bindings { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ServerProfile
{
    public Momoka.Emby.MediaServerKind ServerKind { get; set; }
    public string? ServerVersion { get; set; }
    public string? ServerId { get; set; }
    /// <summary>The name a brand-new profile carries; a migrated one gets <see cref="MigratedName"/>.</summary>
    public const string DefaultName = "Emby 服务器";

    /// <summary>What v1 settings and a fresh install are called before any server has been reached.</summary>
    public const string MigratedName = "我的 Emby";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = DefaultName;

    public string Url { get; set; } = "http://localhost:8096";

    public List<AccountProfile> Accounts { get; set; } = [];

    /// <summary>
    /// True while the name is still one of the stand-ins, which is the only case where a sign-in may
    /// overwrite it with the name the server reports for itself.
    /// </summary>
    public bool HasPlaceholderName => Name.Trim() is "" or DefaultName or MigratedName;

    public AccountProfile? FindAccount(string? id) =>
        id is null ? null : Accounts.FirstOrDefault(account => account.Id == id);

    public override string ToString() => Name;
}

public sealed class AccountProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Username { get; set; } = "";

    /// <summary>Emby's own user id, learned at authentication time.</summary>
    public string UserId { get; set; } = "";

    /// <summary>DPAPI-wrapped password; empty when the user opted out of remembering it.</summary>
    public string ProtectedPassword { get; set; } = "";

    /// <summary>
    /// DPAPI-wrapped access token. v1 stored this in the clear next to the server URL;
    /// wrapping it means a leaked settings.json is no longer a working credential.
    /// </summary>
    public string ProtectedAccessToken { get; set; } = "";

    public bool RememberPassword { get; set; } = true;

    /// <summary>退出账号时关闭，显式再次登录时开启；保存密码本身不构成重新登录授权。</summary>
    public bool? AutoConnect { get; set; }

    public DateTimeOffset? LastSignedIn { get; set; }

    [JsonIgnore]
    public bool HasSavedPassword => ProtectedPassword.Length > 0;

    [JsonIgnore]
    public bool HasSavedToken => ProtectedAccessToken.Length > 0;

    public override string ToString() => Username;
}

public sealed class MpvSettings
{
    /// <summary>Which player the client starts: the user's mpv.exe or in-process libmpv.</summary>
    public MpvBackendKind Backend { get; set; } = MpvBackendKind.BuiltInLibMpv;

    /// <summary>
    /// 内置播放器的渲染管线：集成模式将画面合成进 XAML 视觉树；独占模式由 mpv 自建原生窗口，
    /// 自管交换链、无边框窗口化全屏。只对内置 libmpv 有效，下一次播放生效。
    /// <para>
    /// <b>默认独占（2026-09-20，用户裁定）。</b>集成模式全屏切换那一下画面合成层追不上窗口尺寸
    /// （窗口瞬间到全屏、合成进窗口的那层界面要几帧才按新尺寸重新提交），第一次进全屏尤其明显 ——
    /// 界面线程没有「立刻重画」这句话，盖不住也修不平（试过压暗幕，只是把「画面缩角」换成「黑闪」，
    /// 同一个根子的两张脸）。独占模式画面在 mpv 自己的顶层窗口里、不经过这层合成，切换干净，是这台机器上
    /// 唯一「像别的播放器一样顺」的路。代价是屏幕控件用 mpv 的 uosc 而非 shell 的 XAML chrome。
    /// </para>
    /// <para>
    /// <b>图形接口（2026-10-11，用户令「独占模式不再强制使用 D3D11」）。</b>集成管线的 gpu-api 仍由契约
    /// 钉死——合成交换链（display-swapchain → SpriteVisual）只存在于 D3D11 路径，Vulkan 产不出这张
    /// 交换链；独占管线是 mpv 自建窗口，没有这份依赖，图形接口改由设置页「图形接口」决定（默认 vulkan：
    /// ArtCNN 的 compute 链在 D3D11 上实测慢五倍，artifacts/shader-probe/gpu-findings.md，fork 二进制里
    /// 编有 winvk 上下文）。换片签名吃进这项设置，改了走重启而不是沿用旧 API（gpu-api 在 initialize
    /// 之后改不动）。
    /// </para>
    /// </summary>
    public VideoPipelineKind Pipeline { get; set; } = VideoPipelineKind.Standalone;

    /// <summary>仅内置独占模式读取自定义 mpv.conf；切换模式保留偏好，下次起播生效。</summary>
    public bool CustomConfigEnabled { get; set; }

    public string CustomConfigPath { get; set; } = "";

    /// <summary>
    /// The user's own <c>mpv.exe</c>, and only the external backend needs it. Empty out of the box:
    /// it used to be hard-coded to one portable mpv installation on this machine
    /// (<c>C:\mpv_config-2026.08.12\mpv.exe</c>), which was the last thing in the client still naming
    /// that folder — the built-in backend reads nothing from it any more, since the shaders and
    /// libmpv's own <c>vulkan-1.dll</c> now ship with the program.
    /// <para>
    /// Empty is reported rather than guessed at: <c>MpvProcessBackend.Validate</c> refuses to start with
    /// 「尚未设置 mpv.exe 的路径」, 诊断 prints 「未设置」, and 设置 → 播放器 is where it gets filled in. An
    /// older settings.json that holds a path keeps it; nothing migrates.
    /// </para>
    /// </summary>
    public string ExecutablePath { get; set; } = "";

    /// <summary>
    /// Enables the named-pipe control channel. Without it the app can only report
    /// "started" and "stopped" to Emby, never a real playback position.
    /// </summary>
    public bool EnableIpc { get; set; } = true;

    /// <summary>
    /// 截图的保存目录 —— 播放器「画面」菜单里三档截图的落点（「在设置中新增截图的保存目录」，用户的话，
    /// 2026-09-29）。<b>空就是装机落点</b>（<see cref="Infrastructure.AppPaths.ScreenshotDirectory"/>，数据目录下的
    /// screenshots），所以缺这个键的旧设置文件读出来行为一个像素都不变，不需要迁移；「恢复默认」把它抄成空，
    /// 也就回到了装机落点（<c>SettingsPreferences</c> 整组抄 <see cref="Mpv"/>，这里自动跟着走）。
    /// <para>
    /// 填了的值不直接拿去用：计划层、关于卡和自检问的都是
    /// <see cref="Infrastructure.AppPaths.ResolveScreenshotDirectory"/> 这一个裁决 —— 清掉引号和空白、相对路径
    /// 展开成绝对（两个后端的工作目录不同，同一个「相对」会落到两个地方，见该函数）。改完当场生效：设置页把
    /// 裁决后的目录推给正在播的那一部（<c>PlaybackService.ApplyScreenshotDirectoryAsync</c> 写 mpv 的
    /// <c>screenshot-directory</c> 属性）；起播那一路由计划层从这份设置里读，两处同源。
    /// </para>
    /// </summary>
    public string ScreenshotDirectory { get; set; } = "";

    /// <summary>
    /// 截图的格式（「参考以下参数重构本项目的截图功能」，用户令 2026-10-11，参数随参考 mpv 配置而来；
    /// 同日再令「默认要用png」，装机默认改回 <b>png</b>，参考预设的 webp 成为候选之一）。
    /// mpv 自己的写法（webp/png/jpg/jxl/avif，候选目录 <see cref="Mpv.MpvBaseline.ScreenshotFormats"/>）；
    /// 缺键的旧设置文件读出来就是装机默认，不需要迁移，手改设置文件写出的大小写变体与认不出的
    /// 值由 <c>SettingsMigration.Normalize</c> 归一回默认 —— 外置 mpv.exe 对认不出的值是拒绝起播，不是忽略。
    /// <para>
    /// 与下面四项一样是「设置页上的行」：恢复默认走 <c>SettingsPreferences</c> 整组抄 <see cref="Mpv"/>，
    /// 自动跟着走；改完当场生效 —— 设置页把整组推给正在播的那一部
    /// （<c>PlaybackService.ApplyScreenshotOptionsAsync</c>），起播那一路由计划层从同一份设置里读，两处同源。
    /// </para>
    /// </summary>
    public string ScreenshotFormat { get; set; } = "png";

    /// <summary>WebP 无损。默认关（mpv 自己的默认也是关）；开启时质量与压缩两行不起作用 —— mpv 手册原话
    /// 「当 lossless=no 时生效」，设置页把它们收起来。仅当格式是 WebP 时随组下发。</summary>
    public bool ScreenshotWebpLossless { get; set; }

    /// <summary>有损 WebP 的质量（0–100，默认 85 = 参考预设的取值）。存储端与下发端夹进同一套范围常量
    /// （<c>SettingsMigration.Normalize</c> 与 <see cref="Mpv.MpvBaseline.ScreenshotOptions"/>），手改设置
    /// 文件越界不会把外置 mpv.exe 拒在门外。</summary>
    public int ScreenshotWebpQuality { get; set; } = 85;

    /// <summary>WebP 压缩等级（0–6，默认 6 = 参考预设的取值）。越高编码越慢；有损时它也影响画质 —— 这是 mpv
    /// 的语义，不是设置页的措辞。</summary>
    public int ScreenshotWebpCompression { get; set; } = 6;

    /// <summary>JPG 的质量（0–100，<c>screenshot-jpeg-quality</c>）。装机默认 <b>100</b> = 参考预设给的取值
    /// （mpv 自己的默认是 90）；存储端与下发端夹进同一套范围常量，同 WebP 质量那条。仅当格式是 JPG 时随组下发，
    /// 设置页那行也只在格式是 JPG 时出现（用户令 2026-10-11：「jpg呢？不用压缩吗？」）。</summary>
    public int ScreenshotJpegQuality { get; set; } = 100;

    /// <summary>把画面的色彩空间标记写进截图。默认<b>关</b>（mpv 自己的默认是开，用户预设选了关）：关着、
    /// 或格式承接不了标记时，mpv 把画面转成 sRGB 再落盘 —— HDR 片源的截图因此是 sRGB 图，预设自选的代价。</summary>
    public bool ScreenshotTagColorspace { get; set; }

    // 「libmpv-2.dll、附加参数貌似没什么用，删除」 — both are gone as of v5.
    //
    // LibMpvPath pointed at a libmpv build to load instead of the bundled one. The client ships its own
    // libmpv-2.dll next to the exe and every probe order it tried found that copy first, so the field
    // could only ever select a *different* mpv ABI — which is a way to break playback, not to fix it.
    //
    // ExtraArguments appended a raw command line to the launch. Everything it was used for during
    // development has since become a real setting on this page, and being last it silently overrode
    // them; a typo in it was also fatal for mpv.exe, which exits on an unknown option before playing
    // anything. SettingsMigration drops both keys off an older settings.json.
    //
    // 「删掉这个功能」 — ConfigPath and InputConfigPath went the same way on 2026-09-03, with the
    // 配置文件 card they existed for. They were the mpv.conf / input.conf the settings page let the user
    // read and edit; since playback runs with --no-config / config=no, nothing in this client ever read
    // either file, and a path box that only ever pointed a text editor at someone else's file is not a
    // setting. A null meant 「infer portable_config beside mpv.exe」, so most files never held them; an
    // older file that does gets them dropped the same way, the deserializer having nowhere to put them.
}

public enum MpvBackendKind
{
    /// <summary>In-process libmpv-2.dll; presentation is selected by the video pipeline.</summary>
    BuiltInLibMpv,

    /// <summary>The user's own mpv.exe in its own window, controlled over a named pipe.</summary>
    ExternalMpv
}

/// <summary>内置播放器的视频呈现方式。枚举值保持兼容已有设置。</summary>
public enum VideoPipelineKind
{
    /// <summary>集成模式：画面合成进 XAML 视觉树，与控件混排。</summary>
    Integrated,

    /// <summary>
    /// 独占模式：mpv 自建原生窗口、自管交换链，无边框窗口化全屏（不请求 DXGI 独占，切换顺滑）。
    /// 图形接口 2026-10-11 起跟随设置页「图形接口」（默认 Vulkan），不再强制 D3D11。
    /// </summary>
    Standalone
}

public sealed class PlaybackSettings
{
    public bool ReportProgressToServer { get; set; } = true;

    public bool ResumeFromSavedPosition { get; set; } = true;

    /// <summary>Watched once playback passes this share of the runtime.</summary>
    public int MarkWatchedPercent { get; set; } = 90;

    /// <summary>
    /// 国漫单独的标记已看阈值（「新增国漫播放进度自定义百分比标记已看」，用户令 2026-09-26；默认值同日续令
    /// 「国漫的标记已看默认改为百分之85，其他的默认为百分之90」）。命中国漫的条目（类型：动画，发行公司带
    /// 腾讯/哔哩哔哩/优酷/爱奇艺，判定在 <see cref="Playback.DonghuaRule"/>）按这一档算，不吃
    /// <see cref="MarkWatchedPercent"/>——后者保持出厂 90。
    /// </summary>
    public int DonghuaMarkWatchedPercent { get; set; } = 85;

    /// <summary>
    /// 播放进度够到「标记已看」阈值时，向服务器补报一次「播放停止」（用户令 2026-09-29：「在设置的通知中
    /// 新增功能，播放进度达到自定义百分比的时候，自动触发发送 播放-停止」，同日续令「发送通知的判断标准改为
    /// 播放行为中的 标记已观看阈值(%)，要区分国漫」）。<b>界面上这一格叫「通知接管」</b>（2026-09-30 用户令
    /// 「进度到阈值补报播放停止 改名为 通知接管」）—— 界面的名字在这儿对一次号：设置键名没跟着改，
    /// 改了等于换 settings.json 里的键，于功能无益。
    /// <para>
    /// 阈值不是另立一格，就是 <see cref="MarkWatchedPercent"/> 与 <see cref="DonghuaMarkWatchedPercent"/>
    /// 这两档（国漫走国漫那一档）—— 取数见 <see cref="Playback.PlaybackService.ShouldReportStop"/>，与
    /// 「结束时标记已看」用的是同一条线，所以不会一个报一个不报。
    /// </para>
    /// <para>
    /// 通知本身仍然由 Emby 服务器发（2026-09-25 起客户端不再自己发 webhook），客户端能出力的地方是把
    /// 「这一条到线了」告诉服务器：服务器收到 <c>Sessions/Playing/Stopped</c> 就按用户配的通知条目把
    /// <c>playback.stop</c> 转发出去（本机那条是 Webhooks → MoviePilot —— 用户要这一格就是为了让那边的
    /// 插件早点记上进度）。<b>播放自己不停</b>，用户接着看；一次播放只补报这一趟，此后本场不再向服务器发
    /// 进度与暂停，收尾也不再报第二遍 —— 见 <see cref="Playback.PlaybackService"/>。
    /// </para>
    /// <para>
    /// 出厂关着：这是替服务器提前替条目下结论，默认不动别人的观看记录。
    /// </para>
    /// <para>
    /// 2026-10-09 用户令又添了一个触发点：「新增打开通知接管后，点击标记为已看后自动发送通知，发送的播放
    /// 进度设置进度为100%」—— 手动点「标记为已看」同样补报一趟，位置按条目时长报满（取数见
    /// <see cref="Playback.PlaybackService.ReportMarkedWatchedAsync"/>）。这一格管的是「客户端替服务器判断
    /// 时机」，阈值与手动两种触发都归它管，所以没有为它另立一格。
    /// </para>
    /// <para>
    /// 2026-10-11 用户令把手动那一支对整季 / 整部剧的落点改到最后一集：「开启通知接管后，标记整季已看时要
    /// 发送最后一集的已看通知」；整部剧即最后一季的最后一集。挑集的规矩见
    /// <see cref="Playback.PlaybackService.ReportMarkedWatchedAsync"/>。
    /// </para>
    /// </summary>
    public bool StopReportEnabled { get; set; }

    public int ProgressReportIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Audio-track languages in priority order: the first one the file actually has wins, and an empty list
    /// means 「whatever the server marks as default」.
    /// <para>
    /// <b>A list rather than the single language this held up to v9.</b> mpv's own <c>alang</c> has always taken
    /// a list, the subtitle side next door has always been one, and 「日语 &gt; 粤语 &gt; 英语」 was simply not
    /// expressible — a Cantonese dub was either the only thing you could ask for or nothing. Parsed by the same
    /// <see cref="Playback.TrackLanguagePriority.ParseList"/> the subtitle row uses, so 「简体中文 &gt; 中文」 in
    /// one box and in the other mean the same thing.
    /// </para>
    /// <para>
    /// <b>There is no companion 「按语言挑 / 跟随默认」 mode any more.</b> An empty list already says 「跟随默认」,
    /// and the old <c>AudioTrack</c> enum was a second way to say exactly that — which is how a stored language
    /// could sit in the file being ignored. See <c>SettingsMigration</c>'s v10 step, which respects the old
    /// mode when carrying the single value over.
    /// </para>
    /// </summary>
    public List<string> AudioLanguages { get; set; } = [];

    /// <summary>
    /// 音轨格式筛选规则（「给音轨新增语言优先级和格式优先级」，2026-09-22）。语言这一关选出「哪几条是这个语言」之后，
    /// 格式这一关在那批轨里按编码／声道的关键词再挑一遍：表内越靠上的规则越优先，优先＝含这个词的排前头，候补＝往后排，
    /// 默认＝不生效。和字幕标题筛选、视频文件名筛选同一套 <see cref="KeywordRule"/>／<see cref="Playback.KeywordFilter"/>，
    /// 只是比较的文本是轨的 Codec／Profile／ChannelLayout／标题拼起来的（TrueHD、DTS-HD、Atmos、7.1、5.1 这些词就落在那里）。
    /// <para>
    /// 出厂给几条常见格式词、全设成中立（不生效）—— 让用户在设置页看得见能改哪些词，装机行为不变（同一语言里仍是默认
    /// 轨优先）。格式只在<em>同一语言内部</em>做取舍：语言优先级仍是第一位的，日语 TrueHD 不会因为英语是 Atmos 就输给它。
    /// </para>
    /// </summary>
    public List<KeywordRule> AudioFormatRules { get; set; } =
    [
        new("Atmos", TitlePreference.Neutral),
        new("TrueHD", TitlePreference.Neutral),
        new("DTS-HD", TitlePreference.Neutral),
        new("DTS", TitlePreference.Neutral),
        new("FLAC", TitlePreference.Neutral),
        new("7.1", TitlePreference.Neutral),
        new("5.1", TitlePreference.Neutral)
    ];

    /// <summary>
    /// Subtitle languages in priority order: the first one the file actually has wins. Stored as
    /// names (简体中文) rather than codes so one entry can cover the several codes and title
    /// spellings a 简体 track turns up with.
    /// <para>
    /// 简体中文 first, 中文 second and no 繁体中文 of its own: 中文 is the generic entry of the zh
    /// family, so once no 简体 track exists its 繁体 one is what 中文 matches — the user's call,
    /// 2026-09-05, that the default ask for simplified first and any Chinese after.
    /// </para>
    /// </summary>
    public List<string> SubtitleLanguages { get; set; } = ["简体中文", "中文"];

    /// <summary>
    /// 标题筛选规则（「先按语言选中文，再按标题决定不要双语/特效」，2026-09-22）。语言这一关选出「哪几条是中文」
    /// 之后，标题这一关在那批轨里再挑一遍：每条规则一个关键词加一种态度（优先/默认/候补），表内次序就是优先级，
    /// 见 <see cref="Playback.KeywordFilter"/>。
    /// <para>
    /// 出厂给「双语」「特效」两个词、都设成默认（中立、不生效）—— 用户按需自己改成排除或优先，装机行为不变。
    /// 语言从此不看标题（<see cref="TrackLanguagePriority.Matches"/> 的 <c>includeTitle</c>），两件事分家。
    /// </para>
    /// </summary>
    public List<KeywordRule> SubtitleTitleRules { get; set; } =
    [
        new("双语", TitlePreference.Neutral),
        new("特效", TitlePreference.Neutral)
    ];

    /// <summary>
    /// 视频文件名筛选规则（「参考标题筛选，在播放行为里新增视频文件名筛选」，2026-09-22）。一个条目有多版本（多个
    /// 文件）时，默认播哪一个由这组规则按文件名由上到下比较决定：表内越靠上的规则越优先，优先＝含这个词的版本优先，
    /// 候补＝往后排，默认＝不生效。和字幕标题筛选同一套 <see cref="KeywordRule"/>／<see cref="Playback.KeywordFilter"/>。
    /// <para>
    /// 出厂为空 —— 文件名里的发布组标记（REMUX、4K、枪版…）因人而异，不替用户假设，装机行为不变（默认仍是服务器
    /// 返回的第一个版本）。单版本条目一律不受影响。
    /// </para>
    /// </summary>
    public List<KeywordRule> VideoFileRules { get; set; } = [];

    /// <summary>
    /// 媒体源（多版本）的排法（「在设置中新增自定义选项，媒体源排序 默认/新入库在前」，2026-09-24）。
    /// 详情页媒体源行、播放器版本菜单、默认版本的「同分取第一」和自动换源候选共用同一张表，这一个选择四处
    /// 一起变 —— 判据在 <see cref="Emby.ItemDetail.OrderVersionsNewestFirst"/>。装机默认「新入库在前」，
    /// 就是当天那条「把最新入库的排前面」的原始要求；「默认」是服务器给的次序，想回去就拨回去。枚举存整数，
    /// 认不出来的值在 <c>SettingsMigration.Normalize</c> 里拨回装机默认。
    /// </summary>
    public Emby.MediaSourceOrder MediaSourceOrder { get; set; } = Emby.MediaSourceOrder.NewestFirst;

    /// <summary>When subtitles come on by themselves.</summary>
    public SubtitleMode SubtitleMode { get; set; } = SubtitleMode.Always;

    /// <summary>
    /// Select the file's own default subtitle when it has none of the preferred languages. On by
    /// default: a track labelled in a language nobody listed is still better than no subtitles at all.
    /// </summary>
    public bool SubtitleFallbackToDefault { get; set; } = true;

    /// <summary>
    /// 字幕字体, as a font *family* name — that is what mpv's <c>--sub-font</c> takes. v3 stored the
    /// path of a file under C:\Windows\Fonts here, which mpv quietly ignored: it went looking for a
    /// family literally called "C:\Windows\Fonts\msyh.ttc", found nothing, and fell back to sans-serif.
    /// It only ever looked right because the user's mpv.conf named a real family of its own. See
    /// <see cref="Infrastructure.FontFamilies.Resolve"/>.
    /// <para>
    /// <b>Microsoft YaHei UI Semibold since v19</b>（2026-09-22）— a Windows system font, so nothing has
    /// to be shipped for it. It is the face libass was already substituting for people who set the bundled
    /// 方正中等线简体 at the 细 step: 细 asks mpv for a nonexistent 「方正中等线简体 Light」, and libass's
    /// own fallback resolved the CJK glyphs to Microsoft YaHei UI Semibold (measured 2026-09-22). That
    /// look was preferred, so it is now the default in its own right — reached by name, not by accident.
    /// v19 also drops the bundled 方正中等线简体: nothing is shipped for the default any more. The seat's
    /// history: Microsoft YaHei up to v11, 方正中等线简体 from v12, back to Microsoft YaHei at v14, a
    /// one-day bundled Noto Sans CJK SC (whose Thin default instance made subtitles look far too thin) at
    /// v17, Microsoft YaHei again from v18, and Microsoft YaHei UI Semibold from v19. Whether it renders
    /// bold is <see cref="SubtitleBold"/>, not baked into the family. See
    /// <see cref="Infrastructure.FontFamilies.Default"/>.
    /// </para>
    /// </summary>
    public string SubtitleFontFamily { get; set; } = "Microsoft YaHei UI Semibold";

    /// <summary>
    /// 外观应用范围, mpv's <c>sub-ass-override</c>. Empty is mpv's own <c>scale</c>, under which an
    /// ASS/SSA subtitle keeps its own font, size and colours and every appearance field below this one
    /// does nothing to it; <c>force</c> makes them apply. See
    /// <see cref="Mpv.MpvOutputOptions.SubtitleStyleScopes"/> for what that cost before it was a row.
    /// </summary>
    public string SubtitleAssOverride { get; set; } = "";

    /// <summary>
    /// 字幕编码 for a text subtitle that is not valid UTF-8 (mpv's <c>sub-codepage</c>). Empty is mpv's
    /// own <c>auto</c>, i.e. detection.
    /// <para>
    /// <b>This was <c>gb18030</c> until 2026-09-05</b>, which turned detection off and read every
    /// non-UTF-8 subtitle as 简体 — a Big5 繁体 file came out as mojibake where <c>auto</c> reads it
    /// correctly. Measured both ways; see that catalogue's comment. <see cref="SettingsMigration"/>'s
    /// v11 step clears a stored <c>gb18030</c> for the same reason it was wrong here: nobody chose it.
    /// </para>
    /// </summary>
    public string SubtitleCodepage { get; set; } = "";

    /// <summary>
    /// 字幕字号 in mpv's own units (its default is 38, measured off the shipped libmpv 2026-09-05 —
    /// it was 55 for years and this comment said so long after mpv had changed it); 0 leaves mpv's
    /// default alone. Only ever applies to a text subtitle, and only to an ASS one when
    /// <see cref="SubtitleAssOverride"/> says 强制.
    /// </summary>
    public int SubtitleFontSize { get; set; } = 50;

    /// <summary>
    /// 字幕缩放 as a percentage of mpv's <c>sub-scale</c> (100 = 1.0, i.e. unsaid). Multiplies
    /// <see cref="SubtitleFontSize"/> for a text subtitle, and — this is why the row exists —
    /// <b>it is the one size control an ASS/SSA subtitle honours without 强制</b>, because mpv's
    /// default <c>sub-ass-override=scale</c> lets exactly this option through.
    /// </summary>
    public int SubtitleScalePercent { get; set; } = 100;

    /// <summary>
    /// 字幕加粗, mpv's <c>sub-bold</c>. On (<c>sub-bold=yes</c>) uses the family's real bold face when it
    /// has one and libass's synthetic emboldening otherwise; off leaves the family at its own weight.
    /// <para>
    /// A plain on/off since v20（2026-09-22, 「删掉字重选项，改为 sub-bold」）. v18–v19 briefly made this a
    /// three-step 字重 (细/常规/粗) realised by appending 「 Light」 to the family name or flipping
    /// <c>sub-bold</c>, but the 细 step only worked on families with a real Light face — the v19 default,
    /// Microsoft YaHei UI Semibold, has none, so 细 collapsed to 常规 there. mpv has no numeric weight
    /// option anyway (measured 2026-09-06: <c>sub-font-weight</c> does not exist), so this is back to the
    /// one thing libass can actually toggle. <see cref="SettingsMigration"/>'s v20 step maps a stored 字重
    /// of 粗 to on and everything else to off.
    /// </para>
    /// </summary>
    public bool SubtitleBold { get; set; }

    /// <summary>文字颜色 as <c>#RRGGBB</c>; empty leaves mpv's default alone.</summary>
    public string SubtitleColor { get; set; } = "#FFFFFF";

    /// <summary>
    /// 字体描边 in mpv units, as a string so 「不设置」 (the empty string — mpv's own 1.65) stays
    /// expressible. Free numeric input since 2026-09-06（「这个不用弄成固定的选项，改成输入数字」）:
    /// 0–10, canonicalised by <see cref="Mpv.MpvOutputOptions.ClampSubtitleUnit"/> on the way in and on
    /// every load, so a hand-edited file cannot hand mpv.exe a value it refuses to start on.
    /// </summary>
    public string SubtitleBorderSize { get; set; } = "0.5";

    /// <summary>描边颜色 as <c>#RRGGBB</c>; empty leaves mpv's default alone.</summary>
    public string SubtitleBorderColor { get; set; } = "#000000";

    /// <summary>
    /// 字幕阴影 offset in mpv units, as a string so 「不设置」 (the empty string — mpv's own default is
    /// no shadow at all) stays expressible. Free numeric input like 字体描边 next door; see
    /// <see cref="Mpv.MpvOutputOptions.ClampSubtitleUnit"/>. Drawn in <see cref="SubtitleBackColor"/> —
    /// mpv aliases the shadow colour to the plate colour — so with <see cref="SubtitleBackStyle"/> off
    /// this is the only thing that colour is used for.
    /// </summary>
    public string SubtitleShadowOffset { get; set; } = "0.5";

    /// <summary>
    /// 字幕底板, mpv's <c>sub-border-style</c>: empty for none, or a box mode. Off by default, which is
    /// the look this client has always had.
    /// <para>
    /// <b>Its absence is why 背景颜色 and 背景不透明度 drew nothing at all until 2026-09-05.</b> Since mpv
    /// 0.39 that colour is shared with the drop shadow and this option alone decides which of the two it
    /// paints; unset, mpv draws a shadow and no plate, whatever colour was chosen.
    /// </para>
    /// </summary>
    public string SubtitleBackStyle { get; set; } = "";

    /// <summary>
    /// 底板颜色 as <c>#RRGGBB</c>; empty leaves mpv's own (a black at about 69% opacity). Also the
    /// 阴影 colour — see <see cref="SubtitleShadowOffset"/>.
    /// <para>
    /// Shipped as <c>#000000</c> since v13 — the user's dictated 字幕外观 names this colour outright.
    /// With <see cref="SubtitleBackStyle"/> off it paints only the shadow, which mpv's own default
    /// already was to within the opacity, so the picture does not move; what changes is that the row
    /// reads a real colour out of the box instead of 「不设置」. The same reason is why the v13
    /// migration flips stored empties rather than leaving them.
    /// </para>
    /// </summary>
    public string SubtitleBackColor { get; set; } = "#000000";

    /// <summary>底板不透明度 in percent, applied to <see cref="SubtitleBackColor"/>.</summary>
    public int SubtitleBackOpacity { get; set; } = 60;

    /// <summary>
    /// Stretches an image subtitle (PGS/VOBSUB) to the window when the picture is wider than 16:9.
    /// Those tracks are authored for the full disc frame including its black bars, so on a cropped
    /// 2.39:1 encode their lines land below the bottom of the picture and never appear.
    /// </summary>
    public bool StretchWideImageSubtitles { get; set; } = true;

    /// <summary>
    /// How far → jumps, in seconds — the short pair, which is ← / → in both pipelines.
    /// <para>
    /// 5 rather than the 10 this shipped with up to v15: 「新增键盘上的左和右设置为播放进度快退五秒和
    /// 快进五秒」(用户令, 2026-09-20). A file still holding 10 under both this and
    /// <see cref="SeekBackwardSeconds"/> is a file where nobody ever chose one, so migration v16 flips it;
    /// a file holding 10 under only one of them is a decision and stays.
    /// </para>
    /// </summary>
    public int SeekForwardSeconds { get; set; } = 5;

    /// <summary>How far ← jumps, in seconds. See <see cref="SeekForwardSeconds"/>.</summary>
    public int SeekBackwardSeconds { get; set; } = 5;

    /// <summary>
    /// How far ↑ jumps, in seconds — the long pair, which is ↑ / ↓ in both pipelines.
    /// <para>
    /// 30 is the number the same dictation names (「上方向键和下方向键改为播放进度快进30秒和快退30秒」).
    /// A pair of its own rather than a second reading of the short one: ↑ and → are two different
    /// gestures on the same key cluster, so they are two numbers — the settings page shows four rows,
    /// and every one of the four arrow keys moves by exactly the number printed next to it.
    /// </para>
    /// </summary>
    public int SeekForwardLongSeconds { get; set; } = 30;

    /// <summary>How far ↓ jumps, in seconds. See <see cref="SeekForwardLongSeconds"/>.</summary>
    public int SeekBackwardLongSeconds { get; set; } = 30;

    /// <summary>
    /// Backs the resume position up by this many seconds, so a file continues a moment before where
    /// it stopped instead of mid-word. 0 resumes exactly.
    /// </summary>
    public int ResumeRewindSeconds { get; set; }

    /// <summary>
    /// What to do about a recognised 片头 or 片尾: offer a button, jump over it, or leave it alone.
    /// Defaults to asking — the sections come from chapter marks, and a wrong guess that only puts a
    /// button on screen costs nothing, while a wrong automatic jump skips part of the episode.
    /// </summary>
    public SkipSectionMode SkipSections { get; set; } = SkipSectionMode.Ask;

    /// <summary>
    /// Start the next episode by itself once one plays to the end — within the season only. At a season
    /// boundary the player goes back where it came from: 「最后一季的最后一集播放结束后，直接退出播放界面
    /// 或返回，不得自动续播该最后一季的第一集」（用户令 2026-09-18，跨季自动接播随令取消）.
    /// </summary>
    public bool AutoPlayNextEpisode { get; set; } = true;

    /// <summary>
    /// 「在设置中新增一个开始播放后自动全屏的功能」（用户的话，2026-09-13）。
    /// <para>
    /// 开播即全屏，作用在<b>正在播的那个窗口</b>上：集成模式是主窗口；独占模式（与外部 mpv.exe）的画面
    /// 在 mpv 自建的顶层窗口里，全屏由那扇窗兑现（是否真拿到 DXGI 独占由系统与驱动决定）。往谁身上使，
    /// 是播放页那头按「画面在哪」现场定的，这里不管。
    /// </para>
    /// <para>
    /// 只在<b>真正开始一个新的播放</b>时进一次全屏，不是每换一集都进：连播的下一集接着上一集的样子走，
    /// 用户在全屏里按 F 退出之后不该被下一集顶回去。<c>PlayerPage</c> 那一头是拿「一个新的播放」这个事件
    /// 说的（<c>PlaybackStarted</c>），不是拿「窗口该是全屏」那个状态说的。
    /// </para>
    /// <para>
    /// 装机默认关（false），所以旧的设置文件里没有这个键时行为一个像素都不变，也不需要为它加一条迁移 ——
    /// 同 <see cref="UiSettings.ShowHomeBanner"/> 那条。
    /// </para>
    /// </summary>
    public bool AutoFullscreenOnPlayback { get; set; }

    /// <summary>
    /// 拖窗口边沿改完大小、松开鼠标之后继续播放（用户令 2026-09-23）。关掉它＝松开之后停在暂停，要用户自己按播放。
    /// <para>
    /// <b>调整开始那一下一定冻，没有开关</b> —— 那是同一道令的另一半，判据在 <see cref="Playback.ResizeFreeze"/>：
    /// 拖动那一段的画面是靠合成变换拉伸着跟窗口走的，mpv 先停住，松手后盖上去的那张静止帧与撤掉时屏上那帧
    /// 才是同一帧。这一行只管松手之后往哪边走。
    /// </para>
    /// <para>
    /// <b>只对集成模式有效。</b>那一档的画面嵌在本窗口的视觉树里、窗口是应用自己的；独占模式与外部 mpv.exe
    /// 的画面在 mpv 自建的顶层窗口里，用户拖的是它那扇窗，走不到这条路上（<see cref="Playback.ResizeFreeze.Freezes"/>
    /// 里那一问）。
    /// </para>
    /// <para>
    /// 装机默认开，所以缺这个键的旧设置文件读出来就是「继续播放」，不需要为它加一条迁移 —— 同
    /// <see cref="AutoFullscreenOnPlayback"/> 那条。这一行改完当场生效：拖边收尾那一拍现读设置文件里的值
    /// （<c>PlayerViewModel.ResumeAfterWindowResize</c>），与 <see cref="AutoFullscreenOnPlayback"/> 去同一个
    /// 地方读，不经过任何通知管道。
    /// </para>
    /// </summary>
    public bool ResumeAfterWindowResize { get; set; } = true;

    /// <summary>
    /// 播放页置顶开关的持久化偏好：「对播放页面"是否置顶"的设置进行持久化保存，程序重启后仍保留上次
    /// 选择」（用户的话，2026-09-15）。
    /// <para>
    /// 记的是用户<b>拨过开关之后</b>的那一档：下一次播放接管窗口时按它把置顶立回去（真正动手的仍是
    /// <c>HostWindow.TopMost</c>，这里只是记账）。退出播放把窗口的置顶放下（照旧），但不动这份偏好 ——
    /// 放下是「还给浏览窗口」，不是「替用户改主意」。用户没拨过开关时它是默认的 false，行为与从前一致。
    /// </para>
    /// <para>
    /// 全屏进出让位那套规则（前台被挡时 HWND_NOTOPMOST）对<b>手动置顶</b>本来就不生效，持久化的偏好
    /// 落在窗口上就是手动置顶，两套规则互不干扰。
    /// </para>
    /// </summary>
    public bool PinWindowTopmost { get; set; }

    /// <summary>The smallest 字幕字号 worth offering; below this the text is not readable at any distance.</summary>
    public const int MinimumSubtitleFontSize = 16;

    /// <summary>The largest one the settings row offers.</summary>
    public const int MaximumSubtitleFontSize = 160;

    /// <summary>
    /// 字幕字号 as it is allowed to be stored: 0 (「不指定，由 mpv 自己决定」) or a readable size.
    /// <para>
    /// A named function rather than two clamps, because <b>two places have to agree</b>: the settings
    /// row, where the number is typed, and <see cref="SettingsMigration"/>, which repairs a file. Only
    /// the second existed until 2026-09-05, so a typed 5 was 5 for that whole session — the row's own
    /// note said 「最小 16」 while mpv was being sent 5 — and became 16 at the next launch, which is the
    /// worst of the two: the value the user saw was neither kept nor refused.
    /// </para>
    /// </summary>
    public static int ClampFontSize(int value) =>
        value <= 0 ? 0 : Math.Clamp(value, MinimumSubtitleFontSize, MaximumSubtitleFontSize);

    /// <summary>字幕缩放 range, in percent. Wide on purpose — a 4K film on a small window and a 1080p
    /// one across a projector want very different numbers out of the same 字号.</summary>
    public const int MinimumSubtitleScale = 50;

    /// <inheritdoc cref="MinimumSubtitleScale"/>
    public const int MaximumSubtitleScale = 300;
}

/// <summary>
/// Video-output options handed to mpv per launch. Most fields are opt-in: the empty string (or 0, or
/// false) means 「不设置」 and leaves mpv's own compiled-in default alone. See
/// <see cref="Mpv.MpvOutputOptions"/> for the option names these map to.
/// <para>
/// The defaults are not all empty, because there is no mpv.conf behind them any more: what the
/// client does not set here, nobody sets. The shipped values are the ones the user's own config used
/// to provide.
/// </para>
/// </summary>
public sealed class VideoSettings
{
    /// <summary>
    /// 画质预设, one of <see cref="Mpv.MpvOutputOptions.QualityPresets"/>. <c>default</c> means 「不套用」
    /// and is what an install that never touched this gets, so the picture only changes when asked.
    /// </summary>
    public string QualityPreset { get; set; } = "default";

    /// <summary>
    /// mpv's <c>vo</c>: 视频渲染. <c>gpu-next</c> is the default and the one this shader scheme is written
    /// for — ArtCNN's and CfL's own documentation both ask for it, and mpv-prescalers records a concrete
    /// failure for the alternative: <c>vo=gpu</c> with <c>gpu-api=d3d11</c> reports <c>rgba16f</c> as
    /// unavailable and the ravu chain will not load. <c>gpu</c> is the fallback, not a peer.
    /// </summary>
    public string Renderer { get; set; } = "gpu-next";

    /// <summary>
    /// mpv's <c>gpu-api</c>: 图形接口. <c>vulkan</c> since 2026-09-04, and this one is a measurement rather than
    /// a preference: same card, same film, same chain (<c>ArtCNN_C4F16 + CfL_Prediction_Lite</c>, 1080p into
    /// 2560×1440), only this option changed — <b>45 fps on vulkan, 8.7 on d3d11</b>, where 24 fps is what the
    /// film needs. ArtCNN's eight passes are all <c>//!COMPUTE</c> and that is the whole of the difference; the
    /// fragment-shader chains are within noise of each other on both. The comment that used to sit here said
    /// d3d11 was 「worth one A/B on a real film before switching」 — that A/B is
    /// <c>artifacts/shader-probe/gpu-findings.md</c>.
    /// <para>
    /// Left as a user-visible choice, not a constant: a machine whose driver has no vulkan support would get no
    /// picture at all, and 图形接口 in 设置 → 视频输出 is how it gets one back. <see cref="Mpv.MpvRenderCheck"/>
    /// says so in the log when a chain with compute passes runs on d3d11.
    /// </para>
    /// </summary>
    public string GpuApi { get; set; } = "vulkan";

    /// <summary>
    /// mpv's <c>hwdec</c>: 硬件加速. <c>auto-safe</c> from v7 on, where an empty string used to be the
    /// shipped value — and an empty string means mpv's own default, which is <c>no</c>, that is pure software
    /// decoding. Nobody chose that; it is what a fresh install happened to carry, and it is the one setting
    /// that makes a 4K file plus a shader chain harder than it needs to be. <c>auto-copy-safe</c> from
    /// 2026-10-11, per the 解码 reference document the user handed over: the copy modes are the ones that
    /// let every filter and shader act on the decoded frame (「不推荐选用……无-copy 的原生硬解，目的是确保全部
    /// 设置/滤镜/着色器正常作用（有些不支持）」), and this client's whole picture pipeline is shaders — a
    /// decoder surface a chain cannot sample would quietly half the feature. mpv still falls back to software
    /// decoding by itself when the hardware path fails, and the official manual's own preference for software
    /// decoding stays true: hardware decoding is what reaches for when CPU is the bottleneck, not a choice
    /// worth defending once it misbehaves.
    /// <para>
    /// Only the default moved (v7 did the same, empty → auto-safe, without rewriting anybody); a settings.json
    /// that already holds a value keeps its decoding behavior. The redundant empty choice is now normalized
    /// to explicit no, so it stays pure software decoding. Unlike 图形接口 (v8) and 高帧率回退 (v9), this one
    /// costs CPU rather than changing what anybody was promised, and <see cref="Mpv.MpvRenderCheck"/> already
    /// says so in the log and the self-check report when it is off.
    /// </para>
    /// </summary>
    public string HardwareDecoding { get; set; } = "auto-copy-safe";

    /// <summary>
    /// mpv's <c>video-output-levels</c>: 色彩范围. 「色彩范围默认使用 PC(0-255)」 — PC range is what a
    /// desktop monitor over DisplayPort/HDMI expects, and it is also the case a mislabelled file gets
    /// wrong most visibly (grey blacks and clipped highlights), so it is stated rather than inferred.
    /// </summary>
    public string OutputLevels { get; set; } = "full";

    /// <summary>mpv's <c>video-sync</c>: 视频显示同步.</summary>
    public string VideoSync { get; set; } = "";

    /// <summary>反交错：no、auto 或 yes；强制开启会处理逐行视频。</summary>
    public string DeinterlaceMode { get; set; } = "no";

    /// <summary>mpv's <c>interpolation</c>: smooths judder, and needs display sync to do anything.</summary>
    public bool Interpolation { get; set; }

    /// <summary>
    /// 插值算法 (mpv's <c>tscale</c>), one of <see cref="Mpv.MpvOutputOptions.InterpolationKernels"/>.
    /// <c>oversample</c> remains the shipped default; Mitchell and bicubic offer progressively more smoothing
    /// at the cost of blur. These filters mix existing frames and do not perform motion compensation.
    /// <para>
    /// Only reaches mpv with <see cref="Interpolation"/> on. No 「不设置」 entry, so
    /// <c>SettingsMigration.Normalize</c> maps unknown or removed choices to <c>oversample</c>, so the
    /// settings page and playback always name the same supported choice.
    /// </para>
    /// </summary>
    public string Tscale { get; set; } = "oversample";

    /// <summary>
    /// 补帧模式（2026-10-26）：关闭、平滑插值、运动补偿。这张卡是独立的一组偏好，不改
    /// <see cref="Interpolation"/> 与 <see cref="Tscale"/> —— 关掉它只关它自己，基础输出里的插值偏好原样保留。
    /// 平滑插值仍走 mpv 的相邻帧混合，运动补偿是随包 FFmpeg 的 CPU <c>minterpolate</c>，不是 GPU／AI 补帧。
    /// 枚举与归一化都在 <see cref="Mpv.FrameInterpolationOptions"/>。
    /// </summary>
    public Mpv.FrameInterpolationMode FrameInterpolationMode { get; set; } = Mpv.FrameInterpolationMode.Off;

    /// <summary>
    /// 补帧目标帧率，只在运动补偿下生效。存储端与下发端共用 <see cref="Mpv.FrameInterpolationOptions.ClampFps"/>
    /// 那一对常量（24–120），手改的设置文件不会让 mpv 收到一个它不接受的数。
    /// </summary>
    public int FrameInterpolationFps { get; set; } = Mpv.FrameInterpolationOptions.DefaultFps;

    /// <summary>
    /// 运动补偿的计算质量：只决定 minterpolate 的运动估计参数取舍，不是实测性能评级，也不是画质档位。
    /// </summary>
    public Mpv.FrameInterpolationQuality FrameInterpolationQuality { get; set; } = Mpv.FrameInterpolationQuality.Balanced;

    /// <summary>抖动算法 (mpv's <c>dither</c>); see <see cref="Mpv.MpvOutputOptions.Dithers"/>.</summary>
    public string Dither { get; set; } = "fruit";

    /// <summary>去色带 (mpv's <c>deband</c>), including the client's own 「自动」; see
    /// <see cref="Mpv.MpvOutputOptions.DebandModes"/>.</summary>
    public string Deband { get; set; } = MpvOutputOptions.Auto;

    /// <summary>What to do with an HDR source; see <see cref="Mpv.MpvOutputOptions.HdrModes"/>.</summary>
    public string HdrMode { get; set; } = "tonemap";

    /// <summary>空值继承画质预设；其余是 mpv 的色调映射算法。</summary>
    public string ToneMapping { get; set; } = "";

    /// <summary>HDR 输出峰值，单位 nits；null 交给显示器检测，不套用到 SDR 映射。</summary>
    public double? HdrPeakNits { get; set; }

    /// <summary>HDR 环境中的参考白亮度，单位 nits；null 跟随系统。</summary>
    public double? HdrReferenceWhiteNits { get; set; }

    public double? HdrSubtitleNits { get; set; }

    /// <summary>PGS/VobSub 图形字幕亮度，单位 nits；null 保持内核默认。</summary>
    public double? HdrImageSubtitleNits { get; set; }

    public string HdrComputePeak { get; set; } = "";

    public double? HdrContrastRecovery { get; set; }

    /// <summary>默认保留 RPU；关闭仅供具有兼容基础层的杜比视界片源排障。</summary>
    public bool DolbyVisionMetadata { get; set; } = true;

    public bool DolbyVisionEnhancementLayer { get; set; } = true;

    public bool Hdr10PlusMetadata { get; set; } = true;

    /// <summary>auto、8 或 10；与抖动算法分开，不改变显卡链路位深。</summary>
    public string DitherDepth { get; set; } = "auto";

    public string DebandStrength { get; set; } = "low";

    /// <summary>
    /// 自动 ICC 校色 (mpv's <c>icc-profile-auto</c>): hand mpv the ICC profile Windows currently has set for
    /// the display and let it colour-manage against it.
    /// <para>
    /// <b>Off by default, and that is a decision rather than an oversight.</b> <see cref="Mpv.MpvBaseline"/>
    /// states <c>icc-profile-auto=no</c> on every launch and this switch is what lifts it; the reasons for
    /// the floor are that the client asks Emby what the source is and picks tone mapping from that
    /// (<see cref="HdrMode"/>), which a desktop profile silently overrides, and that Windows' 「default
    /// display profile」 is usually whatever the monitor's driver dropped there rather than a measurement —
    /// so switching this on for everybody would shift colour on machines nobody ever calibrated.
    /// </para>
    /// <para>
    /// It also costs HDR 直通 when both are on: with a profile loaded mpv maps into the profile's space, so
    /// the display no longer receives the HDR signal to map itself. On a screen that really has been
    /// profiled it is the right answer, which is why it is a switch and not a hard-coded <c>no</c>.
    /// </para>
    /// </summary>
    public bool IccProfileAuto { get; set; }

    /// <summary>
    /// Falls back to <c>video-sync=audio</c>, interpolation with it, in the two cases where display sync is
    /// charged more than it returns: a source above about 47fps, which has no spare cadence left to resample
    /// into, and a screen above <see cref="HighRefreshRateLimitHz"/>, where the whole final render pass starts
    /// running once per refresh for judder there is barely any of. Both halves are
    /// <see cref="Mpv.MpvOutputOptions.ResolveSync"/>'s.
    /// <para>
    /// <b>The refresh-rate half arrived on 2026-09-04, and v9 turns the switch back on for anyone who had it
    /// off.</b> Up to then it only meant 「高帧率片源」, so a stored <c>false</c> was an answer to a question
    /// about frame rates and says nothing about screens — and on this machine it was silently declining the
    /// very rule that was measured to halve GPU load on a 144Hz panel (24.7% against 50.1%), with no line
    /// anywhere saying so, because the log only speaks when the rule fires. Same reasoning as v8's 图形接口
    /// move: a value nobody chose for the meaning it now has is not a preference. Switching it off after v9 is
    /// a real choice — the settings row names both halves — and nothing touches it again.
    /// </para>
    /// </summary>
    public bool HighFrameRateAudioSync { get; set; } = true;

    /// <summary>插值关闭阈值的下限（Hz）。24 是还像刷新率的最低一档 —— 填了它，任何一块现实的屏都算「超过」。</summary>
    public const int MinimumHighRefreshRateLimitHz = 24;

    /// <summary>插值关闭阈值的上限（Hz）。目前没有哪块屏超过它，再大的数只是把规则关掉的一种绕路写法。</summary>
    public const int MaximumHighRefreshRateLimitHz = 1000;

    /// <summary>插值关闭阈值的装机默认 —— 从前写死在 <see cref="Mpv.MpvOutputOptions"/> 里的那个数，量出来的那笔账见这一项。</summary>
    public const int DefaultHighRefreshRateLimitHz = 120;

    /// <summary>
    /// 插值关闭阈值（Hz）：播放窗口所在屏幕的刷新率<b>大于</b>这个数，就把插值连同显示同步一起收回、回到音频同步
    /// —— 「在设置中新增自定义输入框，显示器刷新率大于该数值时关闭插值」（2026-09-10）。
    /// <para>
    /// <b>120 是从前的写死值，也是装机默认。</b>它量出来过（2026-09-04，2560×1440、24fps 片源、gpu-next +
    /// vulkan、不挂链，<c>artifacts/shader-probe/interp-cost.ps1</c>）：显示同步把 mpv 最后一趟渲染（混帧、色彩
    /// 编码、抖动，约 1.1 毫秒）从「每个视频帧一次」改成「每次刷新一次」，144Hz 上 Windows 自己的进程 GPU 计数器
    /// 实测 24.7% 对 50.1%；而 144 ÷ 24 = 6.000，没有节奏要补，<c>vo-passes</c> 几乎只报「frame mixing (1
    /// frame)」。120 也是用户自己那份 mpv.conf 里 <c>[fps-fix]</c> 写的数 —— 「外置 mpv 开了插值也不费显卡」正是
    /// 因为那头同一条规则先出手了。别拿 <c>--interpolation-threshold</c> 顶这一行：在 gpu-next 上量过没有可测的
    /// 差别，而且账单在「按刷新率重跑的那趟渲染」，不在混帧本身。
    /// </para>
    /// <para>
    /// 做成输入框之后这个数就是用户的了 —— 一块 60Hz 的屏想彻底关掉它，填 24 就行。只在
    /// <see cref="HighFrameRateAudioSync"/> 开着时生效：那个开关是这一整条回退规则（含片源 47fps 那一半）的总闸，
    /// 关掉它是「任何屏幕都把显示同步还给我」的逃生口。缺键（旧文件升级）读出来是 0，Normalize 把 0 读成装机
    /// 默认 120 —— 缺键的人一个数都不许变。
    /// </para>
    /// </summary>
    public int HighRefreshRateLimitHz { get; set; } = DefaultHighRefreshRateLimitHz;

    /// <summary>Demuxer cache in MiB; 0 leaves mpv's own default alone.</summary>
    public int NetworkCacheMegabytes { get; set; }

    /// <summary>
    /// 宽于 16:9 的片源默认裁切填充 — mpv's <c>panscan=1.0</c>, applied only to a source wider than
    /// <see cref="Mpv.MpvOutputOptions.WideAspect"/>. Off by default, and it crops: a 2.35:1 film fills a 16:9
    /// screen by losing the left and right edges of every frame.
    /// <para>
    /// <b>This is the one piece of 画面几何 worth remembering across files.</b> Aspect override, rotation, pan,
    /// zoom and the colour controls are all on the player's right-click menu and none of them persist — every
    /// playback is a fresh mpv under <c>--no-config</c>. Mainstream players remember all of them; here almost
    /// none of it is missed, because a window that is playing something is held to the shape of the picture
    /// (<c>HostWindow.PictureAspect</c>), so there is normally nothing to letterbox. The exception is exactly
    /// this: a 2.35:1 film always has bars, and
    /// whether to trade the edges of the frame for them is a standing preference rather than a per-film one.
    /// </para>
    /// <para>
    /// Deliberately <b>not</b> a general 「remember the geometry」 feature — 「别把六个 mpv 属性全搬进设置」. One
    /// switch, one threshold, and the player menu keeps the per-film override it always had.
    /// </para>
    /// </summary>
    public bool FillWideSources { get; set; }
}

/// <summary>Audio-output options handed to mpv per launch; same opt-in rule as <see cref="VideoSettings"/>.</summary>
public sealed class AudioSettings
{
    /// <summary>mpv's <c>audio-channels</c>: 扬声器布局.</summary>
    public string Channels { get; set; } = "";

    /// <summary>
    /// Codecs passed through to the receiver untouched (mpv's <c>audio-spdif</c>). Empty means mpv
    /// decodes everything itself, which is the right answer for ordinary speakers and headphones.
    /// </summary>
    public List<string> PassthroughCodecs { get; set; } = [];

    /// <summary>mpv's <c>ad-lavc-ac3drc</c>: 动态范围压缩, as a string so 「不设置」 is expressible.</summary>
    public string DynamicRange { get; set; } = "";

    /// <summary>mpv's <c>audio-exclusive</c>: takes the device over for bit-perfect output.</summary>
    public bool ExclusiveMode { get; set; }

    /// <summary>
    /// mpv's <c>audio-device</c> — a name out of <see cref="Playback.AudioDeviceCatalogue"/>, empty for
    /// 「跟随系统默认设备」 (which is mpv's own <c>auto</c>).
    /// <para>
    /// <b>What it fixes:</b> until this existed, <see cref="ExclusiveMode"/> could only ever take over
    /// 「whatever Windows calls the default right now」. Plug in headphones and which endpoint got taken over
    /// depended on that moment's default — invisible, and not choosable.
    /// </para>
    /// <para>
    /// <b>A stored name that is gone must fall back to the default rather than to silence</b>, because this
    /// list changes whenever a pair of headphones is plugged in or out. The settings row handles that the way
    /// every other unrecognised value is handled (an entry of its own marked 「设置文件中的值」), and mpv itself
    /// falls back to <c>auto</c> for a device it cannot open — so an unplugged device costs a line in the log,
    /// not a silent film.
    /// </para>
    /// </summary>
    public string Device { get; set; } = "";

    /// <summary>Global audio delay in milliseconds; positive means the audio comes later.</summary>
    public int DelayMilliseconds { get; set; }

    /// <summary>
    /// 音量, 0–<see cref="MaxVolume"/>, as the player was last left. Not a knob on the settings page but a
    /// remembered one: every playback starts a fresh mpv with its own config blocked, so mpv's idea of the
    /// volume is always 100 and nothing but this carries a level from one file to the next
    /// (「换个媒体播放音量会变回 100」).
    /// </summary>
    public int Volume { get; set; } = 100;

    /// <summary>
    /// How far the volume rail goes. <b>130 is mpv's own <c>volume-max</c> default</b>, which is the whole
    /// reason for that number: staying on it means the client never has to send <c>volume-max</c> at all, so
    /// there is no second place where the ceiling is stated and no way for the two to disagree.
    /// <para>
    /// The rail was pinned at 100 by this client, not by mpv — six places said so independently, and one of
    /// them (<see cref="Mpv.MpvOutputOptions"/>'s <c>volume</c> line) said it in a way that made a stored 130
    /// unsendable, so the slider would have shown 130 while mpv sat at 100. Everything reads this constant now.
    /// </para>
    /// <para>
    /// <b>Raising it further would mean sending <c>volume-max</c> too</b>, and above 130 the amplification is
    /// pure clipping on anything already mastered near full scale — worth deciding deliberately rather than by
    /// nudging this number.
    /// </para>
    /// </summary>
    public const int MaxVolume = 130;

    /// <summary>
    /// 音量均衡 — one of <see cref="Mpv.MpvOutputOptions.VolumeNormalizers"/>, empty for 「不启用」. Reaches
    /// mpv as an <c>af</c> filter chain; the filter strings live in that catalogue, which the player's
    /// 「切换 下混滤镜」 row shares, so the two cannot drift apart.
    /// <para>
    /// This is the cross-codec answer to 「对白听不清」 that <see cref="DynamicRange"/> is not:
    /// <c>ad-lavc-ac3drc</c> is an AC-3 decoder option and does nothing at all to a DTS, TrueHD, AAC or FLAC
    /// track. It lives in the settings rather than only on the player menu because the output here is desktop
    /// speakers or headphones, which makes it a standing preference rather than something to dial in per film
    /// — and the menu does not persist (every playback is a fresh mpv under <c>--no-config</c>).
    /// </para>
    /// </summary>
    public string VolumeNormalize { get; set; } = "";

    /// <summary>
    /// mpv's <c>audio-normalize-downmix</c>: whether a 5.1 track being folded to two channels is normalised
    /// rather than summed. Off by default, which is also mpv's own default.
    /// <para>
    /// <b>Off on purpose, and it is a trade rather than a bug.</b> On, dialogue stops being buried under the
    /// effects channel; the cost — upstream's own words — is that everything gets quieter, because the sum has
    /// to be scaled to fit. That is a judgement about a particular film's mix, so it is a switch with the cost
    /// written on it, not a default.
    /// </para>
    /// <para>
    /// <b>It only bites when mpv is the one doing the downmix</b>, which on this build it is — measured
    /// 2026-09-04 with the external mpv on a synthetic 5.1 AC-3 clip
    /// (<c>artifacts/shader-probe/downmix.ps1</c>): the client never sets <c>ad-lavc-downmix</c>, so the
    /// decoder hands out <c>5.1(side) 6ch</c> and mpv's own <c>[convert]</c> stage folds it to
    /// <c>stereo 2ch</c>. The counter-case is in the same run: with <c>--ad-lavc-downmix=yes</c> the decoder
    /// emits stereo itself and <c>[convert]</c> reports <c>(disabled)</c> — there, and only there, would this
    /// switch do nothing. So there is nothing for the client to second-guess, and <c>ad-lavc-downmix</c> is
    /// deliberately left alone rather than pinned to a value from C#.
    /// </para>
    /// </summary>
    public bool NormalizeDownmix { get; set; }
}

/// <summary>
/// 着色器档位: how the chain for one playback is chosen. The chains themselves are shipped C# data —
/// <see cref="Mpv.ShaderGroupCatalog"/> — so this holds only the things that are genuinely the user's to say:
/// whether shaders run at all, how much GPU there is to spend, whether to override the automatic pick, and
/// whether a DVD-era source gets cleaned up first.
/// <para>
/// Up to v6 this held four group names and two resolution thresholds, and the rule was 「按片源分辨率挑一个
/// 具名组」. That rule could not work: 「4K 片源」 and 「480p 片源」 only mean something against a screen size,
/// and nothing here ever looked at the output. The names and the thresholds are gone — the deserializer drops
/// keys it has nowhere to put — and what replaced them is <see cref="Mpv.ShaderTier.Measure"/>.
/// </para>
/// </summary>
public sealed class ShaderAutomationSettings
{
    /// <summary>
    /// 启用着色器. Took over from v6's 「所有视频默认启用」: with a chain for every scale factor there is no
    /// 「默认组」 left for that switch to apply, so the honest question is just on or off.
    /// <para>
    /// <b>装机默认是关 —— 用户 2026-09-05 定的（「恢复默认后着色器默认关闭」）。</b>所以「恢复默认」那颗按钮
    /// 交出来的也是关（<see cref="SettingsReset"/> 抄的就是这个初始值），而画质那一头默认只剩
    /// <see cref="VideoSettings.QualityPreset"/> 一个开关 —— 那一项开着色器与否都照样生效，两者互不改写。
    /// </para>
    /// <para>
    /// 存过的文件不受影响：v7 起这个键就写进设置文件了，所以已经开着的人照旧开着。<c>SettingsMigration</c> 里
    /// v7 那一段把 v6 的旧开关照抄过来（开是开、关是关），正是因为装机默认从「开」变成了「关」——
    /// 少了那一手，一份 v6 文件里明明开着的着色器会在升级时被这个新默认悄悄关掉。
    /// </para>
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 显卡档位 — which column of the table is in play. Stated by the user rather than probed from the
    /// adapter's name: that is a list nobody can finish maintaining, and there is one machine to answer for.
    /// Defaults to <see cref="Mpv.GpuTier.Low"/>, which is both this machine's answer (a 5600G's Vega) and
    /// what a missing key reads as.
    /// </summary>
    public Mpv.GpuTier Gpu { get; set; }

    /// <summary>
    /// A hand-picked chain that overrides the automatic one, as a <see cref="Mpv.ShaderGroup.Id"/>; empty
    /// means 「按放大倍数自动挑」, which is the normal case. The id names the row of the table, not the cell,
    /// so changing <see cref="Gpu"/> keeps the override and moves it to the other column.
    /// <para>
    /// This is what the player's 着色器 submenu writes when a chain is picked mid-film, which is how a
    /// side-by-side comparison is made. An id this table no longer has falls back to 自动 in
    /// <c>SettingsMigration.Normalize</c> rather than to 「no shaders」.
    /// </para>
    /// </summary>
    public string ManualGroup { get; set; } = "";

    /// <summary>Use the 动画 half of the table when the item's genres/tags look animated.</summary>
    public bool AutoAnimeProfile { get; set; } = true;

    /// <summary>
    /// 老片源修复: whether a source of 576 lines or fewer gets <c>hdeband</c> — and, above the low column,
    /// <c>nlmeans_light</c> — in front of its chain. On by default, because a DVD transfer has banding and
    /// grain whatever it is being scaled to, and off is here because denoising is the one thing on this list
    /// that can be said to remove detail rather than add it.
    /// <para>
    /// A separate axis from <see cref="Mpv.UpscaleTier"/> on purpose: an NTSC DVD (480 lines) blown up to
    /// 1080p is 2.25× and a PAL DVD (576 lines) of the same film is 1.88×, so folding 去带 into 大倍数 would
    /// treat the two regions of one disc completely differently.
    /// </para>
    /// </summary>
    public bool RestoreVintageSources { get; set; } = true;

    /// <summary>
    /// Applies no chain at all to an 8K source. Nothing on an iGPU decodes 8K and runs a shader chain at the
    /// same time, and the picture is being shrunk by a factor of three anyway.
    /// </summary>
    public bool DisableForUltraHighRes { get; set; } = true;

    /// <summary>Case-insensitive substrings matched against Genres and Tags.</summary>
    public List<string> AnimeKeywords { get; set; } =
        ["动画", "动漫", "国漫", "番剧", "卡通", "Anime", "Animation", "Cartoon", "アニメ"];

    public ShaderAutomationSettings Snapshot() => new()
    {
        Enabled = Enabled,
        Gpu = Gpu,
        ManualGroup = ManualGroup,
        AutoAnimeProfile = AutoAnimeProfile,
        RestoreVintageSources = RestoreVintageSources,
        DisableForUltraHighRes = DisableForUltraHighRes,
        AnimeKeywords = [.. AnimeKeywords]
    };

    /// <summary>Width above which <see cref="DisableForUltraHighRes"/> applies.</summary>
    public const int UltraHighResWidth = 7000;

    /// <summary>
    /// Which of the four kinds of file this is — the two axes that follow what is being played rather than
    /// what anybody picked, and that pick a cached column of <see cref="Mpv.ShaderGroupCatalog"/>.
    /// <para>
    /// Named and public because two callers need the same answer and only one of them is choosing a chain:
    /// <see cref="Resolve"/>, and the player's 着色器 menu, which lists what a row <b>would</b> load for the
    /// file on screen. That menu used to read the axes back off the chain in force, which is null whenever
    /// 启用着色器 is off or the source is 8K — and it then offered a 60fps animated file the ArtCNN row that
    /// <see cref="Mpv.ShaderTier.IsFastMotion"/> exists to keep out of it, and a DVD a row with no
    /// <c>hdeband</c>. The rule has to live in one place, and it is a property of the file, so it lives here.
    /// </para>
    /// </summary>
    /// <param name="sourceHeight">The source's stored height, 0 when Emby never probed it.</param>
    /// <param name="sourceFrameRate">The source's frame rate, 0 when Emby never probed it.</param>
    public (bool Vintage, bool FastMotion) Kind(int sourceHeight, double sourceFrameRate) =>
        (RestoreVintageSources && Mpv.ShaderTier.IsVintage(sourceHeight),
            Mpv.ShaderTier.IsFastMotion(sourceFrameRate));

    /// <summary>
    /// The chain to apply and why, or null with a reason for none. The reason is the one line 任务书 3.7 asks
    /// for — it reaches the launch log, the 诊断 page and the player's 播放信息 panel, so it is written to be
    /// read rather than parsed.
    /// </summary>
    /// <param name="looksAnimated">What the metadata says, before <see cref="AutoAnimeProfile"/> is consulted.</param>
    /// <param name="outputWidth">The picture's target size; 0 when nobody could say — see <see cref="Mpv.ShaderTier.Measure"/>.</param>
    /// <param name="sourceFrameRate">
    /// The source's frame rate, 0 when Emby never probed it. Above <see cref="Mpv.ShaderTier.FastMotionFps"/> the
    /// 动画 rows give up their CNN upscaler — see <see cref="Mpv.ShaderGroup.FastMotion"/>.
    /// </param>
    /// <param name="current">
    /// The tier already in force, when this is a re-measurement after the window changed size. Makes the
    /// boundaries sticky by <see cref="Mpv.ShaderTier.Hysteresis"/>; null for a fresh playback.
    /// </param>
    public (Mpv.ShaderGroup? Group, string Reason, Mpv.UpscaleMeasure Measure) Resolve(
        bool looksAnimated,
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight,
        double sourceFrameRate = 0,
        Mpv.UpscaleTier? current = null)
    {
        if (!Enabled) return (null, "着色器已关闭", default);

        if (DisableForUltraHighRes && (sourceWidth >= UltraHighResWidth || sourceHeight >= 3000))
            return (null, "片源接近 8K，着色器只会拖慢解码，已全部关闭", default);

        var animated = looksAnimated && AutoAnimeProfile;
        var (vintage, fastMotion) = Kind(sourceHeight, sourceFrameRate);
        var measure = Mpv.ShaderTier.Measure(sourceWidth, sourceHeight, outputWidth, outputHeight, current);

        // A hand-picked chain names the row itself, so the 动画 detection is not consulted for it — overriding
        // the pick and then having the metadata override the override is not an override. 老片源 and 高帧率
        // still apply to automatic cells; explicitly named fixed configurations keep their own chain.
        var picked = Mpv.ShaderGroupCatalog.Find(ManualGroup, Gpu, vintage, fastMotion);
        var group = picked ?? Mpv.ShaderGroupCatalog.Resolve(animated, measure.Tier, Gpu, vintage, fastMotion);

        var line = Mpv.ShaderTier.Explain(measure, group.Animated, Gpu, outputWidth, outputHeight, group);
        if (group.Vintage) line += " · 老片源修复";

        // Only worth a word when it actually changed the chain. On the 真人 rows and in 缩小 the two halves of
        // the table are the same chain, so 「已换便宜的链」 there would be a line about nothing.
        if (group.FastMotion && group.Animated && group.Tier != Mpv.UpscaleTier.Shrink)
            line += $" · 高帧率片源（超过 {Mpv.ShaderTier.FastMotionFps:0} fps），已换成便宜的链";

        if (picked is not null)
            line = group.NamedId.Length > 0 ? $"手动指定 · {group.Name} · {line}" : $"手动指定 · {line}";

        return (group, line, measure);
    }
}

/// <summary>
/// A MoviePilot service this app may talk to — 「接入 MoviePilot」, the user's call 2026-09-14.
/// <para>
/// Deliberately not a member of <see cref="ServerProfile"/>: MoviePilot is not an Emby server and is not
/// per-account. It is one service on the LAN that the one person using this app has one login for, and the
/// media-server connection it holds internally is its own business. Hanging it off a server profile would
/// mean asking which Emby server a MoviePilot belongs to, and there is no good answer to that.
/// </para>
/// <para>
/// Credentials are stored DPAPI-wrapped through the same <see cref="ISecretProtector"/> the Emby ones use —
/// see <see cref="MoviePilotCredentials"/>, which is the only place that unwraps them.
/// </para>
/// </summary>
public sealed class MoviePilotSettings
{
    public event EventHandler? ConnectionChanged;

    /// <summary>
    /// Whether the settings page may talk to MoviePilot at all. Off by default: a fresh install has no
    /// MoviePilot, and a client that probes an address nobody configured on every visit to the settings
    /// page would be spending a network timeout on a service that was never there.
    /// </summary>
    public bool Enabled
    {
        get;
        set { if (field == value) return; field = value; ConnectionChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>
    /// The API address as typed, e.g. <c>192.0.2.10:3001</c>. Normalised through
    /// <see cref="MoviePilot.MoviePilotAddress"/> on the way in and out; stored as the user's own text so a
    /// half-typed address survives a restart.
    /// </summary>
    public string Url
    {
        get;
        set { if (field == value) return; field = value; ConnectionChanged?.Invoke(this, EventArgs.Empty); }
    } = "";

    /// <summary>The MoviePilot account this app signs in as.</summary>
    public string Username
    {
        get;
        set { if (field == value) return; field = value; ConnectionChanged?.Invoke(this, EventArgs.Empty); }
    } = "";

    /// <summary>DPAPI-wrapped password. Empty when the user has not saved one.</summary>
    public string ProtectedPassword
    {
        get;
        set { if (field == value) return; field = value; ConnectionChanged?.Invoke(this, EventArgs.Empty); }
    } = "";

    /// <summary>
    /// The signed-in user's display name from the last successful test, for the card to report. Not a
    /// credential — nothing authenticates with it.
    /// </summary>
    public string LastUserName { get; set; } = "";

    /// <summary>
    /// When the connection last succeeded, or null for never. Shown on the card so a stale 「上次测试通过」
    /// cannot be mistaken for the present state of the network.
    /// </summary>
    public DateTimeOffset? LastConnected { get; set; }

    [JsonIgnore]
    public bool HasSavedPassword => ProtectedPassword.Length > 0;
}

public sealed class UiSettings
{
    public Dictionary<string, List<RecentSearchEntry>> RecentSearches { get; set; } = new(StringComparer.Ordinal);

    public int PageSize { get; set; } = 100;

    /// <summary>
    /// 选中那套主题的 id，见 <see cref="Theming.UiThemes"/>。
    /// <para>
    /// 存 id 而不是存一堆色值：主题的定义在代码里，这里只记「用户选了哪一套」。认不出来的 id 由
    /// <c>SettingsMigration.Normalize</c> 拨回默认那套 —— 手改过的文件、装回旧版本都会落到这条路上。
    /// </para>
    /// </summary>
    public string Theme { get; set; } = Theming.UiThemes.DefaultId;

    /// <summary>界面动效开关；默认保留现有动画，关闭时直接显示最终状态。仍遵循 Windows 的动画设置。</summary>
    public bool AnimationsEnabled { get; set; } = true;

    /// <summary>主窗口左侧导航；默认关闭，旧配置缺少此项时继续使用标题栏入口。</summary>
    public bool SidebarMode { get; set; }

    // 「海报宽度（像素）」那一行 2026-09-05 按用户的话删掉了（「删掉设置中的海报宽度」）：卡片从此固定用
    // CardSize 的默认尺寸（海报 170、剧照 300、演职人员人像 124）。这里不留一个存而不用的键 —— 反序列化碰到
    // 没处放的键本来就不出声，所以旧设置文件里留下的那一行照旧读得起来，只是没人再听它的。

    // 「显示观看状态标记」（`ShowWatchedIndicators`）2026-10-16 按同一句话的下一轮删掉了（「删掉这个开关，
    // 改为默认开启」）：海报角上的已看、收藏与未看集数从此一直画。**同一个理由不留键** —— 旧设置文件里存着
    // 的 `false` 照旧读得起来、也没人再听它的，也就是升级之后角标自己回来，正是用户要的那一件。

    /// <summary>
    /// 隐藏功能下方说明（屏上原叫「精简模式」）—— 「在设置中新增一个精简模式，开启后隐藏各项功能下方的
    /// 说明」（用户的话，2026-09-25）；「把极简模式改名为隐藏功能下方说明」（用户的话，2026-09-26），
    /// 名字直接说它干什么，键名 CompactMode 不动。
    /// <para>
    /// 开着的时候，设置页每一条行标签下面那行小字说明整页收起，只留标签和控件；开关本身就在设置 → 界面卡上，
    /// 拨一下当场生效，不用重开设置页。收的是「各条功能下面的说明」：卡片标题底下那一句分区介绍不属于哪一条
    /// 功能，照旧显示。开关行自己的说明也在「各项功能」之列 —— 开着它，连介绍它自己的那行小字一起收走。
    /// </para>
    /// <para>
    /// 装机默认关，所以缺这个键的旧设置文件读出来就是关，行为一个像素都不变，也不需要为它加一条迁移 ——
    /// 同 <see cref="ShowHomeBanner"/> 那条。恢复默认（<see cref="SettingsPreferences"/>）对界面组是「默认当
    /// 偏好、例外具名留下」的整组抄法，这一项自动跟着回关，不用具名豁免。
    /// </para>
    /// </summary>
    public bool CompactMode { get; set; }

    /// <summary>
    /// 启动时自动检查有没有新版本 —— 「在设置中新增开启应用后自动检查有没有新版本的功能」。
    /// <para>
    /// 开着的时候，程序起来之后会去项目的发布页问一句有没有比这一版新的；有新版本就弹一条提示，带一颗
    /// 「打开下载页」。**关掉就是完全不检查** —— 这一格是这件事唯一的开关，关着时进程不会向发布页发出任何请求，
    /// 设置页里那颗手动「检查新版本」按钮不受它管（那是用户按下去的，不是程序自己动的）。
    /// </para>
    /// <para>
    /// <b>装机默认开</b>（用户令 2026-10-14：这一格默认要开启）。所以缺这个键的旧设置文件读出来就是开 ——
    /// 从旧版本升上来的人不必先找到这一格才会收到更新提示。发布页是本程序唯一一个既不由用户配置、也不由他
    /// 自己的服务器给出的外部地址（见 <c>UpdateClient</c>），为此隐私政策第 4 节单列了一条，写明这一趟会发出去
    /// 什么（只有客户端名与版本号，不带账号、令牌、设备 id、机器名或服务器地址）以及怎么关掉它。恢复默认
    /// （<see cref="SettingsPreferences"/>）对界面组是「默认当偏好」的整组抄法，这一项自动跟着回到开。
    /// </para>
    /// <para>
    /// 拨一下<b>下一次启动才生效</b>，不做「拨开就立刻查一次」—— 那样按一下开关会跳出一个对话框，而用户按下它
    /// 时想的是「以后启动的时候留意一下」。想现在就知道结果，按下面那颗「检查新版本」。
    /// </para>
    /// </summary>
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>
    /// 主页顶上那张轮播大图显不显示 —— 「在设置中新增关闭轮播图的功能」。
    /// <para>
    /// 关掉之后主页第一屏就没有那一块了：大图和它右边那一栏继续观看一起收起，继续观看回到下面横着排的第一排
    /// （见 <c>HomeViewModel.BuildShelves</c>），整页变成一叠普通的货架。
    /// </para>
    /// <para>
    /// 装机默认是开，所以旧的设置文件里没有这个键时行为一个像素都不变，也不需要为它加一条迁移。
    /// </para>
    /// </summary>
    public bool ShowHomeBanner { get; set; } = true;

    /// <summary>
    /// 主页第一屏上媒体库那一排摆在哪 —— 「主页媒体库位置」（用户 2026-10-12 时叫「首页媒体库位置」，
    /// 2026-10-16 按用户令改名并挪进主页卡）。三档见
    /// <see cref="Emby.HomeLibraryPlacement"/>：随窗口高度变换（默认，2026-09-13 起一直有的那一档）、固定在轮播
    /// 图中、固定在轮播图下方。
    /// <para>
    /// 摆在设置 → 主页卡上（那一页的「主页媒体库位置」一行）：2026-10-16 之前它在界面卡上，理由是「这块界面长
    /// 什么样」，而用户把它和主页那张版面表（勾哪几排、什么次序）看作同一件事 —— 都是主页第一屏长什么样。
    /// </para>
    /// <para>
    /// 枚举存整数（同 <see cref="CarouselSource"/> 那条规矩），<c>Auto</c> 必须是 0：缺键的旧设置文件读出来就是
    /// 它，从旧版本升上来的人一个像素都不变。认不出来的数字由 <c>SettingsMigration.Normalize</c> 拨回。
    /// </para>
    /// <para>
    /// 改完当场生效：设置页写完喊一声 <c>ShellPrefs</c>，主页那一头重新摆一次那一排（不等回到主页、不用重启）。
    /// 轮播收起来的时候（关掉大图，或者一张幻灯片都没有）「固定在轮播图中」没有轮播可依，那一排按「在下方」显示 ——
    /// 这是唯一一处钉不住的情形，屏上回退成下面那一档的样子。
    /// </para>
    /// </summary>
    public Emby.HomeLibraryPlacement HomeLibraryPlacement { get; set; } = Emby.HomeLibraryPlacement.Auto;

    /// <summary>
    /// 轮播条目从哪来 —— 「要使用最近添加还是随机的」（用户的话，2026-09-13）。枚举存整数，
    /// Recent 必须是 0：缺键的旧设置文件读出来就是它，
    /// 从旧版本升上来的人一个像素都不变。认不出来的数字由 <c>SettingsMigration.Normalize</c> 拨回。
    /// <para>
    /// 改完当场生效：设置页写完喊一声 <c>ShellPrefs</c>，主页那一头照新来源把条目重新要一遍。
    /// </para>
    /// </summary>
    public Emby.CarouselSource CarouselSource { get; set; } = Emby.CarouselSource.Recent;

    /// <summary>
    /// 这条轮播带站哪一类媒体 —— 「要使用什么媒体」（用户的话，2026-09-13）。全部、只看电影或只看剧集；
    /// 音乐类从不出现在轮播里，这一点不随这一档变。存整数，规矩同上。
    /// </summary>
    public Emby.CarouselMediaType CarouselMedia { get; set; } = Emby.CarouselMediaType.All;

    /// <summary>
    /// 这条带最多摆几张。装机默认就是 <see cref="Emby.HomeCarousel.Slots"/> 那「前十个」；上下限在
    /// <see cref="Emby.HomeCarousel"/> 上（<c>ClampSlots</c>），设置页那一行和 <c>SettingsMigration.Normalize</c>
    /// 读同一对常量 —— 一个比设置窄的框会显示一个文件里没有的数，这是同一类 bug 的老窝（见 ImageCacheMegabytes）。
    /// </summary>
    public int CarouselCount { get; set; } = Emby.HomeCarousel.Slots;

    /// <summary>
    /// 一张封面站多少秒再换下一张 —— 「封面的轮换的秒数」（用户的话，2026-09-13）。装机默认
    /// <see cref="Emby.HomeCarousel.DefaultDwellSeconds"/>；夹取在 <see cref="Emby.HomeCarousel.DwellFor"/>。
    /// <para>
    /// 改完当场生效：主页那一头（<c>HomeBanner</c>）从 <c>ShellPrefs</c> 的话里拿到新秒数，自动翻页的钟
    /// 和底下那道进度条当场按新的走，不等回到主页。
    /// </para>
    /// </summary>
    public int CarouselSeconds { get; set; } = Emby.HomeCarousel.DefaultDwellSeconds;

    /// <summary>
    /// 海报缓存在磁盘上最多占多少 MB。
    /// <para>
    /// 从前这个数写死在 <see cref="Emby.EmbyImageStore"/> 的构造函数上（400 MB），谁都改不了：库大的人一路撞
    /// 上限、每次滚回去都要重下一遍海报，库小的人白占着几百兆。默认值就是从前那个 400，所以从没动过这一行的人
    /// 行为一个像素都不变。
    /// </para>
    /// <para>
    /// 上下限、装机默认值和「MB 换字节」都在 <see cref="Emby.ImageCachePolicy"/> 上，由单测钉着：设置页那一行的
    /// 范围必须和 <c>SettingsMigration.Normalize</c> 夹的范围是同一对数（理由见 Normalize 里那一段），而两处各抄
    /// 一遍字面值就是这一类 bug 的老窝。
    /// </para>
    /// <para>
    /// 改完当场生效：设置页在另一个窗口里，所以那一行写完喊一声 <c>ShellPrefs</c>，主窗口那一头把新预算交给
    /// 图片仓库并当场清一次 —— 拖小了不清，用户看见的是「设置了但没用」。
    /// </para>
    /// </summary>
    public int ImageCacheMegabytes { get; set; } = Emby.ImageCachePolicy.DefaultMegabytes;

    // 「评分来源」原来在这一行下面（2026-09-03 起：「加入显示评分改为豆瓣评分的功能，可在设置使用豆瓣、tmdb、
    // 烂番茄等平台的评分」），2026-10-16 按用户的话整条删掉了（「删掉评分来源这个设置选项，服务端给什么评分就用
    // 什么」）—— 详情页那个分从此直接画服务器给的那个数（Emby.ItemScore），不再由用户选平台。旧设置文件里那个键
    // 被反序列化器直接丢掉，不需要迁移。

    // 「锁定窗口比例大小」那一行 2026-09-05 按用户的话整条删掉了（「删除设置中锁定比例的功能」）：浏览时的窗口
    // 从此随便拉，只有放片子的时候形状还跟着画面走（HostWindow.PictureAspect）。「默认收起侧边栏」那一行
    // 2026-09-06 跟着侧边栏本身一起删掉了（「删掉侧边栏」）—— 没有栏可收，那个开关就只是一句没人兑现的话。
    // 两处都不留一个存而不用的键 —— 反序列化碰到没处放的键本来就不出声，所以旧设置文件照旧读得起来。

    public string? LastLibraryId { get; set; }

    /// <summary>
    /// The sort each library was last left on, keyed by library id. Emby remembers this per library and
    /// so does this: a music library put in 加入日期 order should not go back to sorting by name because
    /// the film library next to it is sorted that way.
    /// <para>
    /// Library roots only, deliberately. A folder or a season would add an entry every time one was
    /// opened, and this dictionary is written to settings.json.
    /// </para>
    /// </summary>
    public Dictionary<string, LibrarySort> Sort { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 需求 5：the filters each library was last left on, keyed by library id, on the same terms as
    /// <see cref="Sort"/> — library roots only, because a per-folder entry would grow this file every
    /// time one was opened.
    /// <para>
    /// The runtime type is stored directly rather than copied into a settings-only DTO. It is already a
    /// plain object of string lists for exactly this reason, and a second shape would mean a mapping to
    /// keep in step with the panel.
    /// </para>
    /// </summary>
    public Dictionary<string, ItemFilters> Filters { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The view shape (海报 / 缩略图 / 列表) each library was last left in, keyed by library id, on the
    /// same terms as <see cref="Sort"/> — library roots only. Stored as the enum's name: an int would
    /// silently mean something else the day a shape is inserted between two others.
    /// </summary>
    public Dictionary<string, LibraryView> Views { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 每个媒体库自己的那一套显示设置（视图、图像大小、分组、显示字段），键与 <see cref="Views"/> 同一个。
    /// <para>
    /// 2026-10-17 起取代 <see cref="Views"/>：网页端那颗设置齿轮里的四组东西都记在这儿，一个库一份 ——
    /// 电视剧库和电影库因此可以各开各的视图、各显示各的字段。<see cref="Views"/> 只留着读旧文件，
    /// 读到之后由 <c>LibraryViewModel</c> 搬进这里。
    /// </para>
    /// </summary>
    public Dictionary<string, LibraryDisplay> Display { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 窗口关掉时它多大、在哪儿 —— 下次照这个开。桌面坐标，物理像素，不含最大化那一档（那一档是
    /// <see cref="WindowMaximized"/> 的事，尺寸记的始终是还原之后的那个）。
    /// <para>
    /// <see cref="WindowWidth"/> 或 <see cref="WindowHeight"/> 是 0 就是「还没记过」，这时候开窗尺寸由
    /// 外壳自己算（16:9 的页面加侧边栏那条），也就是第一次运行看到的那个样子。所以这四个数不设默认值 ——
    /// 写一个进去就等于替用户决定了他从没拉过的那个尺寸。
    /// </para>
    /// <para>
    /// 摆到哪块屏、要不要缩进工作区，由 <see cref="Infrastructure.ScreenPlacement.Restore"/> 定：显示器
    /// 拔掉了、分辨率变小了、笔记本离开扩展坞之后，照原样摆回去就是一个标题栏在桌面外面的窗口，鼠标既
    /// 拖不动也关不掉。
    /// </para>
    /// <para>
    /// <c>--screen</c> 明确指定过的那一次不读这几个数（自检默认就带着它）：那一路要的是每次都一样的几何，
    /// 而报告里量的正是客户区尺寸和比例。
    /// </para>
    /// </summary>
    public int WindowLeft { get; set; }

    /// <inheritdoc cref="WindowLeft"/>
    public int WindowTop { get; set; }

    /// <inheritdoc cref="WindowLeft"/>
    public int WindowWidth { get; set; }

    /// <inheritdoc cref="WindowLeft"/>
    public int WindowHeight { get; set; }

    /// <summary>
    /// 关掉的时候是不是最大化着。下次照样最大化开，而 <see cref="WindowWidth"/> 那一份记的是还原之后的
    /// 尺寸 —— 所以从最大化状态退出的窗口，取消最大化之后回到的还是用户自己拉出来的那个大小。
    /// </summary>
    public bool WindowMaximized { get; set; }

    /// <summary>
    /// 主页上那几排的顺序和显示与否 —— 「新增页里拖拽决定这些列表的顺序，勾选显示或者不勾选取消显示」。
    /// <para>
    /// 顺序就是这个列表的顺序，每一项自己带一个勾。空着（第一次运行、或者手改设置时删掉了）就是
    /// <see cref="Emby.HomeLayout"/> 里那份默认版面：继续观看、媒体库、接下来看，然后每个媒体库
    /// 一排它自己的最近添加，全部显示。整服那一排「最近添加」2026-09-12 退役，存档里还带着的会被归一化扔掉。
    /// </para>
    /// <para>
    /// 主页每次读完都把归一化之后的版面写回来（<see cref="Emby.HomeLayout.Plan"/>）：服务器上新加的媒体库
    /// 因此自己排到末尾，删掉的那个自己消失，改过名的那一排把新名字带回来。
    /// </para>
    /// </summary>
    public List<HomeRowSetting> HomeRows { get; set; } = [];

    public string HomeRowsIdentity { get; set; } = "";

    public Dictionary<string, List<HomeRowSetting>> HomeRowsByIdentity { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 主页上一排的记档：认它的那把钥匙、上一次看见的名字，和显示与否。
/// <para>
/// 名字也存下来是为了设置窗口：那一头有设置文档，却没有这个账号的媒体库列表（它甚至可能开在服务器连不上的
/// 时候），而拖拽排序那张表要写得出每一排叫什么。主页读完会把新名字写回来，所以它最多旧一次。
/// </para>
/// </summary>
public sealed class HomeRowSetting
{
    /// <summary>见 <see cref="Emby.HomeLayout"/>：四排固定的各有一把，媒体库那几排是 <c>library:{id}</c>。</summary>
    public string Key { get; set; } = "";

    public string Title { get; set; } = "";

    public bool Visible { get; set; } = true;
}

/// <summary>One library's remembered sort: an Emby <c>SortBy</c> value and a direction.</summary>
public sealed class LibrarySort
{
    public string By { get; set; } = "";

    public bool Descending { get; set; }
}
