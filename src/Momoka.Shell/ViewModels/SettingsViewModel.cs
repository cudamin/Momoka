using Momoka.Infrastructure;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.Media;
using Momoka.Theming;
using Momoka.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.ViewModels;

/// <summary>One card on the settings page: a heading, a sentence about it, and the rows under it.</summary>
public sealed partial class SettingSection : ObservableObject
{
    internal SettingSection(string category, string title, string description, IReadOnlyList<SettingRow> rows)
    {
        Category = category;
        Title = title;
        Description = description;
        Rows = new ObservableCollection<SettingRow>(rows);
        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Count));
    }

    /// <summary>
    /// The entry in the left-hand list that shows this card. Kept separate from <see cref="Title"/> because
    /// several cards can share one category — 「画质与着色器」 under 「视频输出」 — and matching
    /// the two by substring, as this page used to, makes every future heading a trap.
    /// </summary>
    public string Category { get; }

    public string Title { get; private set; }

    /// <summary>
    /// 同一张卡挂两个左栏入口时改抬头：飞牛的「外观」就是「界面」卡，选中哪一项，卡上就写哪一项，
    /// 否则左栏说「外观」、右边却写「界面」。别的卡不动。
    /// </summary>
    internal void Retitle(string title)
    {
        if (string.Equals(Title, title, StringComparison.Ordinal)) return;
        Title = title;
        OnPropertyChanged(nameof(Title));
    }

    public string Description { get; }

    public ObservableCollection<SettingRow> Rows { get; }

    /// <summary>
    /// 从属行原地增删，保留仍在场的控件和输入焦点；不整卡替换，WinUI 才能给增删与补位做过渡。
    /// 类型变化（如可编辑选择变只读文字）必须替换；标签相同但类型不同不复用旧模板。
    /// 同一张卡里期望行的「标签＋类型」必须唯一 —— 复用就按这两样找，重复会让两条期望行拿到同一个实例。
    /// 隐藏行的值仍在设置里，恢复时新行从该值读取；这里不写盘。
    /// </summary>
    internal void UpdateRows(IReadOnlyList<SettingRow> desired)
    {
        var rows = desired.Select(row =>
        {
            var existing = Rows.FirstOrDefault(current => current.Label == row.Label && current.GetType() == row.GetType());
            if (existing is null) return row;
            existing.Restate(row.Note);
            switch (existing, row)
            {
                case (SettingChoiceRow current, SettingChoiceRow updated):
                    current.ReseedSelection(updated.Selected?.Label);
                    break;
                case (SettingToggleRow current, SettingToggleRow updated):
                    current.Reseed(updated.IsOn);
                    break;
                case (SettingFactRow current, SettingFactRow updated):
                    current.Value = updated.Value;
                    break;
            }
            return existing;
        }).ToArray();
        for (var index = Rows.Count - 1; index >= 0; index--)
            if (!rows.Contains(Rows[index])) Rows.RemoveAt(index);
        for (var index = 0; index < rows.Length; index++)
        {
            var current = Rows.IndexOf(rows[index]);
            if (current < 0) Rows.Insert(index, rows[index]);
            else if (current != index) Rows.Move(current, index);
        }
    }

    /// <summary>
    /// 卡片牌子右端那个读数：这张卡管着几件事。
    /// <para>
    /// 数的是设置的件数，不是树上的容器数（那是 <see cref="SettingsViewModel.Containers"/> 的活）—— 一组
    /// 开关是好几件事，而包着它们的那一行本身不是一件；一行色板是一件事，不是六件。
    /// </para>
    /// </summary>
    public string Count => $"{Rows.Sum(row => row is SettingToggleGroupRow group ? group.Toggles.Count : 1)} 项";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SectionVisibility))]
    public partial bool IsVisible { get; set; }

    /// <summary>
    /// Every card is built and kept; picking a category only changes which one is shown. The page has no
    /// server data in it, so building all of them costs nothing measurable, and a card that stays alive
    /// keeps any half-typed text across a trip through the category list.
    /// </summary>
    public Visibility SectionVisibility => IsVisible ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// The settings page, as data. Builds the cards and their rows out of <see cref="AppSettings"/> and
/// the mpv option catalogues, and saves the settings document on every edit.
/// <para>
/// There is no 「loading」 flag here. Each row reads its starting value in its own constructor and only
/// writes from that point on, so nothing has to suppress writes while the page is being built — which is
/// what the page-wide flag was for, and it had to be held during any programmatic refresh, suppressing
/// genuine edits along with the spurious ones.
/// </para>
/// </summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private static readonly (string Label, MpvBackendKind Value)[] Backends =
    [
        ("内置播放器（libmpv）", MpvBackendKind.BuiltInLibMpv),
        ("外部 mpv.exe（独立窗口）", MpvBackendKind.ExternalMpv)
    ];

    private static readonly (string Label, VideoPipelineKind Value)[] Pipelines =
    [
        ("集成模式（在主窗口播放）", VideoPipelineKind.Integrated),
        ("独占模式（单独的视频窗口）", VideoPipelineKind.Standalone)
    ];

    private static readonly (string Label, SkipSectionMode Value)[] SkipModes =
    [
        ("询问（显示跳过按钮）", SkipSectionMode.Ask),
        ("自动跳过", SkipSectionMode.Auto),
        ("关闭", SkipSectionMode.Off)
    ];

    // 媒体源排序（「在设置中新增自定义选项，媒体源排序 默认/新入库在前」，2026-09-24）。两档照他点的名。
    private static readonly (string Label, Emby.MediaSourceOrder Value)[] MediaSourceOrders =
    [
        ("默认", Emby.MediaSourceOrder.Default),
        ("新入库在前", Emby.MediaSourceOrder.NewestFirst)
    ];

    private static readonly (string Label, SubtitleMode Value)[] SubtitleModes =
    [
        ("总是显示匹配字幕", SubtitleMode.Always),
        ("只显示强制字幕", SubtitleMode.ForcedOnly),
        ("音轨为外语时显示", SubtitleMode.ForeignAudioOnly),
        ("从不显示", SubtitleMode.Off)
    ];

    private ISettingsService? _settings;
    private ShaderStaging? _shaders;
    private FontLibrary? _fonts;
    private AppPaths? _paths;
    private Platform.ISystemLauncher? _launcher;
    private Platform.IStartupRegistration? _startup;
    private IServerCapabilities? _capabilities;
    private readonly LoadGeneration _administrationLoads = new();
    private bool _active;
    private bool _disposed;
    private MoviePilotService? _moviePilot;
    private MoviePilotCredentials? _moviePilotCredentials;
    private SettingMoviePilotRow? _moviePilotRow;
    private UpdateClient? _updates;
    private SettingFontRow? _subtitleFont;
    private AudioDeviceCatalogue? _audioDevices;
    private SettingChoiceRow? _audioDevice;
    private SettingSubtitlePreviewRow? _subtitlePreview;
    private Func<Task>? _pushSubtitleStyle;

    /// <summary>「关于」卡上图片缓存两行的数据来源；没有接上（测试路径）时那两行只是不更新。</summary>
    private EmbyImageStore? _images;

    /// <summary>「关于」卡上那行图片缓存读数，握着一清空之后当场改它（同 <see cref="_updateRow"/> 的理由）。</summary>
    private SettingFactRow? _imageCache;

    /// <summary>「把 截图保存目录 推给正在播的那部」，见 <see cref="Attach"/> 的 <c>pushScreenshotDirectory</c>。</summary>
    private Func<string, Task>? _pushScreenshotDirectory;

    /// <summary>「把 截图格式与画质 推给正在播的那部」，见 <see cref="Attach"/> 的 <c>pushScreenshotOptions</c>。</summary>
    private Func<Task>? _pushScreenshotOptions;

    /// <summary>
    /// 「关于」卡上那行「截图目录」，握着好让「播放行为」卡上的目录一改它就当场跟上（同 <see cref="HomeRows"/>
    /// 那样握着一行的理由）。只在 <see cref="AboutCard"/> 建出来时有值；没有「关于」卡（测试路径）就是 null，
    /// 改目录那一头 null 检查过。
    /// </summary>
    private SettingFactRow? _screenshotFact;

    /// <summary>
    /// 「关于」卡上那行「检查新版本」，握着好在按下去之后当场改它那一行读数（同 <see cref="_screenshotFact"/>
    /// 握着一行的理由）。只在 <see cref="AboutCard"/> 建出来时有值；没有「关于」卡（测试路径）就是 null，
    /// 手动检查那一头 null 检查过。
    /// </summary>
    private SettingFactRow? _updateRow;

    /// <summary>
    /// 手动检查正在跑。这一趟有网络、要几秒，挡掉连点：不挡的话按第二下会再发一个请求，而两颗按钮长得一样、
    /// 第一趟还没回来时看不出哪个是哪个。
    /// </summary>
    private bool _checkingUpdates;

    /// <summary>快捷键卡里那 21 行可重绑的行，握着好在重绑/清空之后逐行刷新显示（同 <see cref="HomeRows"/> 那样握着一行的理由）。</summary>
    private readonly List<SettingShortcutRow> _shortcutRows = [];

    /// <summary>
    /// 当前账号可见的服务端管理页名单：Emby 是「通知」加上三段服务器目录（控制台、通用、用户……
    /// 以及服务器插件页），Jellyfin 是五段服务器目录（控制台、常规、用户、媒体库、插件页……），
    /// 飞牛影视是「影视服务器」组的四个原生页。Plex 没有。
    /// <para>
    /// 2026-10-19 用户令：「把这些分区和功能移左边设置栏里……删除网页控制台分区」。Emby／Jellyfin 的
    /// 「网页控制台」从此都不再单列，服务器目录的第一项「控制台」就是同一页。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> ServerAdministrationCategories => _serverAdministrationCategories;

    private IReadOnlyList<string> _serverAdministrationCategories = [];
    private IReadOnlyList<ServerSettingsMenuGroup> _serverGroups = [];
    private IReadOnlyList<ServerSettingsMenuEntry> _serverEntries = [];
    private IReadOnlyDictionary<string, string> _serverRoutes = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// 清空/重排平铺名单时保住选中项。它有自己的一小段防抖：<see cref="SelectedCategory"/> 是键，
    /// <see cref="SelectedEntry"/> 是左栏与紧凑下拉共用的那一项。
    /// </summary>
    private bool _syncingSelection;

    /// <summary>分区标题偶尔被键盘或自动化选中时，选择退回这里——上一次真正打开的页面。</summary>
    private SettingsCategoryEntry? _lastSelectableEntry;

    /// <summary>飞牛影视「帐号」组的两个内嵌页；「外观」映射到现有「界面」卡，不另造一页。</summary>
    private static readonly string[] FnNasAccountHostedCategories = [FnNasPasswordCategory, FnNasPreferencesCategory];
    private static readonly string[] FnNasAccountCategories = [FnNasPasswordCategory, FnNasPreferencesCategory, FnNasAppearanceCategory];
    private IReadOnlyList<string> _fnNasAccountHosted = [];
    private bool _showsFnNasAccount;

    /// <summary>
    /// 管理组是否显示。初始表用于离线布局；接入能力服务后，在请求前同步真实身份与产品。
    /// Jellyfin 只在确认管理员时显示，Emby 保留未知权限时的既有回退行为。
    /// </summary>
    private bool _showsServerAdministration = true;
    private MediaServerKind _administrationKind;

    /// <summary>
    /// 左栏那一列的分组，见 <see cref="CategoryGroups"/>。
    /// </summary>
    private IReadOnlyList<SettingsCategoryGroup> _categoryGroups = [];

    /// <summary>
    /// 建左栏初表：只有 Momoka 自己的一组。服务器目录要等 <see cref="RefreshServerAdministrationAsync"/>
    /// 按真实产品与账号权限填进来 —— 初表不该先猜一个 Emby，再从屏上换成 Jellyfin。
    /// </summary>
    public SettingsViewModel()
    {
        _categoryGroups = BuildCategoryGroups(serverAdministration: false, MediaServerKind.Emby, fnNasAccount: false);

        Categories.Clear();
        foreach (var entry in _categoryGroups.SelectMany(group => group.Entries))
            Categories.Add(entry);
        SelectedEntry = Categories.FirstOrDefault();
    }

    /// <summary>
    /// 照账号权限与产品造左栏分组表：「Momoka」永远在最上面，Emby 管理员接「Emby Server」「设备」「高级」，
    /// Jellyfin 管理员接「服务器」「设备」「电视直播」「插件」「高级」，飞牛影视作为唯一在线服务器时接「帐号」以及管理员才有的
    /// 「影视服务器」。三套目录的条目与次序各自同源、不混用。Plex 或没有连接时只有 Momoka 一组。
    /// </summary>
    private List<SettingsCategoryGroup> BuildCategoryGroups(bool serverAdministration, MediaServerKind kind, bool fnNasAccount)
    {
        var groups = new List<SettingsCategoryGroup>
        {
            MomokaGroup(serverAdministration, kind)
        };

        // 飞牛影视的左栏照它自己的设置抽屉分成「帐号」与「影视服务器」两组。帐号组对已登录的飞牛账号
        // 一律可见；影视服务器组要管理员，和网页端一样。
        if (fnNasAccount && kind == MediaServerKind.FnNas)
        {
            var account = new SettingsCategoryGroup("帐号");
            foreach (var category in FnNasAccountCategories) account.Add(category, cardCategory: SectionForCategory(category));
            groups.Add(account);
        }

        // 用户 2026-10-19：Momoka 自己的设置在设置页最上面，服务器目录接在它下面。
        if (serverAdministration && kind is MediaServerKind.Emby or MediaServerKind.Jellyfin or MediaServerKind.FnNas)
        {
            foreach (var group in _serverGroups)
            {
                var entries = new SettingsCategoryGroup(group.Label);
                foreach (var entry in group.Entries)
                    entries.Add(entry.Label, route: entry.Route, cardCategory: SectionForCategory(entry.Label));
                groups.Add(entries);
            }
        }

        return groups;
    }

    private static SettingsCategoryGroup MomokaGroup(bool serverAdministration, MediaServerKind kind)
    {
        var group = new SettingsCategoryGroup("Momoka");
        foreach (var category in SidebarCategories(serverAdministration, kind)) group.Add(category);
        return group;
    }

    /// <summary>
    /// 「Momoka」组里管理员才看得见的那几个入口。Emby 只留「通知」（服务器设置有自己的三段）；
    /// Jellyfin 的管理页全在服务器目录里，没有额外入口；飞牛影视与 Plex 一个都没有。
    /// </summary>
    private static IReadOnlyList<string> MomokaAdministrationCategories(MediaServerKind kind) => kind switch
    {
        MediaServerKind.Emby => [NotificationsCategory],
        _ => []
    };

    /// <summary>Emby、Jellyfin 或飞牛影视服务器目录里的页名；Plex 没有这套目录。</summary>
    private IReadOnlyList<string> ServerDirectoryCategories(MediaServerKind kind) =>
        kind is MediaServerKind.Emby or MediaServerKind.Jellyfin or MediaServerKind.FnNas
            ? [.. _serverEntries.Select(entry => entry.Label)]
            : [];

    /// <summary>当前产品下管理员可见的全部入口（Momoka 组里的 + 服务器目录）。</summary>
    private IReadOnlyList<string> AdministrationCategories(MediaServerKind kind) =>
        [.. MomokaAdministrationCategories(kind), .. ServerDirectoryCategories(kind)];

    /// <summary>
    /// 左栏「Momoka」组的条目，照左栏显示的次序：卡片之外，服务器、诊断两张内嵌页插在最后一张卡（关于）
    /// 前面；管理员入口再插在 诊断 与 关于 中间。Emby 的服务器设置目录不在这里 —— 它自成一档，
    /// 见 <see cref="BuildCategoryGroups"/>。
    /// </summary>
    private static IReadOnlyList<string> SidebarCategories(bool serverAdministration, MediaServerKind kind) =>
        [.. CardCategories[..^1], "服务器", "诊断",
            .. (serverAdministration ? MomokaAdministrationCategories(kind) : []),
            LastCardCategory];

    /// <summary>The cards, in the order they appear in the left-hand list.</summary>
    /// <remarks>
    /// 用户令 2026-10-11「调整左侧选项排序」：主页与界面领队（进设置先收拾界面本身），播放器、播放行为、
    /// 视频输出、音频输出、字幕这五张播放相关的卡跟着，然后是快捷键；MoviePilot 在快捷键之后、
    /// 「服务器／诊断」两张内嵌页之前 —— 它仍是可选的第二服务，不是本程序自己的行为。
    /// </remarks>
    private static readonly string[] CardCategories =
        ["主页", "界面", "播放器", "播放行为", "视频输出", "音频输出", "字幕", "快捷键", "MoviePilot", "关于"];

    /// <summary>
    /// 需求 2 的后半句：「诊断和服务器移动到设置里」，加上需求 8 的网页控制台。Entries in the same list
    /// that are not cards but whole pages — a server list that talks to the network, a log view that tails a
    /// file, the embedded browser on the server's own console — hosted in the settings page's own frame rather
    /// than flattened into setting rows they do not fit.
    /// <para>
    /// 「通知」也住这一边（2026-09-25）：它原是一张普通设置卡（客户端直发 webhook 的那套）；改走 Emby 服务器的
    /// 通知系统之后，它要的是「条目列表 + 增删改 + 测试」——和服务器页一样是一整页会说话的东西，塞进行卡片里
    /// 反而两头不是。
    /// </para>
    /// <para>
    /// 2026-10-19 起 Emby 的三段服务器设置也住这一边：它们的页名来自 <see cref="ServerSettingsMenu"/>，
    /// 由 <see cref="ServerAdministrationCategories"/> 带着来去；Jellyfin 的「网页控制台」仍是固定一项。
    /// </para>
    /// </summary>
    private static readonly string[] FixedHostedCategories = ["服务器", "诊断"];

    /// <summary>
    /// 「通知」那一页. Named because the navigation switch and the self-check both have to say it —
    /// same reason as <see cref="DashboardCategory"/>.
    /// </summary>
    public const string NotificationsCategory = "通知";

    /// <summary>
    /// Jellyfin 的网页控制台。Emby 从 2026-10-19 起把同一页放进「Emby Server」目录的第一项「控制台」，
    /// 不再单列这个分类；这里保留它给 Jellyfin，因为 Jellyfin 的网页端没有这套 Emby 管理菜单。
    /// </summary>
    public const string DashboardCategory = "网页控制台";

    /// <summary>飞牛影视左栏：帐号三项，影视服务器四项。名字与官方 0.9.8 设置抽屉一致。</summary>
    public const string FnNasPasswordCategory = "修改密码";
    public const string FnNasPreferencesCategory = "播放偏好";
    public const string FnNasAppearanceCategory = "外观";
    public const string FnNasLibrariesCategory = "媒体库";
    public const string FnNasUsersCategory = "用户";
    public const string FnNasSettingsCategory = "设置";
    public const string FnNasTasksCategory = "任务计划";

    /// <summary>
    /// 「外观」不是另一张卡：它就是 Momoka 自己的「界面」卡。飞牛网页端的外观页管的是网页自己的主题、
    /// 语言与海报角标，搬进 WinUI 后对应的是同一批界面设置，所以左栏保留这一个入口、内容复用。
    /// </summary>
    internal static string SectionForCategory(string category) =>
        string.Equals(category, FnNasAppearanceCategory, StringComparison.Ordinal) ? "界面" : category;

    /// <summary>The left-hand list. Order is the order of the cards, then the hosted pages.</summary>
    /// <remarks>
    /// 「恢复默认」曾是这份名单末尾的一项（2026-09-13～2026-09-24）；2026-09-24 按「把恢复默认设置移动到关于中」
    /// 搬进了「关于」卡，和新增的「备份配置文件」「恢复配置」并作三颗动作按钮 —— 见 <see cref="AboutCard"/>。它不再
    /// 是名单里的一项，所以这里也不再有它。
    /// <para>
    /// 管理员才看得见的那几个入口按身份来去（<see cref="ApplyServerAdministration"/>），而**换的是一份新表，
    /// 不是原地增删**：那个 <c>CollectionViewSource</c> 的分组视图不吃「组从集合里消失」这一套 —— 组底下的条目
    /// 确实会没，组的标题却留在屏上（2026-10-10 截图实量，见探针 settings-server-admin-hidden.png 之前那一版）。
    /// 所以这份表每次重算都是一个新实例，页面把 <c>CategoryGroupsSource.Source</c> 重新指一次。
    /// </para>
    /// </remarks>
    public IReadOnlyList<SettingsCategoryGroup> CategoryGroups => _categoryGroups;

    /// <summary>
    /// The flat list of every entry, in the same order as <see cref="CategoryGroups"/> — what the compact
    /// window's picker shows and what <c>SettingsPage.Select</c> answers 「这一页有这一项吗」 from.
    /// <see cref="ApplyServerAdministration"/> takes the same names out of it as out of the groups.
    /// </summary>
    public ObservableCollection<SettingsCategoryEntry> Categories { get; } = [];

    public ObservableCollection<SettingSection> Sections { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsHosted))]
    [NotifyPropertyChangedFor(nameof(CardsVisibility))]
    [NotifyPropertyChangedFor(nameof(HostedVisibility))]
    public partial string SelectedCategory { get; set; } = CardCategories[0];

    /// <summary>左栏与紧凑下拉共用的那一项；<see cref="SelectedCategory"/> 是它的键。</summary>
    [ObservableProperty]
    public partial SettingsCategoryEntry? SelectedEntry { get; set; }

    internal SettingsCategoryEntry? EntryFor(string value) => FindEntry(value);

    private SettingsCategoryEntry? FindEntry(string value) =>
        Categories.FirstOrDefault(entry => string.Equals(entry.Key, value, StringComparison.Ordinal))
        ?? Categories.FirstOrDefault(entry => string.Equals(entry.Label, value, StringComparison.Ordinal));

    partial void OnSelectedCategoryChanged(string value)
    {
        if (!_syncingSelection)
        {
            _syncingSelection = true;
            try { SelectedEntry = FindEntry(value); }
            finally { _syncingSelection = false; }
        }
        ShowCategory(SelectedEntry?.Label ?? value);
    }

    partial void OnSelectedEntryChanged(SettingsCategoryEntry? value)
    {
        if (value is null) return;

        // 分区标题只用来分段。键盘或自动化偶尔把它选进来时，退回上一次真正打开的页面；
        // 右半边不闪、SelectedCategory 也不跟着一个不存在的页名走。
        if (value.IsSection)
        {
            var previous = _lastSelectableEntry ?? Categories.FirstOrDefault();
            if (previous is not null && !ReferenceEquals(previous, value))
            {
                _syncingSelection = true;
                try { SelectedEntry = previous; }
                finally { _syncingSelection = false; }
            }
            return;
        }

        _lastSelectableEntry = value;
        // 在紧凑下拉里选到收起的组时把它展开；重建名单（_syncingSelection）不推翻用户手动收起的状态。
        if (!_syncingSelection && !value.Group.IsExpanded) value.Group.IsExpanded = true;
        if (_syncingSelection)
        {
            ShowCategory(value.Label);
            return;
        }

        if (!string.Equals(SelectedCategory, value.Key, StringComparison.Ordinal))
        {
            _syncingSelection = true;
            try { SelectedCategory = value.Key; }
            finally { _syncingSelection = false; }
        }
        ShowCategory(value.Label);
    }

    /// <summary>选中项属于哪一条在线身份；Momoka 自己的卡片与单服务器产品组是 null。</summary>
    internal string? SelectedSourceKey => SelectedEntry?.SourceKey;

    /// <summary>
    /// 选中项属于哪一种产品：多服务器分组里问那一组自己的来源，单服务器时就是当前产品。
    /// 内嵌页的分派不能只看页名 —— Emby／Jellyfin 的目录里也有「媒体库」「用户」，与飞牛的原生页同名。
    /// </summary>
    internal MediaServerKind SelectedServerKind =>
        SelectedEntry?.SourceKey is { Length: > 0 } key
        && _capabilities?.Sources.FirstOrDefault(source => string.Equals(source.Key, key, StringComparison.Ordinal)) is { } source
            ? source.Kind
            : _administrationKind;

    /// <summary>这一项该不该打开飞牛的原生页；同名项按它所属的产品分辨。</summary>
    internal bool OpensFnNasPage(string label) => SelectedServerKind == MediaServerKind.FnNas
        && label is FnNasPasswordCategory or FnNasPreferencesCategory or FnNasLibrariesCategory
            or FnNasUsersCategory or FnNasSettingsCategory or FnNasTasksCategory;

    /// <summary>
    /// Whether the selected entry is one of the pages rather than one of the cards. The page watches
    /// this to navigate its frame, and the two visibilities below are the same question drawn.
    /// <para>
    /// Emby／Jellyfin 的服务器页、飞牛的帐号两页与服务器四页都在内嵌名单里，所以它们是 hosted；
    /// 非管理员时服务器组那份名单收起，落在旧分类上的选择会被 <see cref="ApplyServerAdministration"/> 收走。
    /// 飞牛的「外观」不是 hosted，它落到「界面」卡。
    /// </para>
    /// <para>
    /// 多服务器分组里每一项都带来源键：除了飞牛的「外观」（它仍是「界面」卡），其余都是内嵌页。
    /// </para>
    /// </summary>
    public bool ShowsHosted
    {
        get
        {
            var entry = SelectedEntry;
            var label = entry?.Label ?? SelectedCategory;
            if (entry?.SourceKey is not null)
                return !string.Equals(label, FnNasAppearanceCategory, StringComparison.Ordinal);
            return FixedHostedCategories.Contains(label, StringComparer.Ordinal)
                || _fnNasAccountHosted.Contains(label, StringComparer.Ordinal)
                || _serverAdministrationCategories.Contains(label, StringComparer.Ordinal);
        }
    }

    /// <summary>自检用：飞牛「外观」挂在「界面」卡上，分组表比卡片多这一项。</summary>
    internal IReadOnlyList<string> CardAliases =>
        Categories.Any(entry => string.Equals(entry.Label, FnNasAppearanceCategory, StringComparison.Ordinal))
            ? [FnNasAppearanceCategory] : [];

    public Visibility CardsVisibility => Show(!ShowsHosted);

    public Visibility HostedVisibility => Show(ShowsHosted);

    /// <summary>
    /// Where the page goes when it is opened without a category, or with one it does not have. Not a
    /// literal 「主页」 in three places: the first card is what this page has always opened on, and the
    /// day its order changes that should follow.（2026-10-11 用户令「调整左侧选项排序」后，第一张卡从
    /// 播放器换成了主页 —— 没有指名分类的打开就落在主页上。）
    /// </summary>
    internal static string FirstCardCategory => CardCategories[0];

    /// <summary>
    /// 最后一张卡（关于）。管理员才看得见的「通知」「网页控制台」就插在它前面 —— 探针拿它认那两个入口的落点，
    /// 所以不写成字面量。
    /// </summary>
    internal static string LastCardCategory => CardCategories[^1];

    /// <summary>
    /// 这个名字是不是一张设置卡（不是 服务器／诊断／通知／网页控制台 那几页）。给「点进去落在哪张卡」这类
    /// 读数当判据用：播放页右键菜单末尾那三行设置入口（<see cref="Momoka.Mpv.PlayerSettingsLinks"/>）拿它
    /// 对账，卡片改名而那一张表没跟上时当场报红 —— 找不到名字时 <c>SettingsPage.Select</c> 会安静地退回
    /// 「播放器」，用户点「字幕」却开在别的卡上，那种错靠看是看不出来的。
    /// </summary>
    internal static bool IsCardCategory(string category) =>
        CardCategories.Contains(category, StringComparer.Ordinal)
        || string.Equals(category, FnNasAppearanceCategory, StringComparison.Ordinal);

    /// <summary>主题那一行的色板，同样是为了让自检不必去树上找它。</summary>
    internal SettingThemeRow? Themes { get; private set; }

    /// <summary>
    /// How many row containers the cards would put on the visual tree between them; the self-check
    /// compares this to what really rendered.
    /// </summary>
    internal int RowCount => Sections.Sum(Containers);

    /// <summary>
    /// Each card in the order they appear, and the containers it holds. What the self-check walks: it steps
    /// through the categories and checks the tree against the running total, because a collapsed card never
    /// builds its rows and a single look at this page would leave every card but one untouched.
    /// </summary>
    internal IReadOnlyList<(string Category, int Rows)> Cards =>
        [.. Sections.GroupBy(section => section.Category, StringComparer.Ordinal)
            .Select(group => (group.Key, group.Sum(Containers)))];

    /// <summary>
    /// Row containers one card puts on the tree, which is not the same as its row count: a toggle group is one
    /// row holding an <c>ItemsControl</c>, so it realises a container for itself and another for each switch
    /// under it. Counting rows instead would leave the tree looking five containers too full.
    /// </summary>
    internal static int Containers(SettingSection section) =>
        section.Rows.Sum(row => row is SettingToggleGroupRow group ? 1 + group.Toggles.Count : 1);

    /// <summary>
    /// The capabilities this page needs, and nothing else: the settings document with its write-back, the
    /// shader catalogue the 着色器 card offers, the machine's fonts for the 字幕 card's picker, — for the
    /// 关于 card — where this app keeps its files and how to show a folder, and what the signed-in account is
    /// allowed to administer. Named rather than handed the whole composition root — a page that edits settings
    /// has no business being able to reach the session or the player.
    /// </summary>
    /// <param name="capabilities">
    /// 当前账号是不是服务器管理员 —— 左栏「通知」「网页控制台」（以及飞牛影视那一组）的显隐由它决定，见
    /// <see cref="RefreshServerAdministrationAsync"/>。收窄过的能力而不是会话本身：这一页要知道的只有
    /// 「这个账号能不能管服务器」这一位，上面那句「够不着会话」照旧成立。
    /// </param>
    /// <param name="pushSubtitleStyle">
    /// 「把 字幕外观 推给正在播的那部片子」, handed over as one method rather than as the player it belongs to —
    /// which is how this page gains that one ability without gaining 播放 / 停止 / 跳转 along with it, and how
    /// the sentence above stays true. <see cref="PlaybackService.ApplySubtitleStyleAsync"/> is what arrives
    /// here; it does nothing when nothing is playing, so this page never has to ask.
    /// </param>
    /// <param name="pushScreenshotDirectory">
    /// 「把 截图保存目录 推给正在播的那部片子」, same seam as <paramref name="pushSubtitleStyle"/> one slot up:
    /// <see cref="PlaybackService.ApplyScreenshotDirectoryAsync"/> arrives, nothing else of the player does,
    /// and nothing is playing means nothing happens.
    /// </param>
    /// <param name="pushScreenshotOptions">
    /// 「把 截图格式与画质 推给正在播的那部片子」, the same seam one more time:
    /// <see cref="PlaybackService.ApplyScreenshotOptionsAsync"/> arrives — it reads the settings itself, so
    /// the rows only have to say 「推一遍」. Nothing playing means nothing happens.
    /// </param>
    /// <param name="startup">
    /// 开机自启那一格：它读写的不是设置文件而是机器上的启动项（HKCU 的 Run），所以这一页要的是「这台机器上这一
    /// 条此刻在不在、拨一下改得动吗」，见 <see cref="StartupRow"/>。打包安装（MSIX）之后它答
    /// <see cref="Platform.IStartupRegistration.Available"/> 为假，那一行整个不显示。
    /// </param>
    /// <param name="updates">
    /// 「关于」卡上那颗手动「检查新版本」用的：去项目的发布页问一句有没有比这一版新的。交出去的是一个客户端而不是
    /// 一个方法，因为这一页要的正是「查一次」这一件事的全部结果（有没有新版、新版是哪一版、它的页面在哪），
    /// 而打开那个页面仍走这一页本来就有的 <see cref="Platform.ISystemLauncher"/>。
    /// </param>
    internal void Attach(
        ISettingsService settings,
        ShaderStaging shaders,
        FontLibrary fonts,
        AppPaths paths,
        Platform.ISystemLauncher launcher,
        Platform.IStartupRegistration startup,
        AudioDeviceCatalogue audioDevices,
        Func<Task> pushSubtitleStyle,
        Func<string, Task> pushScreenshotDirectory,
        Func<Task> pushScreenshotOptions,
        MoviePilotService moviePilot,
        MoviePilotCredentials moviePilotCredentials,
        UpdateClient updates,
        IServerCapabilities capabilities,
        EmbyImageStore? images = null)
    {
        if (_disposed) return;
        Activate();
        _settings = settings;
        _shaders = shaders;
        _fonts = fonts;
        _paths = paths;
        _launcher = launcher;
        _startup = startup;
        _capabilities = capabilities;
        _audioDevices = audioDevices;
        _pushSubtitleStyle = pushSubtitleStyle;
        _pushScreenshotDirectory = pushScreenshotDirectory;
        _pushScreenshotOptions = pushScreenshotOptions;
        _moviePilot = moviePilot;
        _moviePilotCredentials = moviePilotCredentials;
        _updates = updates;
        _images = images;
    }

    /// <summary>自检读的：管理员才看得见的那几个入口这会儿是收着的吗（非管理员账号下是）。</summary>
    internal bool ServerAdministrationHidden => !_showsServerAdministration;

    /// <summary>探针读的：飞牛「帐号」组这会儿在不在。</summary>
    internal bool FnNasAccountShown => _showsFnNasAccount;

    /// <summary>
    /// 按当前账号的管理员身份与产品决定服务端设置入口在不在，并在它们该收起时把落点收回第一张卡。
    /// <para>
    /// 页面每次打开都要走一趟，不能只在建页那一次：设置页是缓存的（半截输入和「刚看的是哪张卡」都要留着），
    /// 换过账号之后页面不重建，不重走就会留着上一个账号的答案。
    /// </para>
    /// <para>
    /// 身份取自 <see cref="IServerCapabilities.IsAdministrator"/>；Jellyfin 读取用户策略，不能从系统信息的
    /// 可读性推断管理员。Emby 管理员随后再读一次服务器插件页，把 Cinema Intros、DLNA 这些条目补进目录；
    /// 读不到就保留固定条目，不拿一条错误挡住整页。
    /// </para>
    /// </summary>
    internal async Task RefreshServerAdministrationAsync()
    {
        if (!_active || _capabilities is not { } capabilities) return;

        var token = _administrationLoads.Begin();
        var sources = capabilities.Sources.ToArray();
        var keys = sources.Select(source => source.Key).Order(StringComparer.Ordinal).ToArray();
        var revisions = sources.ToDictionary(source => source.Key, source => source.Scope.PermissionRevision, StringComparer.Ordinal);
        var identity = sources.FirstOrDefault(source => source.IsCurrent);
        var knownKind = capabilities.ServerKind;

        // 来源键只说明「谁」，捕获作用域还说明「哪一次在线身份」。同键重建、同服换默认账号、
        // 离页或后一次刷新都不能借旧空表提交；Scope 是快照对象，不能拿新旧 Scope 引用比较。
        bool Current() => _active
            && ReferenceEquals(_capabilities, capabilities)
            && _administrationLoads.IsCurrent(token)
            && knownKind == capabilities.ServerKind
            && sources.All(source => source.IsAvailable && source.Scope.PermissionRevision == revisions[source.Key])
            && (identity is null || identity.IsCurrent)
            && keys.SequenceEqual(capabilities.Sources.Select(source => source.Key).Order(StringComparer.Ordinal));

        // 多服务器：Momoka 组之后每台在线服务器一组，默认全部收起。组里的入口绑定那一台自己的
        // 捕获作用域 —— 这正是从前只有独占模式才看得见的那批设置页，现在按服务器各归各的。
        if (sources.Select(source => source.ServerProfileId).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            ApplyMultiServerGroups(sources, new Dictionary<string, IReadOnlyList<ServerSettingsPluginPage>>(StringComparer.Ordinal));

            var pages = new Dictionary<string, IReadOnlyList<ServerSettingsPluginPage>>(StringComparer.Ordinal);
            await Task.WhenAll(sources
                .Where(source => source.IsAdministrator && source.Kind is MediaServerKind.Emby or MediaServerKind.Jellyfin)
                .Select(async source =>
                {
                    var list = await capabilities.GetAdminMenuPagesAsync(source.Key, token).ConfigureAwait(true);
                    lock (pages) pages[source.Key] = list;
                })).ConfigureAwait(true);

            if (!Current()) return;
            // 等待后重读每条来源的权限，不让等待前的管理员位重新打开已收起的目录。
            ApplyMultiServerGroups(capabilities.Sources, pages);
            return;
        }

        // 先同步已知状态，防止权限请求尚在等待时显示上一个账号、上一台服务器或上一个连接模式的管理页。
        // 产品一变立刻换上那一家的固定条目（插件页稍后补）；不能拿上一家的分组顶着新产品的名字。
        // 这一步也是命令行按分类打开的落点依据：OnNavigatedTo 里的 Select 紧接着同步这一段就执行。
        if (knownKind != _administrationKind || _serverEntries.Count == 0)
        {
            SetServerMenu(knownKind switch
            {
                MediaServerKind.Emby => ServerSettingsMenu.Build(),
                MediaServerKind.Jellyfin => JellyfinServerSettingsMenu.Build(),
                MediaServerKind.FnNas => FnNasServerMenu(),
                _ => []
            }, knownKind);
        }
        ApplyServerAdministration(CanShowServerAdministration, knownKind);
        if (capabilities.IsAdministrator is null)
            await capabilities.ProbeAsync(token).ConfigureAwait(true);
        if (!Current()) return;

        var visible = CanShowServerAdministration;
        var kind = capabilities.ServerKind;
        if (visible && kind is MediaServerKind.Emby or MediaServerKind.Jellyfin)
        {
            var pages = await capabilities.GetAdminMenuPagesAsync(token).ConfigureAwait(true);
            if (!Current()) return;

            visible = CanShowServerAdministration;
            SetServerMenu(!visible ? [] : kind == MediaServerKind.Emby
                ? ServerSettingsMenu.Build(pages)
                : JellyfinServerSettingsMenu.Build(pages), kind);
        }
        else
        {
            // 飞牛的四页是本机原生页，没有插件页要补；即使当前不是管理员，目录也先备着，
            // 下次权限恢复时那几项不用再依赖一次网络。
            SetServerMenu(kind == MediaServerKind.FnNas ? FnNasServerMenu() : [], kind);
        }

        ApplyServerAdministration(visible, kind);
    }

    /// <summary>缓存页重开只恢复读取活动期，不 Reload、保存或替换草稿集合。</summary>
    internal void Activate()
    {
        if (_disposed) return;
        _administrationLoads.Cancel();
        _active = true;
    }

    public override void Cancel()
    {
        _active = false;
        _administrationLoads.Cancel();
        base.Cancel();
    }

    public override void Dispose()
    {
        _disposed = true;
        _active = false;
        _administrationLoads.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// 多服务器时的左栏：Momoka 组之后，每台在线服务器一组。组默认收起；同一台服务器上有多个账号在线时
    /// 优先用管理员那一个 —— 那一个才看得见库管理入口。没有可管理入口的服务器不占一行。
    /// </summary>
    private void ApplyMultiServerGroups(IReadOnlyList<ServerSettingsSource> sources,
        IReadOnlyDictionary<string, IReadOnlyList<ServerSettingsPluginPage>> pages)
    {
        _showsFnNasAccount = false;
        _fnNasAccountHosted = [];
        _administrationKind = _capabilities?.ServerKind ?? MediaServerKind.Emby;

        var groups = new List<SettingsCategoryGroup>
        {
            MomokaGroup(serverAdministration: false, _administrationKind)
        };

        var previous = _categoryGroups
            .Where(group => group.SourceKey is not null)
            .ToDictionary(group => group.SourceKey!, group => group.IsExpanded, StringComparer.Ordinal);

        var selected = sources
            .GroupBy(source => source.ServerProfileId, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(source => source.IsAdministrator)
                .ThenByDescending(source => source.IsCurrent)
                .First())
            .OrderBy(ServerOrder)
            .ToArray();

        foreach (var source in selected)
        {
            var rows = SourceRows(source, pages.GetValueOrDefault(source.Key));
            if (rows.Count == 0) continue;

            var group = new SettingsCategoryGroup(source.Label, source.Key, collapsible: true,
                expanded: previous.TryGetValue(source.Key, out var expanded) && expanded);
            foreach (var row in rows)
            {
                if (row.IsSection) group.AddSection(row.Label);
                else group.Add(row.Label, source.Key, row.Route, SectionForCategory(row.Label));
            }
            groups.Add(group);
        }

        _showsServerAdministration = groups.Count > 1;
        ReplaceCategories(groups);
    }

    private int ServerOrder(ServerSettingsSource source)
    {
        var servers = _settings?.Settings.Servers;
        var index = servers?.FindIndex(server => string.Equals(server.Id, source.ServerProfileId, StringComparison.Ordinal)) ?? -1;
        return index >= 0 ? index : int.MaxValue;
    }

    /// <summary>左栏里的一行：原生目录的分区标题，或者一个能打开的入口。</summary>
    private sealed record ServerSourceRow(string Label, string Route = "", bool IsSection = false);

    /// <summary>
    /// 一条来源在左栏里列出的行。Emby／Jellyfin 保留它们网页控制台的原生分区（Emby 三段、Jellyfin 五段），
    /// 分区标题与入口交错；Emby 的通知列在原生分区之前。飞牛保留「帐号／影视服务器」两段：
    /// 帐号三项所有账号可见，影视服务器四项只在管理员时列出；Plex 没有这套设置。
    /// </summary>
    private static IReadOnlyList<ServerSourceRow> SourceRows(ServerSettingsSource source,
        IReadOnlyList<ServerSettingsPluginPage>? pages)
    {
        if (source.Kind == MediaServerKind.Emby)
        {
            if (!source.IsAdministrator) return [];
            var rows = new List<ServerSourceRow> { new(NotificationsCategory) };
            AddSections(rows, ServerSettingsMenu.Build(pages));
            return rows;
        }

        if (source.Kind == MediaServerKind.Jellyfin)
        {
            if (!source.IsAdministrator) return [];
            var rows = new List<ServerSourceRow>();
            AddSections(rows, JellyfinServerSettingsMenu.Build(pages));
            return rows;
        }

        if (source.Kind == MediaServerKind.FnNas)
        {
            var rows = new List<ServerSourceRow>
            {
                new("帐号", IsSection: true),
                new(FnNasPasswordCategory),
                new(FnNasPreferencesCategory),
                new(FnNasAppearanceCategory)
            };
            if (source.IsAdministrator)
            {
                rows.Add(new("影视服务器", IsSection: true));
                rows.AddRange(FnNasServerMenu()
                    .SelectMany(group => group.Entries)
                    .Select(entry => new ServerSourceRow(entry.Label, entry.Route)));
            }
            return rows;
        }

        return [];
    }

    private static void AddSections(List<ServerSourceRow> rows, IReadOnlyList<ServerSettingsMenuGroup> groups)
    {
        foreach (var group in groups)
        {
            rows.Add(new(group.Label, IsSection: true));
            rows.AddRange(group.Entries.Select(entry => new ServerSourceRow(entry.Label, entry.Route)));
        }
    }

    /// <summary>
    /// 飞牛影视的四个管理页。它们是 Momoka 的原生页而不是网页路由，<c>Route</c> 故意留空；
    /// 点击后的落点由 <c>SettingsPage</c> 按页名分派，但先要问选中项属于哪一种产品 ——
    /// Emby／Jellyfin 的目录里有同名的「媒体库」「用户」，它们的页名不能落进飞牛的原生页。
    /// </summary>
    internal static IReadOnlyList<ServerSettingsMenuGroup> FnNasServerMenu() =>
    [
        new("影视服务器", ServerSettingsSection.Server,
        [
            new(FnNasLibrariesCategory, "", ServerSettingsSection.Server, "library"),
            new(FnNasUsersCategory, "", ServerSettingsSection.Server, "users"),
            new(FnNasSettingsCategory, "", ServerSettingsSection.Server, "settings"),
            new(FnNasTasksCategory, "", ServerSettingsSection.Server, "tasks")
        ])
    ];

    /// <summary>
    /// 服务端管理入口此刻在不在。除了「账号能管这台服务器」，还要界面确实能确定是哪一台：
    /// 同时连着多台服务器时是通用界面，这些入口指向哪台没有答案，整组收起（用户令 2026-10-19）。
    /// </summary>
    private bool CanShowServerAdministration => _capabilities?.IsConnected == true
        && _capabilities.InterfaceFlavor != ServerInterfaceFlavor.Generic
        && ServerAccess.CanAdminister(_capabilities.ServerKind, _capabilities.IsAdministrator);

    internal bool CanOpenServerCategory(string value)
    {
        var entry = FindEntry(value);
        if (entry is not null)
        {
            if (entry.SourceKey is { } key)
            {
                var source = _capabilities?.Sources.FirstOrDefault(value => string.Equals(value.Key, key, StringComparison.Ordinal));
                if (source is null || !source.IsAvailable) return false;
                if (source.Kind == MediaServerKind.FnNas
                    && entry.Label is FnNasPasswordCategory or FnNasPreferencesCategory or FnNasAppearanceCategory) return true;
                return source.IsAdministrator && source.Kind is MediaServerKind.Emby or MediaServerKind.Jellyfin or MediaServerKind.FnNas;
            }
            return (!_fnNasAccountHosted.Contains(entry.Label, StringComparer.Ordinal) || _showsFnNasAccount)
                && (!_serverAdministrationCategories.Contains(entry.Label, StringComparer.Ordinal)
                    || (CanShowServerAdministration
                        && AdministrationCategories(_capabilities!.ServerKind).Contains(entry.Label, StringComparer.Ordinal)));
        }

        if (value.Contains('\u001f')) return false; // 已撤销/收起的显式来源键不能走普通本机分类回退。
        return (!_fnNasAccountHosted.Contains(value, StringComparer.Ordinal) || _showsFnNasAccount)
            && (!_serverAdministrationCategories.Contains(value, StringComparer.Ordinal)
                || (CanShowServerAdministration
                    && AdministrationCategories(_capabilities!.ServerKind).Contains(value, StringComparer.Ordinal)));
    }

    private bool IsHostedEntry(SettingsCategoryEntry entry) => entry.SourceKey is not null
        ? !string.Equals(entry.Label, FnNasAppearanceCategory, StringComparison.Ordinal)
        : FixedHostedCategories.Contains(entry.Label, StringComparer.Ordinal)
            || _fnNasAccountHosted.Contains(entry.Label, StringComparer.Ordinal)
            || _serverAdministrationCategories.Contains(entry.Label, StringComparer.Ordinal);

    internal IReadOnlyList<string> AvailableHostedCategories =>
        [.. Categories.Where(IsHostedEntry).Select(entry => entry.Key)];

    /// <summary>左栏某个服务器页（控制台、通用、插件页……）对应的网页路由；不是服务器页就答 null。</summary>
    internal string? ServerRoute(string value)
    {
        if (FindEntry(value)?.Route is { Length: > 0 } route) return route;
        return _serverRoutes.TryGetValue(value, out route) ? route : null;
    }

    /// <summary>
    /// 自检和控制台走查要落的那一页：两家目录的第一项都是「控制台」。没有管理入口时答 null。
    /// 「网页控制台」这个名字仍认，作为命令行和旧调用的兼容别名，见 <c>SettingsPage.Select</c>。
    /// </summary>
    internal string? ConsoleCategory =>
        Categories.FirstOrDefault(entry => string.Equals(entry.Label, "控制台", StringComparison.Ordinal)
            && string.Equals(entry.Route, ServerSettingsMenu.DefaultRoute, StringComparison.Ordinal))?.Key
        ?? _serverRoutes.FirstOrDefault(pair => pair.Value == ServerSettingsMenu.DefaultRoute).Key
        ?? (Categories.Any(entry => string.Equals(entry.Label, DashboardCategory, StringComparison.Ordinal)) ? DashboardCategory : null);

    /// <summary>
    /// 把当前账号该看得见的那几个入口装上或摘掉。
    /// <para>
    /// 分组那份换的是一个新表（理由见 <see cref="CategoryGroups"/>）；平铺那份按新目录重排，但保留
    /// ComboBox 里正被选中的那一项 —— 把选中项本身摘掉会让控件写回一个 null，落点就丢了。
    /// </para>
    /// </summary>
    private void ApplyServerAdministration(bool visible, MediaServerKind kind)
    {
        _showsServerAdministration = visible;
        _administrationKind = kind;
        // 帐号组只跟「在线唯一来源是飞牛」有关，不看管理员：改自己的密码和播放偏好不需要管理权限。
        _showsFnNasAccount = kind == MediaServerKind.FnNas
            && _capabilities?.IsConnected == true
            && _capabilities.InterfaceFlavor == ServerInterfaceFlavor.FnNas;
        _fnNasAccountHosted = _showsFnNasAccount ? FnNasAccountHostedCategories : [];
        ReplaceCategories(BuildCategoryGroups(visible, kind, _showsFnNasAccount));
    }

    /// <summary>
    /// 重排平铺名单，同时保住当前选中项：先按新键找回同一项，找不回再按旧分类名认一次，
    /// 都不在就落到第一项。左栏与紧凑下拉共用 <see cref="SelectedEntry"/>，所以它也跟着换。
    /// </summary>
    private void ReplaceCategories(IReadOnlyList<SettingsCategoryGroup> groups)
    {
        _categoryGroups = groups;
        var desired = groups.SelectMany(group => group.Entries).ToArray();
        if (desired.Length == 0) return;

        var keep = desired.FirstOrDefault(entry => string.Equals(entry.Key, SelectedCategory, StringComparison.Ordinal))
            ?? (SelectedCategory.Contains('\u001f')
                ? null
                : desired.FirstOrDefault(entry => string.Equals(entry.Label, SelectedCategory, StringComparison.Ordinal)))
            ?? desired[0];

        _syncingSelection = true;
        try
        {
            Categories.Clear();
            foreach (var entry in desired) Categories.Add(entry);
            // 权限收走选中管理项时，页面会在SelectedCategory通知内同步读取SelectedEntry并释放/导航。
            // 先把新落点交给它，不能让「主页」通知仍读到旧管理项而又打开一次已经降权的页面。
            SelectedEntry = keep;
            if (!string.Equals(SelectedCategory, keep.Key, StringComparison.Ordinal)) SelectedCategory = keep.Key;
        }
        finally { _syncingSelection = false; }

        ShowCategory(keep.Label);
        OnPropertyChanged(nameof(CategoryGroups));
    }

    /// <summary>
    /// 换上服务器插件页之后的目录，并把页名到路由的对应表一起更新。页名是左栏的显示字，插件页的显示名
    /// 也就是它的分类键；同名项只留最后一个路由，避免同一行指向两页。
    /// <para>
    /// 管理入口名单只收当前产品真正列出的页：Emby 多一个「通知」，Jellyfin 没有；「网页控制台」从两家
    /// 目录里退役之后不再是管理入口，只保留为命令行的别名。
    /// </para>
    /// </summary>
    private void SetServerMenu(IReadOnlyList<ServerSettingsMenuGroup> groups, MediaServerKind kind)
    {
        _serverGroups = groups;
        _serverEntries = [.. groups.SelectMany(group => group.Entries)];

        var routes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in _serverEntries) routes[entry.Label] = entry.Route;
        _serverRoutes = routes;

        _serverAdministrationCategories =
        [
            .. MomokaAdministrationCategories(kind)
                .Concat(_serverEntries.Select(entry => entry.Label))
                .Distinct(StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// Whether the font picker holds the machine's fonts rather than just the stored one. Only the
    /// self-check reads it: the scan lands a moment after the page does, and a report written in that
    /// moment would be reporting on a list that had not arrived yet.
    /// </summary>
    internal bool FontsReady { get; private set; }

    /// <summary>
    /// The 字幕 → 字体 picker, put through a search and back. Null when the card has not been built.
    /// </summary>
    internal SettingFontRow.Probe? MeasureFontPicker() => _subtitleFont?.Measure();

    /// <summary>
    /// Builds every card. Nothing here waits on the network — the settings document is already in memory —
    /// so this is synchronous work behind the async signature the base class defines for the pages that do
    /// have something to fetch. The one thing it does not have in hand is the font list, which is handed to
    /// the 字幕 card's picker whenever the scan comes back; the page is usable in the meantime.
    /// </summary>
    public override Task ReloadAsync()
    {
        if (_settings is null) return Task.CompletedTask;
        SyncSaveState();

        // 精简模式是页级状态（SettingRow.NotesHidden），建卡之前先对齐 —— 行的可见性在容器落到树上那一刻
        // 才求值，这里晚了才是错的。恢复默认那趟重走这里，同一句话把它拨回新文档的样子。
        SettingRow.NotesHidden = Settings.Ui.CompactMode;

        // 建卡次序照左栏那份（CardCategories，用户令 2026-10-11 排的序），好让分类走查、Cards 分组这些
        // 按 Sections 次序读数的地方与屏上的次序是同一份。同一分类的多张分区卡连着排：播放行为分类三张
        // （播放行为 / 资源筛选 / 截图），视频输出分类三张（基础输出 / HDR / 画质与着色器）。
        Sections.Clear();
        Sections.Add(HomeCard());
        Sections.Add(InterfaceCard());
        Sections.Add(PlayerCard());
        if (MpvCustomConfig.IsBuiltInBackend(Settings.Mpv)) Sections.Add(CustomMpvConfigCard());
        Sections.Add(PlaybackCard());
        Sections.Add(ResourceFilterCard());
        Sections.Add(ScreenshotCard());
        Sections.Add(VideoCard());
        Sections.Add(ApplicableHdrCard());
        Sections.Add(ApplicableShaderCard());
        Sections.Add(AudioCard());
        Sections.Add(SubtitleCard());
        Sections.Add(ShortcutsCard());
        Sections.Add(MoviePilotCard());
        Sections.Add(AboutCard());
        _ = MeasureImageCacheAsync();

        ShowCategory(SelectedCategory);
        IsReady = true;

        return Task.WhenAll(FillFontsAsync(), FillAudioDevicesAsync());
    }

    /// <summary>
    /// Re-enumerates the 音频输出设备 row on a cached page's re-entry. The page is deliberately cached
    /// (half-typed text survives navigation), so ReloadAsync does not run again. The device list is a reading,
    /// not a preference; shared settings may also change dependent rows' applicability while the page is away.
    /// Only those dependent rows and changed controlling values update, preserving unrelated in-progress edits.
    /// </summary>
    internal Task RefreshAudioDevicesAsync()
    {
        // 缓存重入只原位增删适用性变化的从属行，不整页 Reload，也不改半输入与保存状态。
        RefreshSettingsApplicability();
        return FillAudioDevicesAsync();
    }

    /// <summary>
    /// Hands the 音频输出设备 row the machine's real devices once they have been enumerated. Same shape and same
    /// reason as <see cref="FillFontsAsync"/>: the list comes out of a throwaway libmpv context, which is tens
    /// of milliseconds of native work, and the page is worth more than that one row being complete on the first
    /// frame. Until it lands the row holds 「跟随系统默认设备」 plus whatever the settings file names.
    /// </summary>
    private async Task FillAudioDevicesAsync()
    {
        if (_audioDevices is null || _audioDevice is null) return;

        var row = _audioDevice;
        var devices = await _audioDevices.LoadAsync().ConfigureAwait(true);
        if (!ReferenceEquals(row, _audioDevice)) return;

        var audio = Settings.Audio;
        var (choices, selected) = DeviceChoices(audio, devices);
        row.Fill(choices, selected);
    }

    /// <summary>
    /// The 音频输出设备 drop-down: 「跟随系统默认设备」 first, then whatever mpv found. Passing an empty list is
    /// the normal first-frame state and the permanent state on a machine where libmpv could not enumerate —
    /// the row is still usable, it just offers the default and whatever the settings file names.
    /// <para>
    /// mpv's own <c>auto</c> entry never reaches here: <see cref="AudioDeviceCatalogue.Selectable"/> drops it,
    /// in Core, where a unit test can hold it down. Leaving it in put the same behaviour on the list twice —
    /// 「跟随系统默认设备」 and, right underneath, mpv's English 「Autoselect device」. Two rows for one answer is
    /// bad enough; picking the second one also stored <c>auto</c> instead of the empty string, so the row
    /// afterwards read 「Autoselect device」 to somebody who believed he had chosen the system default.
    /// </para>
    /// </summary>
    private (List<SettingChoice> Choices, SettingChoice? Selected) DeviceChoices(
        AudioSettings audio,
        IReadOnlyList<AudioDevice> devices)
    {
        (string Label, string Value)[] options =
        [
            ("跟随系统默认设备", ""),
            .. devices.Select(device => (Label: device.Label, Value: device.Name))
        ];

        return Options(
            options,
            () => audio.Device,
            value => audio.Device = value,
            StringComparer.OrdinalIgnoreCase,
            stored => $"{stored}（已保存，本次内置设备名单中未找到）");
    }

    /// <summary>
    /// Hands the font picker the machine's families once they have been read. Awaited by nobody — the page
    /// is shown before this returns — so the row filling itself is the only visible effect.
    /// </summary>
    private async Task FillFontsAsync()
    {
        if (_fonts is null || _subtitleFont is null)
        {
            // Nothing to wait for: without a library the row keeps the one family the settings file names.
            FontsReady = true;
            return;
        }

        var catalogue = await _fonts.LoadAsync().ConfigureAwait(true);

        // The row may have been rebuilt while the scan ran — a second visit to the page does that — so the
        // current one is asked for rather than the one captured above.
        _subtitleFont?.Fill(catalogue);
        FontsReady = true;
    }

    /// <summary>
    /// 「恢复默认」「恢复配置」都要弹一次确认框，而 <c>ContentDialog</c> 同时只许一个 —— 这一挡挡掉对话框还没
    /// 合上时的第二次点击。备份不弹框（它不动任何东西），不占这一挡。
    /// </summary>
    private bool _restoring;


    /// <summary>存文件框：给一个建议文件名，回来的是用户选的落点（取消或弹不出来是 null）。见 <see cref="Views.SettingsFile"/>。</summary>
    internal delegate Task<string?> SaveFileRequest(string suggestedName);

    /// <summary>开文件框：回来的是用户挑的那个文件的路径（取消或弹不出来是 null）。见 <see cref="Views.SettingsFile"/>。</summary>
    internal delegate Task<string?> OpenFileRequest();

    private SaveFileRequest? _saveFile;
    private OpenFileRequest? _openFile;

    /// <summary>
    /// 页面把两个文件框交进来（备份要存、恢复配置要开），和基类的 <c>UseConfirm</c> 交确认框是同一手：文件框
    /// 要页面的 <see cref="Microsoft.UI.Xaml.XamlRoot"/> 才拿得到窗口句柄，摆得出它的只有页面。
    /// </summary>
    internal void UseFilePickers(SaveFileRequest save, OpenFileRequest open)
    {
        _saveFile = save;
        _openFile = open;
    }

    /// <summary>
    /// 自检用：备份 / 恢复配置那两颗按钮的文件框接上了没有 —— 没接上点下去弹不出框，什么都不会发生，屏上一个
    /// 字都不说（同基类的 <c>CanConfirm</c> 盯确认框那根线）。
    /// </summary>
    internal bool CanPickFiles => _saveFile is not null && _openFile is not null;

    private void ShowCategory(string category)
    {
        var sectionCategory = SectionForCategory(category);
        foreach (var section in Sections)
        {
            section.IsVisible = string.Equals(section.Category, sectionCategory, StringComparison.Ordinal);
            if (section.Category == "界面")
                section.Retitle(string.Equals(category, FnNasAppearanceCategory, StringComparison.Ordinal) ? FnNasAppearanceCategory : "界面");
        }

        // 主页那张版面表只在它显示着的时候重填。收着的时候 ListView 不重排，重填进来的那几排只建得出容器、
        // 套不上模板 —— 用户回到这一页看到的是几个空盒子（见 RefreshHomeRows）。
        if (string.Equals(sectionCategory, FirstCardCategory, StringComparison.Ordinal)) RefreshHomeRows();
    }

    private AppSettings Settings => _settings!.Settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnsavedVisibility))]
    [NotifyCanExecuteChangedFor(nameof(RetrySaveCommand))]
    public partial bool HasUnsavedChanges { get; set; }

    public Visibility UnsavedVisibility => Show(HasUnsavedChanges);

    /// <summary>
    /// 缓存页重入时只对齐共享服务的保存结果；别的页面也可能保存成功或失败。
    /// 不重建卡片、不写盘，保留半输入、分类与集合实例；属性通知同时更新提示和重试命令。
    /// </summary>
    internal void SyncSaveState()
    {
        if (_settings is { } settings) HasUnsavedChanges = settings.HasUnsavedChanges;
    }

    private void Save()
    {
        if (_settings is null) return;
        HasUnsavedChanges = !_settings.TrySave();
    }

    [RelayCommand(CanExecute = nameof(HasUnsavedChanges))]
    private void RetrySave()
    {
        Save();
        if (!HasUnsavedChanges) Notify(null, "设置已保存。", InfoBarSeverity.Success);
    }

    // ── Cards ────────────────────────────────────────────────────────────────────────────────────────

    // 2026-10-11 一天数改：先「把播放器移到视频输出里」，再「移回侧边栏」，然后「把渲染管线移到视频输出、
    // 播放器卡分区」，再「两个功能合在一起，播放后端为内置时隐藏外置相关」—— 外置播放器分区并回播放器卡，
    // 路径与 IPC 两行只有外部后端才构造（重建不写回偏好，切回外部后端恢复原值）。渲染管线行归视频输出
    // 「基础输出」卡 —— 与图形接口的显隐联动随行过去，切管线在那一行自己的 after 里重建它所在的卡（那次
    // 重建从此发生在行自己的卡上，探针钉着这条路）；当日早些时候自成分区卡的截图，随「截图分区移到播放
    // 行为里」搬去了播放行为分类。左栏排序同日重排，播放器不再是第一张卡（见 CardCategories）。
    private SettingSection PlayerCard()
    {
        var rows = new List<SettingRow>
        {
            Pick("播放后端", null, Backends, () => Settings.Mpv.Backend, value => Settings.Mpv.Backend = value,
                EqualityComparer<MpvBackendKind>.Default, after: RefreshPlayerBackend)
        };
        if (Settings.Mpv.Backend == MpvBackendKind.ExternalMpv)
        {
            // 认证只经过核验了服务端进程身份的管道。关闭进度不关闭这个启动必需通道；失败就停止，
            // 不退回命令行或临时明文文件。它仍然信任所选程序和同一 Windows 用户下的代码。
            rows.Add(PathBox("mpv.exe 路径", "mpv.exe 路径", () => Settings.Mpv.ExecutablePath, value => Settings.Mpv.ExecutablePath = value,
                "只有「外部 mpv.exe」后端需要它。访问令牌通过核验进程身份的管道传递，不放进命令行或临时文件；"
                    + "无法建立安全通道就停止起播。请只选择可信的 mpv.exe，同一 Windows 用户下的恶意程序仍可能窃取凭据。"));

            rows.Add(Toggle("启用 IPC 进度通道", "关掉后不读取或上报进度，也不提供客户端控制；安全起播和退出仍需管道", () => Settings.Mpv.EnableIpc, value => Settings.Mpv.EnableIpc = value));
        }
        return new SettingSection("播放器", "播放器", "用程序里内置的播放器播，还是调起独立的 mpv.exe 来播。", rows);
    }

    /// <summary>
    /// 播放后端那一行的 after：先重建这一行自己住的播放器卡（外置两行随值进出，与渲染管线行重建自己那
    /// 张卡同一条路），再走视频卡与自定义 mpv.conf 的联动。
    /// </summary>
    private void RefreshPlayerBackend()
    {
        var index = Sections.ToList().FindIndex(section => section.Title == "播放器");
        if (index >= 0) Sections[index].UpdateRows(PlayerCard().Rows);
        RefreshVideoBackend();
    }

    /// <summary>
    /// 播放器卡之后的分区卡：<b>内置播放器后端就建卡</b>（用户令 2026-10-16「播放后端选择内置播放器时自定义
    /// mpv.conf 不自动隐藏」）。卡里那一项只在独占模式下可选；集成模式下它固定为关，那一行换成只读文字
    /// （判定在 Core 的 <see cref="MpvCustomConfig.IsAvailable"/>，与视频输出卡上「图形接口」在集成模式下的做法
    /// 同一条路）。外部后端整张卡收起，因为那时读的是 mpv.exe 自己的配置。下次播放生效。
    /// </summary>
    private SettingSection CustomMpvConfigCard()
    {
        var rows = new List<SettingRow>();
        if (MpvCustomConfig.IsAvailable(Settings.Mpv))
        {
            rows.Add(Toggle("启用自定义 mpv.conf", "读取指定文件中的音视频参数，覆盖本程序的常规播放设置；窗口与播放控制仍由 Momoka 管理。",
                () => Settings.Mpv.CustomConfigEnabled, value => Settings.Mpv.CustomConfigEnabled = value,
                after: RefreshCustomMpvConfig));
            if (Settings.Mpv.CustomConfigEnabled)
                rows.Add(PathBox("mpv.conf 路径", "选择并粘贴 mpv.conf 的完整路径",
                    () => Settings.Mpv.CustomConfigPath, value => Settings.Mpv.CustomConfigPath = value,
                    "支持带引号的路径。文件内的 ~~/ 指向配置所在目录；不自动载入该目录的 input.conf 和脚本。"));
        }
        else
        {
            // 集成模式：不读外部配置文件，这一项固定关闭。只读文字展示有效值，不动用户存着的偏好 ——
            // 切回独占模式时原来那个开关和路径都还在（隐藏/不可用不清空偏好）。
            rows.Add(Fact("启用自定义 mpv.conf",
                "集成模式不读取外部配置文件，这一项固定关闭；切成独占模式后可以选择。", "关"));
        }
        return new SettingSection("播放器", "自定义 mpv.conf",
            "内置播放器可用；只有独占模式会读取这个文件，下次播放生效。", rows);
    }

    private void RefreshCustomMpvConfig()
    {
        var index = Sections.ToList().FindIndex(section => section.Title == "自定义 mpv.conf");
        if (MpvCustomConfig.IsBuiltInBackend(Settings.Mpv))
        {
            if (index >= 0) Sections[index].UpdateRows(CustomMpvConfigCard().Rows);
            else Sections.Insert(Sections.ToList().FindIndex(section => section.Title == "播放器") + 1, CustomMpvConfigCard());
        }
        else if (index >= 0) Sections.RemoveAt(index);
        ShowCategory(SelectedCategory);
    }

    /// <summary>播放行为分类的第三张分区卡（用户令 2026-10-11「截图分区移到播放行为里」；当日早些时候它
    /// 还是播放器分类在播放器卡之后的分区卡，「参考文档新增自定义选项」令下诞生。同日再令「默认要用png」
    /// 「开无损后下面的压缩和质量自动隐藏」「PNG（默认）排第一个 jpg第二webp第三，另外两个删掉」与
    /// 「jpg呢？不用压缩吗？」）。</summary>
    private SettingSection ScreenshotCard()
    {
        var mpv = Settings.Mpv;
        var rows = new List<SettingRow>
        {
            // 「在设置中新增截图保存目录」（用户令 2026-09-29）。空 = 装机落点，裁决在 Core 的
            // AppPaths.ResolveScreenshotDirectory。每敲一键提交一次（SettingTextRow 的节奏），所以这里只做
            // 三件不要钱的事：写设置、刷新「关于」卡那行读数、把裁决后的目录推给正在播的那部 ——
            // 建目录不在这一档（会把路径的每个前缀都建成文件夹），它归启动和「关于」卡的「打开」。
            PathBox("截图保存目录", "例如 D:\\影屏截图", () => mpv.ScreenshotDirectory, value =>
            {
                mpv.ScreenshotDirectory = value;
                AnnounceScreenshotDirectory();
            }, "播放器「画面」菜单里三档截图存到这儿，文件名是片名、播放时间和序号。留空用装机落点（数据目录下的 "
                + "screenshots 文件夹）；改完当场生效，正播着的那部也从下一张截图起落进新目录"),

            // 截图画质是参考 mpv 配置里那组参数（webp 质量 85／压缩 6、jpg 质量 100、不标记色彩空间），默认格式按
            // 用户令用 png。每一行写完盘就把整组推给正在播的那部（PushScreenshotOptions），不猜哪条变了 ——
            // 运行中的播放器上「不推」就是「保持我上一分钟发的那条」。范围常量与夹取在 Core 的
            // MpvBaseline.ScreenshotOptions，滑块只是同一套数字的控件端。
            Mpv("截图格式", MpvBaseline.ScreenshotFormats, () => mpv.ScreenshotFormat,
                value => mpv.ScreenshotFormat = value, "screenshot-format",
                "装机默认 png。选 WebP 出现无损、质量与压缩，选 JPG 出现质量；其余格式用 mpv 自己的默认。",
                RefreshScreenshotCard),
            Toggle("色彩空间标记", "把画面的色彩空间写进文件。默认关：关着或格式不支持时 mpv 把画面转成 sRGB 再落盘，"
                + "HDR 片源的截图因此是 sRGB 图。",
                () => mpv.ScreenshotTagColorspace, value => mpv.ScreenshotTagColorspace = value,
                "screenshot-tag-colorspace", PushScreenshotOptions),
        };

        // 各格式的画质行只在该它们在场的时候在场（用户令「开无损后下面的压缩和质量要是没用了就自动隐藏」，
        // 同一原则顺着推到格式：不是 WebP，三行整个没用）：换格式或拨无损由各自行的 after 原地更新从属行 ——
        // 与渲染管线行重建基础输出卡同一条机制（那条路径有探针钉着）。
        if (string.Equals(mpv.ScreenshotFormat, MpvBaseline.ScreenshotOptions.WebpFormat, StringComparison.OrdinalIgnoreCase))
        {
            rows.Add(Toggle("WebP 无损", "无损截图文件大得多，画质一点不损；开启时下面两行收起来。",
                () => mpv.ScreenshotWebpLossless, value => mpv.ScreenshotWebpLossless = value,
                "screenshot-webp-lossless", RefreshScreenshotCard));
            if (!mpv.ScreenshotWebpLossless)
            {
                rows.Add(Slider("WebP 质量", MpvBaseline.ScreenshotOptions.MinimumQuality, MpvBaseline.ScreenshotOptions.MaximumQuality, 5,
                    () => mpv.ScreenshotWebpQuality, value => mpv.ScreenshotWebpQuality = (int)value,
                    "有损 WebP 的质量，越高越接近原画面、文件越大。", "screenshot-webp-quality", PushScreenshotOptions));
                rows.Add(Slider("WebP 压缩", MpvBaseline.ScreenshotOptions.MinimumCompression, MpvBaseline.ScreenshotOptions.MaximumCompression, 1,
                    () => mpv.ScreenshotWebpCompression, value => mpv.ScreenshotWebpCompression = (int)value,
                    "压缩等级，越高编码越慢；有损 WebP 时它也影响画质。", "screenshot-webp-compression", PushScreenshotOptions));
            }
        }
        else if (string.Equals(mpv.ScreenshotFormat, MpvBaseline.ScreenshotOptions.JpegFormat, StringComparison.OrdinalIgnoreCase))
        {
            rows.Add(Slider("JPG 质量", MpvBaseline.ScreenshotOptions.MinimumQuality, MpvBaseline.ScreenshotOptions.MaximumQuality, 5,
                () => mpv.ScreenshotJpegQuality, value => mpv.ScreenshotJpegQuality = (int)value,
                "JPEG 压缩质量，mpv 自己的默认是 90。", "screenshot-jpeg-quality", PushScreenshotOptions));
        }

        return new SettingSection("播放行为", "截图",
            "播放器「画面」菜单里三档截图的落点与画质。改完当场生效：正播着的那部从下一张截图起照新值落盘。",
            rows);
    }

    /// <summary>截图格式或无损开关变了，当场重建这张卡：WebP 三行按条件进出场（同渲染管线行重建基础输出卡）。
    /// 更新之外还要推一遍 —— 换成 WebP 要把三条带值发出去、拨无损要把新状态发出去，运行中的播放器上
    /// 「不推」就是「保持我上一分钟发的那条」。</summary>
    private void RefreshScreenshotCard()
    {
        var index = Sections.ToList().FindIndex(section => section.Title == "截图");
        if (index >= 0) Sections[index].UpdateRows(ScreenshotCard().Rows);
        ShowCategory(SelectedCategory);
        PushScreenshotOptions();
    }

    /// <summary>截图画质各行共用的「推给正在播的那部」：整组发，没在播就是无事可做。</summary>
    private void PushScreenshotOptions() => _ = PushScreenshotOptionsAsync();

    private async Task<bool> PushScreenshotOptionsAsync()
    {
        if (_pushScreenshotOptions is null) return true;
        try
        {
            await _pushScreenshotOptions().ConfigureAwait(true);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception error)
        {
            Report("当前播放的截图选项未完全更新", error);
            return false;
        }
    }

    private SettingSection PlaybackCard()
    {
        var playback = Settings.Playback;
        return new SettingSection("播放行为", "播放行为", "断点续播、进度上报、片头片尾和音轨选择。",
        [
            Toggle("向服务器汇报播放进度", "关闭后服务器不再记录本次播放进度", () => playback.ReportProgressToServer, value => playback.ReportProgressToServer = value),
            Toggle("从服务器保存的位置继续", "关掉后每次都从片头放起", () => playback.ResumeFromSavedPosition, value => playback.ResumeFromSavedPosition = value),
            Toggle("自动播放下一集", "一集放完自动接下一集，本季放完退出播放", () => playback.AutoPlayNextEpisode, value => playback.AutoPlayNextEpisode = value),

            // 「在设置中新增一个开始播放后自动全屏的功能」（用户的话，2026-09-13）。这一行只影响「下一个
            // 播放怎么开始」，改完当场生效 —— 设置页里改这一下，播放器那一头下一次起播就认新的了（现读
            // 同一个设置），所以没有 ShellPrefs 那一句：那个东西是给「已经摆在屏上的东西要重排」用的
            // （主页、图片预算），这一行底下没有已经摆着的东西要动。
            // 「用独立窗口播放」那一行曾在这附近（2026-09-13～2026-09-19）：行为并入独占模式后开关随令
            // 删除；同日独立控制窗也随令退场，独占模式以视频窗里的 uosc 为唯一控制面 ——
            // 见 PlayerViewModel.HeadlessPlayback。
            Toggle("开始播放后自动全屏", "每开始放一部片子就自动进全屏，不用再按 F。只在开始播放那一下进，"
                + "连播的下一集不会把你从全屏里顶出来，按 F 退出后也不会被下一集顶回去",
                () => playback.AutoFullscreenOnPlayback, value => playback.AutoFullscreenOnPlayback = value),

            // 「调整窗口大小后继续播放」（用户令 2026-09-23）：拖窗口边沿那一趟会先把 mpv 冻住再抓帧（同
            // 2026-09-22 切全屏那一趟 —— 覆盖层上那帧与撤掉时屏上那帧同帧），这一行只管松手之后往哪边走。
            // 冻那一半没有开关；而且用户自己按下的暂停一根手指都不碰，与这一档无关。
            // 现读设置，不走任何通知管道：下一次拖边收尾就认新的值。
            Toggle("调整窗口大小后继续播放",
                "拖窗口边沿改大小的时候画面会先停住，松开鼠标后自动接着放。关掉它就停在暂停，要自己按播放。"
                + "只对集成模式有效（画面嵌在本窗口里的那种）；独占模式的窗口归 mpv 自己管，拖动不经过这里",
                () => playback.ResumeAfterWindowResize, value => playback.ResumeAfterWindowResize = value),

            Number("标记已观看阈值（%）", 50, 100, () => playback.MarkWatchedPercent, value => playback.MarkWatchedPercent = value,
                "放到这个百分比以上，这一条就算看过"),

            // 国漫单独一档（「新增国漫播放进度自定义百分比标记已看」，用户令 2026-09-26）：命中的判定在 Core 的
            // DonghuaRule（类型：动画 ∧ 发行公司带腾讯/哔哩哔哩），这里只管数值。现读设置，下一次播放结束就认。
            Number("国漫标记已观看阈值（%）", 50, 100, () => playback.DonghuaMarkWatchedPercent, value => playback.DonghuaMarkWatchedPercent = value,
                "类型为动画、发行公司带腾讯、哔哩哔哩、优酷或爱奇艺的片子单独按这一档算，不吃上面那行的值"),
            // 四颗方向键、两对步长（2026-09-20 用户令）：← / → 用上面这两行，↑ / ↓ 用下面那两行。两对都印在
            // 设置页上，因为两个管线各有一套输入处理 —— 集成模式的键盘归 shell 的快捷键表，独占模式的键盘是
            // mpv 自己那层（keybind，见 <c>MpvSeekKeys</c>）—— 而两套都从这几个字段取值。屏上印的数字与按下
            // 去的位移因此永远对得上，不会出现「改了设置、按键还是老样子」那种界面在骗人。
            Number("快进跨度（秒）", 1, 600, () => playback.SeekForwardSeconds, value => playback.SeekForwardSeconds = value,
                "→ 键跳这么多秒"),
            Number("快退跨度（秒）", 1, 600, () => playback.SeekBackwardSeconds, value => playback.SeekBackwardSeconds = value,
                "← 键跳这么多秒"),
            Number("大步快进跨度（秒）", 1, 600, () => playback.SeekForwardLongSeconds, value => playback.SeekForwardLongSeconds = value,
                "↑ 键跳这么多秒"),
            Number("大步快退跨度（秒）", 1, 600, () => playback.SeekBackwardLongSeconds, value => playback.SeekBackwardLongSeconds = value,
                "↓ 键跳这么多秒"),
            Number("续播自动快退（秒）", 0, 120, () => playback.ResumeRewindSeconds, value => playback.ResumeRewindSeconds = value),
            Number("进度上报间隔（秒）", 1, 60, () => playback.ProgressReportIntervalSeconds, value => playback.ProgressReportIntervalSeconds = value),
            Choice("跳过片头片尾", SkipModes, () => playback.SkipSections, value => playback.SkipSections = value),
            // 音轨语言优先级, the same drop-down as the 字幕 card's since 2026-09-22（「给音轨新增语言优先级和格式
            // 优先级」——语言这栏跟字幕一样改成勾选下拉）. It was a single-pick up to v9, which could not express
            // 「日语 > 粤语 > 英语」 at all; an empty list is 「跟随服务器默认音轨」. Unlike 字幕, 音轨 keeps 普通话／粤语
            // in the menu —— they name a spoken variant, which is exactly an 音轨 distinction.
            LanguagePriority("音轨语言优先级", "未指定，跟随服务器默认音轨", () => playback.AudioLanguages, value => playback.AudioLanguages = value,
                "点开勾语言，越靠上越优先，按住或用箭头换次序。空着就是「跟随服务器默认音轨」。"
                    + "「其他字幕」这类兜底项对音轨同样代表「前面都没有时，有一条别的语言的总比没有好」。下次播放生效"),

            // 音轨格式筛选（「给音轨新增语言优先级和格式优先级」，2026-09-22）：语言选完之后，在同一语言那批轨里按
            // 编码／声道的关键词再挑一遍。和字幕标题筛选、视频文件名筛选同一套控件与有序优先级（KeywordRules），只是它看的
            // 是 TrueHD／DTS-HD／Atmos／7.1／5.1 这些格式词。出厂几条常见词都设成默认（不生效）。
            KeywordRules("音轨格式筛选", () => playback.AudioFormatRules, value => playback.AudioFormatRules = value,
                "语言选出「哪几条是这个语言」之后，再按编码／声道里的词挑一遍：优先＝含这个词的排前头，候补＝排到最后"
                    + "（同语言只剩它时仍会给一条），默认＝不生效。预置 Atmos、TrueHD、DTS-HD、DTS、FLAC、7.1、5.1，"
                    + "输入框可自定义添加，拖动调整优先级。格式只在同一语言内部作用，不会越过语言优先级。下次播放生效")
        ]);
    }

    /// <summary>
    /// 播放行为分类的第二张分区卡（用户令 2026-10-11「播放行为里分三个分区…一个是资源筛选」）：一部片子挂
    /// 多版本时挑哪一版 —— 媒体源排序与视频文件名筛选从播放行为卡搬过来凑成这一张，行与说明原样照搬。
    /// 音轨那两行没有跟着来：它们挑的是一部片子<b>里面</b>的轨，不是多版本之间的那一版，归播放行为。
    /// </summary>
    private SettingSection ResourceFilterCard()
    {
        var playback = Settings.Playback;
        return new SettingSection("播放行为", "资源筛选",
            "一部片子有多个版本（多个文件）时挑哪一版：媒体源按什么次序排，默认播的那一版按文件名里的词筛。",
        [
            // 媒体源排序（2026-09-24）：一个条目挂多版本时详情页媒体源行、播放器版本菜单的次序，也顺带决定
            // 没挑到规则时默认播哪一版。现读设置 —— 下一次打开详情页或下一次起播就认新的值，没有已经摆着的
            // 东西要重排。
            Choice("媒体源排序", MediaSourceOrders, () => playback.MediaSourceOrder, value => playback.MediaSourceOrder = value,
                "一部片子有多个版本（多个文件）时，详情页「媒体源」那一行和播放器里「版本」菜单的次序："
                    + "默认＝服务器给的次序；新入库在前＝最新入库的那一版排最前（按服务器记的入库时间，"
                    + "每一版要多问一次服务器）。没有文件名规则可依时，默认播的也是排在最前的那一版。"
                    + "只有一个版本的片子不受影响。下次打开详情页或下次播放生效"),

            // 视频文件名筛选（「参考标题筛选，新增视频文件名筛选」，2026-09-22）：一个条目挂多版本（多个文件）时，
            // 默认播哪一版按文件名里的关键词挑。和字幕标题筛选同一套控件与有序优先级，只是它筛的是版本文件名。
            KeywordRules("视频文件名筛选", () => playback.VideoFileRules, value => playback.VideoFileRules = value,
                "一部片子有多个版本（多个文件）时，按文件名里的词决定默认播哪一版：拖动调整顺序，越靠上优先级越高；"
                    + "优先＝含这个词的版本优先，候补＝往后排（只剩它时仍会用它），默认＝不生效。"
                    + "输入框可自定义添加，如 REMUX、4K、HDR、枪版。只有一个版本的片子不受影响。下次播放生效")
        ]);
    }

    private SettingSection SubtitleCard()
    {
        var playback = Settings.Playback;

        // 「参考图2新增字幕外观功能」（2026-09-06）：卡顶这条「字幕示例」，照下面外观各行的当前值实时画。
        // 它是行列表的第一行，重画的线只有 Live<T> 那一条 —— 外观每一行写完设置都从那儿过，所以预览
        // 不可能停在旧样子上。
        var preview = new SettingSubtitlePreviewRow("字幕示例",
            "纯文本字幕的样式示意：两行文字可比较逐行盒与整体背景盒，大小会跟随字号和缩放。"
                + "不是 libass 播放画面；实际尺寸还受窗口大小与字幕格式影响。", playback);
        _subtitlePreview = preview;

        var rows = new List<SettingRow>
        {
            preview,

            LanguagePriority("字幕语言优先级", "未指定，跟随文件默认字幕", () => playback.SubtitleLanguages, value => playback.SubtitleLanguages = value,
                "点开勾语言，越靠上越优先，按住或用箭头换次序。「其他字幕」是一个预设项，代表任何别的语言的字幕 —— "
                    + "排在最后就是「前面都不匹配时，有一条别的语言的总比没有好」。空列表只跟随明确的默认轨；"
                    + "仍服从显示模式，没有默认轨就不显示。下次播放生效",
                // 普通话、粤语是「说的是哪种」，那是音轨的区分，字幕轨不按这个标；字幕表不列它们（音轨那份仍可用）。
                exclude: ["普通话", "粤语"]),

            // 标题筛选：语言选完之后按标题里的关键词再挑一遍（「先确定哪几条是中文，再决定不要双语和特效」）。
            // 语言和标题从此分家 —— 语言只看轨的语言字段，这里只看标题；表内次序就是优先级。
            KeywordRules("标题筛选", () => playback.SubtitleTitleRules, value => playback.SubtitleTitleRules = value,
                "语言选出「哪几条是中文」之后，再按标题里的词挑一遍：拖动调整顺序，越靠上优先级越高；优先＝含这个词的排前头，"
                    + "候补＝排到最后（同语言只剩它时仍会给一条，不至于没字幕），默认＝不生效。输入框可自定义添加词。"
                    + "另：字幕语言里选了「简体中文」时，会自动把标题带「繁 / 繁体」的排为候补。下次播放生效"),
            Choice("显示模式", SubtitleModes, () => playback.SubtitleMode, value => playback.SubtitleMode = value),
            Toggle("没有匹配语言时使用默认字幕", "只回退服务器指定或文件标记的默认字幕，不再按标题改选别的轨道；没有默认轨就不显示。"
                + "允许任意语言兜底请在语言列表末尾加入「其他字幕」。空语言列表不受本开关影响", () => playback.SubtitleFallbackToDefault, value => playback.SubtitleFallbackToDefault = value),

            // 这一行管着它下面那一整组。放在这儿而不是外观末尾：番剧和压制组的内封字幕大量是 ASS，
            // 而 mpv 默认让 ASS 自己的样式说话 —— 也就是说下面九行对那些文件一个字都改不动。
            Mpv("外观应用范围", MpvOutputOptions.SubtitleStyleScopes,
                () => playback.SubtitleAssOverride, Live<string>(value => playback.SubtitleAssOverride = value),
                "sub-ass-override",
                "ASS/SSA 字幕自带字体和颜色，默认由它自己说了算，下面这些外观只对纯文本字幕（srt/vtt）生效。"
                    + "强制覆盖可能破坏 ASS 的定位与特效。蓝光原盘的图形字幕（PGS/VOBSUB）是图片，字体和文字颜色无法改变它"),

            Font("字体", "列出这台机器装的字体和程序自带的字体，可搜索；mpv 认的是字体族名，不是文件路径",
                () => playback.SubtitleFontFamily, Live<string>(value => playback.SubtitleFontFamily = value), "sub-font"),

            // 字号那一行原本能存 0（「不指定，mpv 自己是 38」），滑块没有「不指定」这个状态，所以装值时把 0
            // 落到 38 上 —— 那是同一个大小换了个写法；拖一下就把 38 写实了（见 UnitSlider 那条注释，同一件事）。
            Slider("字号", PlaybackSettings.MinimumSubtitleFontSize, PlaybackSettings.MaximumSubtitleFontSize, 1,
                () => playback.SubtitleFontSize > 0 ? playback.SubtitleFontSize : SubtitlePreviewPlan.MpvDefaultFontSize,
                Live<double>(value => playback.SubtitleFontSize = PlaybackSettings.ClampFontSize((int)value)),
                "字幕文字的大小，出厂 50；mpv 自己是 38", "sub-font-size"),
            Slider("字幕缩放（%）", PlaybackSettings.MinimumSubtitleScale, PlaybackSettings.MaximumSubtitleScale, 5,
                () => playback.SubtitleScalePercent, Live<double>(value => playback.SubtitleScalePercent = (int)value),
                "在字号之上再乘一次。ASS/SSA 字幕不用「强制」也认这一项，是唯一能把它们调大的旋钮。"
                    + "出厂默认 100，就是「不缩放」；想回去就拖回 100", "sub-scale"),

            // 字幕加粗（2026-09-22「删掉字重选项，改为 sub-bold」）。mpv 没有连续字重可调，能开关的只有
            // sub-bold 这一个，所以早先那个三档字重（v18–v19）退回成一个开关：开了字体用粗体、没真粗体的由
            // libmpv 合成。选值经 Live 走，一改就把整套字幕外观重发给正在播的片子（sub-bold 在
            // SubtitleStyleOptions 里，一起被重推）。
            Toggle("字幕加粗", "开了字幕用粗体：字体有真粗体就用真的，没有的由 libmpv 合成加粗",
                () => playback.SubtitleBold, Live<bool>(value => playback.SubtitleBold = value), "sub-bold"),
            ColorRow("文字颜色", () => playback.SubtitleColor, Live<string>(value => playback.SubtitleColor = value), "sub-color",
                "HTML 颜色代码（#RRGGBB），点色块从拾色器里挑，随便什么颜色都能给"),
            UnitSlider("描边大小", SubtitlePreviewPlan.MpvDefaultBorderSize,
                () => playback.SubtitleBorderSize, Live<string>(value => playback.SubtitleBorderSize = value),
                "文字外那一圈边的宽度，拖到 0 就是没有描边。出厂 0.5，mpv 自己是 1.65", "sub-border-size"),
            ColorRow("描边颜色", () => playback.SubtitleBorderColor, Live<string>(value => playback.SubtitleBorderColor = value), "sub-border-color"),
            SubtitleShadowRow(),

            Mpv("字幕底板", MpvOutputOptions.SubtitleBackStyles, () => playback.SubtitleBackStyle,
                Live<string>(value => playback.SubtitleBackStyle = value),
                "sub-border-style", "亮画面上最管用的一项。关着的时候只有描边和阴影", RefreshSubtitleBackRows),

            Mpv("字幕编码", MpvOutputOptions.SubtitleCodepages, () => playback.SubtitleCodepage, value => playback.SubtitleCodepage = value,
                "sub-codepage", "只对不是 UTF-8 的文本字幕有意义。选了具体编码就不再自动识别了，"
                    + "所以简体那一档会把 Big5 的繁体字幕读成乱码。下次播放生效"),
            Toggle("拉伸图形字幕到画面", "画面比 16:9 更宽的电影，把图形字幕（PGS/VOBSUB）拉伸到画面里，"
                + "免得字幕落到画面外头去。下次播放生效", () => playback.StretchWideImageSubtitles, value => playback.StretchWideImageSubtitles = value,
                "stretch-image-subs-to-screen")
        };
        rows.InsertRange(rows.FindIndex(row => row.Label == "字幕底板") + 1, SubtitleBackRows());
        return new SettingSection("字幕", "字幕", "语言优先级和字幕外观。勾选语言并排序，越靠上越优先，"
            + "「其他字幕」代表任何别的语言的字幕、放在最后可兜底。"
            + "外观这一组改完立刻作用到正在播的片子；语言、显示模式和字幕编码下次播放生效。", rows);
    }

    /// <summary>
    /// 底板颜色 那一行的说明，照 <see cref="PlaybackSettings.SubtitleBackStyle"/> 的当前值写：mpv 把底板和
    /// 阴影用的是同一个颜色，所以这一行到底在给什么上色，只有上面那一行能回答。
    /// </summary>
    private static string BackColorNote(PlaybackSettings playback) =>
        playback.SubtitleBackStyle switch
        {
            "background-box" => "给包住所有文字行的背景盒上色；不设置时使用黑色，透明度由下一行控制",
            "opaque-box" => "给逐行盒的阴影上色；描边盒使用描边颜色。不设置时使用黑色",
            _ => "给文字阴影上色；字幕底板关闭时不画背景盒。不设置时使用黑色"
        };

    /// <summary>
    /// 字幕外观那一组的写入口：写完设置，再推给正在播的那部片子，再喊卡顶那条「字幕示例」重画一遍。
    /// <para>
    /// 包一层而不是给十一行各挂一个 <c>after</c>：那几个行工厂里只有一半带 <c>after</c> 这个参数（Toggle、
    /// Slider、Font 都没有），补齐参数是为一件小事改四个签名。这一层还顺手把「哪些行是外观」画在了一处 ——
    /// 语言、显示模式、字幕编码和图形字幕拉伸没有从这儿过，因为它们本来就只能下次播放生效。预览也只从
    /// 这儿跟：它照的是这张卡的外观，语言那些行动了它本来就不该动。
    /// </para>
    /// </summary>
    private Action<T> Live<T>(Action<T> write) =>
        value =>
        {
            write(value);
            _subtitlePreview?.Refresh();
            _ = PushSubtitleStyleAsync();
        };

    private async Task<bool> PushSubtitleStyleAsync()
    {
        if (_pushSubtitleStyle is null) return true;
        try
        {
            await _pushSubtitleStyle().ConfigureAwait(true);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception error)
        {
            Report("当前播放的字幕外观未完全更新", error);
            return false;
        }
    }

    private SettingSection AudioCard()
    {
        var audio = Settings.Audio;

        // 直通说明的共同尾巴。直通绕过 mpv 的混音与处理链这件事比「原样解码」四个字更要紧：开了直通还
        // 指望音量均衡、下混归一化或音量条起作用，是这一页最容易被骗的一处（mpv 手册对 audio-spdif 的
        // 警告也是同一句）。逐格式只说差别，共同代价写一遍。
        const string PassthroughTail =
            "需要功放/电视支持这一格式；直通后音量均衡、下混归一化和音量条都不再作用于这条音轨";
        var passthroughNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ac3"] = $"交给功放原样解码（杜比数字）。{PassthroughTail}",
            ["eac3"] = $"交给功放原样解码（杜比数字加）。{PassthroughTail}",
            ["dts"] = $"交给功放原样解码（DTS 核心流）。{PassthroughTail}",
            ["dts-hd"] = $"交给功放原样解码（DTS-HD MA；与「直通 DTS」同时勾选时按这一档处理）。{PassthroughTail}",
            ["truehd"] = $"交给功放原样解码（杜比 TrueHD，仅 HDMI）。{PassthroughTail}",
        };

        var passthrough = MpvOutputOptions.PassthroughCodecs
            .Select(codec => Toggle($"直通 {codec.Label}",
                passthroughNotes.GetValueOrDefault(codec.Value, "交给功放原样解码"),
                () => audio.PassthroughCodecs.Contains(codec.Value, StringComparer.OrdinalIgnoreCase),
                value =>
                {
                    if (value && !audio.PassthroughCodecs.Contains(codec.Value, StringComparer.OrdinalIgnoreCase))
                        audio.PassthroughCodecs.Add(codec.Value);
                    if (!value)
                        audio.PassthroughCodecs.RemoveAll(item => string.Equals(item, codec.Value, StringComparison.OrdinalIgnoreCase));
                },
                "audio-spdif"))
            .ToList();

        // 生效时机写在页头而不是每行重复：这一页的行只改设置并保存，没有字幕外观那种实时推送 —— 正在放
        // 的片子不因这里而变（2026-09-30 审查补的说明，免得「改了却听不出变化」被当成坏了）。
        return new SettingSection("音频输出", "音频输出", "输出设备、声道布局、响度、独占模式和功放直通。"
            + "这一页的改动保存后从下一次播放开始生效，正在放的片子不受影响；播放中的临时调整在画面菜单里，不保存。",
        [
            // 音频输出设备. Built from whatever has already been enumerated — nothing on the first frame — and
            // refilled by FillAudioDevicesAsync a moment later. Held in a field for exactly that.
            _audioDevice = DeviceRow(audio),
            Mpv("扬声器布局", MpvOutputOptions.Channels, () => audio.Channels, value => audio.Channels = value,
                "audio-channels"),

            // 「只对 AC-3 / E-AC-3 有效」 is the whole point of this note. The option itself is fine and stays —
            // it really does work on an AC-3 track that carries DRC metadata — but the row used to read as the
            // general 「让对白清楚一点」 control, and on a DTS or TrueHD track it does exactly nothing. That is
            // the 「界面在骗人」 class of defect, so the row now names its own range and points at the next one.
            Mpv("动态范围压缩", MpvOutputOptions.DynamicRange, () => audio.DynamicRange, value => audio.DynamicRange = value,
                "ad-lavc-ac3drc",
                "只对 AC-3 / E-AC-3 音轨有效，而且要片源自带 DRC 信息；DTS、TrueHD、AAC、FLAC 一律没有反应，"
                + "那些请用下面的「音量均衡」"),
            Mpv("音量均衡", MpvOutputOptions.VolumeNormalizers, () => audio.VolumeNormalize, value => audio.VolumeNormalize = value,
                "af", "对解码后的音频有效（直通除外），代价是动态范围被压窄；播放器右键菜单里可以当场试听这三档"),
            Toggle("5.1 下混到两声道时归一化",
                "多声道下混时防削波：把混音系数整体压到不超载，代价是整体变轻。它不单独抬高对白 —— "
                + "想要对白清楚请用上面的「音量均衡」。只在下混由 mpv 完成时有效，这台机器上量过确实如此",
                () => audio.NormalizeDownmix, value => audio.NormalizeDownmix = value,
                "audio-normalize-downmix"),
            Toggle("音频独占模式", "播放时把声卡占下来、绕过系统混音，声音更原样；代价是放片的时候别的程序出不了声",
                () => audio.ExclusiveMode, value => audio.ExclusiveMode = value,
                "audio-exclusive"),
            Number("全局音频延迟（毫秒）", -5000, 5000, () => audio.DelayMilliseconds, value => audio.DelayMilliseconds = value,
                "正值＝声音推迟，负值＝声音提前；清空输入框不会归零，灰字是仍在生效的值", null, "audio-delay"),
            new SettingToggleGroupRow("直通格式", passthrough)
        ]);
    }

    /// <summary>
    /// 音频输出设备. Its own factory rather than an inline <see cref="Pick"/> call because the note is the point
    /// of the row: 独占模式 without this could only ever take over 「whatever Windows calls the default right
    /// now」, and the whole reason to choose a device by hand is to say which one that is.
    /// </summary>
    private SettingChoiceRow DeviceRow(AudioSettings audio)
    {
        var (choices, selected) = DeviceChoices(audio, _audioDevices?.Known ?? []);

        return new SettingChoiceRow(
            "音频输出设备",
            Annotate("每次打开设置会重新读取内置 libmpv 的设备名单；内置后端起播前再次核对，"
                + "未找到时本次尝试系统默认，不改已保存的选择。外部 mpv 使用自己的设备表，不保证这些名称可用，也不执行此回退。"
                + "播放中拔插或设备无法打开仍可能无声", "audio-device"),
            choices,
            selected,
            Save);
    }

    /// <summary>
    /// 主页：那几排的次序和显示与否 —— 「把媒体库的列表也添加到主页之中，新增页里拖拽决定这些列表的顺序，勾选
    /// 显示或者不勾选取消显示」。
    /// <para>
    /// 表里那几项从设置文件里那份版面读（<see cref="Emby.HomeLayout"/> 归一化过的那一份，主页每次读完都写回来），
    /// 所以这一头不用问服务器有哪几个媒体库 —— 设置窗口连不上服务器时这张卡照样写得出每一排叫什么。改一下（拖过
    /// 或者点过勾）就立刻写回设置并喊一声（<see cref="ShellPrefs"/>），主页那一头照新的重排。
    /// </para>
    /// </summary>
    private SettingSection HomeCard()
    {
        var ui = Settings.Ui;
        var plan = Emby.HomeLayout.Plan(ui.HomeRows, null);

        HomeRows = new SettingHomeLayoutRow(
            "媒体库顺序",
            "按住一行往上下拖、或者按右边那两颗箭头决定次序，取消勾选就不显示。媒体库那一排是进各媒体库的入口"
                + "（点上面的卡片打开那个库），那几排装的是每个库最近添加的内容。",
            plan.Where(row => Emby.HomeLayout.IsAvailable(row.Key, _moviePilot?.Available == true))
                .Select(row => new HomeRowChoice(row.Key, row.Title, row.Visible)),
            rows =>
            {
                ui.HomeRows = Emby.HomeLayout.Save(Emby.HomeLayout.MergeEdits(
                    Emby.HomeLayout.Plan(ui.HomeRows, null),
                    rows.Select(row => new Emby.HomeRowPlan(row.Key, row.Title, row.Visible)).ToArray()));

                Save();
                ShellPrefs.Apply(ui);
            });

        // 「新增在设置中设置轮播图要使用什么媒体，和要使用最近添加还是随机的还有数量的选项。还有封面的
        // 轮换的秒数」（用户的话，2026-09-13）。四行照他点的名落：站哪一类、从哪来、几张、一张站几秒。
        // 全部当场生效（ShellPrefs）：来源、媒体和张数是主页重新取数的事，秒数是那条带换钟的事 ——
        // 改完不用回主页，也不用重启。
        (string Label, Emby.CarouselMediaType Value)[] carouselMedia =
        [
            ("全部媒体", Emby.CarouselMediaType.All),
            ("只看电影", Emby.CarouselMediaType.Movies),
            ("只看剧集", Emby.CarouselMediaType.Series)
        ];

        (string Label, Emby.CarouselSource Value)[] carouselSource =
        [
            ("最近添加", Emby.CarouselSource.Recent),
            ("随机挑选", Emby.CarouselSource.Random)
        ];

        return new SettingSection("主页", "主页", "顶上那张轮播大图、媒体库那一排摆在哪，以及主页上那几排的次序和"
            + "显示与否，包括每个媒体库自己那一排。",
        [
            // 「在设置中新增关闭轮播图的功能」（用户的话，2026-09-05）。摆在四行轮播设置和拖拽表上面，
            // 因为它管的是整个第一屏：关掉之后顶上那张大图整个没有，那张表里的每一排照旧横着排在各自的
            // 位置上。改完当场喊一声（ShellPrefs），主页那一头照新的重排 —— 和下面几行走同一条路。
            Toggle("显示主页轮播大图",
                "主页最上面那张会自己走的大图。开着的时候它是一块四边留白的大卡片，剧照铺满整张卡、下面那几排"
                    + "接在它下面（第一屏里就看得见）；关掉之后这张图整个没有，整页就是一叠横着排的普通货架。"
                    + "大图上放哪些条目，由下面四行轮播设置说了算。",
                () => ui.ShowHomeBanner,
                value =>
                {
                    ui.ShowHomeBanner = value;
                    ShellPrefs.Apply(ui);
                }),

            Choice("轮播图用什么媒体", carouselMedia,
                () => ui.CarouselMedia,
                value =>
                {
                    ui.CarouselMedia = value;
                    ShellPrefs.Apply(ui);
                },
                "顶上那张大图站哪一类。音乐类从不出现在轮播里"),

            Choice("轮播图条目来源", carouselSource,
                () => ui.CarouselSource,
                value =>
                {
                    ui.CarouselSource = value;
                    ShellPrefs.Apply(ui);
                },
                "最近添加按服务器记录的入库时间从新到旧；随机挑选拿到的是服务器随手给的一批，回到主页就可能换一批"),

            Number("轮播图张数", Emby.HomeCarousel.MinSlots, Emby.HomeCarousel.MaxSlots,
                () => ui.CarouselCount,
                value =>
                {
                    ui.CarouselCount = value;
                    ShellPrefs.Apply(ui);
                },
                $"这条带最多摆几张，{Emby.HomeCarousel.MinSlots}–{Emby.HomeCarousel.MaxSlots}。"
                    + "筛掉没有宽图的条目之后可能不足这个数"),

            Number("封面轮换秒数", Emby.HomeCarousel.MinDwellSeconds, Emby.HomeCarousel.MaxDwellSeconds,
                () => ui.CarouselSeconds,
                value =>
                {
                    ui.CarouselSeconds = value;
                    ShellPrefs.Apply(ui);
                },
                "一张封面站多少秒再换下一张，指针停在带上的时候不计时"),

            // 「主页媒体库位置」（用户 2026-10-12 令，当时叫「首页媒体库位置」、摆在界面卡上；2026-10-16 令改名
            // 并挪进主页卡）。三档的判定在 Core（<see cref="Emby.HomeFold.LibraryOnBanner"/>），主页那一头只管量
            // 几何和搬那一排。改完当场喊一声（ShellPrefs）：主页在另一个窗口里，它照这一档重新摆一次，不等回到
            // 主页、不用重启。
            Pick("主页媒体库位置",
                "媒体库那一排（进各媒体库的入口）摆在主页第一屏的哪儿。「随窗口高度变换」是装机默认：窗口矮到"
                    + "快看不见下面那几排时，它自己压到轮播封面的左下角，窗口拉高又落回轮播下面；另两档钉死，"
                    + "不看窗口高矮。轮播收起来的时候（关掉大图，或者一张幻灯片都没有）没有轮播可依，"
                    + "「固定在轮播图中」按「在下方」显示。",
                [
                    ("随窗口高度变换", Emby.HomeLibraryPlacement.Auto),
                    ("固定在轮播图中", Emby.HomeLibraryPlacement.OnCarousel),
                    ("固定在轮播图下方", Emby.HomeLibraryPlacement.BelowCarousel)
                ],
                () => ui.HomeLibraryPlacement,
                value => ui.HomeLibraryPlacement = value,
                EqualityComparer<Emby.HomeLibraryPlacement>.Default,
                after: () => ShellPrefs.Apply(ui)),

            HomeRows
        ]);
    }

    /// <summary>自检用：那张可拖拽的表这一次建出来的那一行。</summary>
    internal SettingHomeLayoutRow? HomeRows { get; private set; }

    internal void RefreshHomeRows()
    {
        if (_settings is null || HomeRows is null) return;

        // 只在这一页显示着的时候重填。MoviePilot 那两排是连接状态落定之后才进表的，那一刻用户可能正开在
        // 别的分类上：卡收着的时候 ListView 不重排，重填进来的那两排只建得出容器、套不上模板，等用户切回
        // 主页看到的就是两个空盒子（2026-10-12 实测：7 个容器里只有 5 个带模板）。收着就先不动，ShowCategory
        // 把这一页摆到前面时会再喊一次。
        if (!string.Equals(SelectedCategory, FirstCardCategory, StringComparison.Ordinal)) return;

        var wanted = Emby.HomeLayout.Plan(Settings.Ui.HomeRows, null)
            .Where(row => Emby.HomeLayout.IsAvailable(row.Key, _moviePilot?.Available == true))
            .Select(row => new HomeRowChoice(row.Key, row.Title, row.Visible))
            .ToList();

        // 名单没变就别重填。重填是一记 Reset，会把这一页上正看着的容器整批拆了重建 —— 而「切回主页」这一次
        // 调用绝大多数时候名单就是一个没变（见 ShowCategory），白拆一遍只会把切分类那点动效搅乱。
        if (HomeRows.Rows.Count == wanted.Count
            && HomeRows.Rows.Zip(wanted).All(pair => pair.First.Key == pair.Second.Key
                && pair.First.Title == pair.Second.Title
                && pair.First.Visible == pair.Second.Visible))
        {
            return;
        }

        HomeRows.Seed(wanted);
    }

    /// <summary>
    /// 自检用：「图片缓存上限（MB）」那一行。
    /// <para>
    /// 存着它是因为这件事真会坏的地方不在这一页上：设置页开在另一个窗口里，它只把数字写进设置文档然后喊一声
    /// （<see cref="ShellPrefs"/>），是主窗口那一头把新预算交给图片仓库的。那根绳子断了的样子是「三处读数全对、
    /// 缓存照旧按旧上限清」—— 屏上没有任何东西说得出现在生效的是哪个数。
    /// </para>
    /// </summary>
    internal SettingNumberRow? ImageBudget { get; private set; }

    /// <summary>自检读的：「关于」卡上图片缓存那一行此刻的文字。</summary>
    internal string ImageCacheSummary => _imageCache?.Value ?? "未读取";

    /// <summary>
    /// 自检：把这一行拨动一格，看图片仓库的预算跟不跟得上，再拨回去。
    /// <para>
    /// **只拨一格（±1 MB）**，不是拨到上限。两个理由：拨到上限对「上限本来就是上限」的人是一次空操作
    /// （<c>SettingNumberRow.Value</c> 是 <c>[ObservableProperty]</c>，相等就直接返回，<c>after</c> 一次都不跑），
    /// 这一关于是变成一句永远为真的空话；而万一还原那一步没走到，留在设置文件里的差别只有 1 MB，不是「上限没了」。
    /// </para>
    /// </summary>
    internal (bool Ok, string Detail)? MeasureImageBudget(Emby.EmbyImageStore images)
    {
        if (ImageBudget is not { } row) return null;

        var before = Settings.Ui.ImageCacheMegabytes;
        var probe = before < Emby.ImageCachePolicy.MaxMegabytes ? before + 1 : before - 1;

        var agreedBefore = images.MaxBytes == Emby.ImageCachePolicy.BudgetBytes(before);

        row.Value = probe;
        var moved = Settings.Ui.ImageCacheMegabytes == probe;
        var followed = images.MaxBytes == Emby.ImageCachePolicy.BudgetBytes(probe);

        row.Value = before;
        var restored = Settings.Ui.ImageCacheMegabytes == before
            && images.MaxBytes == Emby.ImageCachePolicy.BudgetBytes(before);

        return (agreedBefore && moved && followed && restored,
            $"设置里 {before} MB、仓库 {images.MaxBytes / 1024 / 1024} MB（一致={agreedBefore}）"
            + $"；拨到 {probe} MB → 设置{(moved ? "跟上" : "没跟上")}、仓库{(followed ? "跟上" : "没跟上")}"
            + $"；拨回 {before} MB → {(restored ? "两头都还原了" : "没还原")}"
            + $"；范围 {Emby.ImageCachePolicy.MinMegabytes}–{Emby.ImageCachePolicy.MaxMegabytes}");
    }

    /// <summary>
    /// 自检：那张表和设置文件里那一份对得上没有 —— 存着几排，表里就该有几排，钥匙和次序都一样。
    /// <para>
    /// 这一条盯的是设置窗口这一头拿不到服务器那份媒体库列表：媒体库那几排的名字只能从存档里记着的那句标题来
    /// （见 <see cref="Emby.HomeLayout.Plan"/> 里那一手）。那一手断掉的样子是屏上一张只有四行固定排的表 ——
    /// 看着完全正常，而用户在上面随手拖一下，就把媒体库那几排从设置文件里抹掉了。
    /// </para>
    /// </summary>
    internal (bool Ok, string Detail)? MeasureHomeRows()
    {
        if (HomeRows is not { } row) return null;

        var saved = Emby.HomeLayout.Plan(Settings.Ui.HomeRows, null)
            .Where(entry => Emby.HomeLayout.IsAvailable(entry.Key, _moviePilot?.Available == true)).ToArray();
        var keys = row.Rows.Select(choice => choice.Key).ToList();
        var ok = keys.Count == saved.Length
            && saved.Select(entry => entry.Key).SequenceEqual(keys, StringComparer.Ordinal);

        return (ok, $"表里 {keys.Count} 排、当前可用 {saved.Length} 排"
            + $"：{(keys.Count == 0 ? "无" : string.Join('、', row.Rows.Select(choice =>
                $"{choice.Title}{(choice.Visible ? "✓" : "✗")}")))}");
    }

    private SettingSection InterfaceCard()
    {
        var ui = Settings.Ui;
        Themes = ThemeSwatches(ui);

        var rows = new List<SettingRow>
        {
            Themes,

            Toggle("UI 动画", "控制页面切换、卡片悬停等界面动效，修改后立即生效；开启时仍遵循 Windows 动画设置",
                () => ui.AnimationsEnabled,
                value => ui.AnimationsEnabled = value,
                after: () => ShellPrefs.Apply(ui)),

            Toggle("侧边栏模式", "在主窗口左侧显示主页、搜索、收藏和媒体库导航；默认关闭，修改后立即生效",
                () => ui.SidebarMode,
                value => ui.SidebarMode = value,
                after: () => ShellPrefs.ApplySidebar(ui)),

            // 「在设置中新增开机自启的功能」。打包安装（MSIX）之后这一条办不到（见 IStartupRegistration），
            // 那时整行不显示 —— 按项目规则，当前配置下不会生效的功能整个收起，不是置灰。
        };

        if (_startup is { Available: true }) rows.Add(StartupRow());

        rows.AddRange(
        [
            // 「主页媒体库位置」原来在这一段里（2026-10-12 起，叫「首页媒体库位置」），2026-10-16 按用户令改名并挪
            // 进主页卡 —— 它和主页那张版面表（勾哪几排、什么次序）说的是同一件事：主页第一屏长什么样。

            // 「锁定窗口比例大小」原来就在这里，2026-09-05 按用户的话整条删掉了（「删除设置中锁定比例的功能」）
            // —— 浏览时的窗口从此随便拉，放片子时形状照旧跟着画面走。紧跟着那一行是「默认收起侧边栏」，
            // 2026-09-06 跟着侧边栏本身一起删掉了（「删掉侧边栏」）：媒体库后来从主页那一排卡片进，没有栏可收。
            // 那一行原来还负责「改完当场生效」这条线（ShellPrefs），而那条线还在 —— 图片缓存上限和主页版面
            // 照旧走它。

            // Both ranges match what SettingsMigration.Normalize clamps these to. They have to: a box narrower
            // than its setting shows a clamped number the file does not contain and writes it back on the next
            // touch, and a box wider than its setting lets a value be typed that the save then silently moves.
            Number("每页条目数", 20, 500, () => ui.PageSize, value => ui.PageSize = value,
                "媒体库一页列多少个，翻页按这个数走"),

            // 「海报宽度（像素）」原来在这一行下面，2026-09-05 按用户的话整行删掉了（「删掉设置中的海报宽度」）
            // —— 卡片回到 CardSize 固定的默认尺寸，AppSettings.UiSettings 里那句注释记着这件事。

            // 「评分来源」原来在这一行下面，2026-10-16 按用户的话整行删掉了（「删掉评分来源这个设置选项，服务端给
            // 什么评分就用什么」）—— 详情页那个分从此直接画服务器给的那个数（Emby.ItemScore），不再有下拉可选。

            // 「显示观看状态标记」原来在这一行下面，2026-10-16 按用户的话整行删掉了（「删掉这个开关，改为默认
            // 开启」）—— 海报角上的已看、收藏和未看集数从此一直画，`AppSettings.UiSettings` 里那句注释记着这件事。

            // 「在设置中新增一个精简模式，开启后隐藏各项功能下方的说明」（用户的话，2026-09-25）。收放是页级
            // 的事（SettingRow.NotesHidden），所以 after 里先对齐标记、再逐行喊一遍 —— 与 ShowHomeBanner 不同，
            // 这件事不出设置页，不走 ShellPrefs。开关行自己的说明也在「各项功能」之列：开着它，连介绍它自己的
            // 那行小字一起收走 —— 趁它还显示着，把这句话读完再拨。
            // 屏上的名字改成新话：「把极简模式改名为隐藏功能下方说明」（用户的话，2026-09-26）—— 名字
            // 直接说它干什么；键名 CompactMode、自检里的 ProbeCompactMode 不动，改的只是给人看的字。
            Toggle("隐藏功能下方说明", "开启后，这一页每一条设置下面的小字说明都收起来，只留标签和控件；想看某条是"
                + "什么意思，把它关掉就放回来",
                () => ui.CompactMode,
                value => ui.CompactMode = value,
                after: () =>
                {
                    SettingRow.NotesHidden = ui.CompactMode;
                    RefreshNoteVisibility();
                })
        ]);

        return new SettingSection("界面", "界面",
            "配色主题、UI 动画、侧边栏模式、开机自启、隐藏功能下方说明和媒体库分页。",
            rows);
    }

    /// <summary>
    /// 「在设置中新增开机自启的功能」。这一行和这一页别的行都不一样：它写进去的不是设置文件，而是机器上的启动项
    /// （HKCU 里 <see cref="StartupEntry"/> 说的那两格），所以它没有存档值 —— 读的是注册表当下的事实，拨一下当场
    /// 写下去。
    /// <para>
    /// <b>不另存一份偏好。</b>注册表那两格就是事实，另存一份迟早会和它对不上；而用户去任务管理器「启动」页看时
    /// 看到的是那两格，那时屏上这一行要是还说「开着」，就是这一页最怕的「界面在骗人」。代价写在
    /// <see cref="SettingRow"/> 那一头：这一项不进备份文件，也不跟着「恢复默认」走 —— 那两件事动的都是设置文件里
    /// 的偏好，而这是机器上的登记。
    /// </para>
    /// <para>
    /// 写不成（组策略挡住、注册表只读、安全软件拦下）时把开关拨回去并说一句：不拨回去的话，开关停在用户拨的
    /// 那一档、机器上什么都没变，而这一页没有任何别的读数会响。
    /// </para>
    /// </summary>
    private SettingToggleRow StartupRow()
    {
        SettingToggleRow? row = null;

        row = Toggle("开机自启",
            "登录 Windows 之后自动打开 Momoka；关掉就从系统的启动项里撤掉。改完当场生效，不用重启。"
                + "这一项不进设置文件，任务管理器「启动」页上看到的就是它。",
            () => _startup?.IsEnabled ?? false,
            value => _startup?.Apply(value),
            after: () =>
            {
                if (row is null || _startup is null) return;

                var actual = _startup.IsEnabled;
                if (actual == row.IsOn) return;

                row.Reseed(actual);
                Notify(null, actual
                    ? "没能关掉开机自启：系统拒绝了这次改动，本程序仍会在登录后启动。"
                    : "没能打开开机自启：系统拒绝了这次改动，本程序不会在登录后启动。",
                    InfoBarSeverity.Error);
            });

        return row;
    }

    /// <summary>
    /// 精简模式拨了一下：整页每一行的说明可见性都变了，逐行把消息喊过去。The page deliberately has no refresh
    /// pass — this is not one: no value is recomputed, each row is only told that a property it already exposes
    /// moved, which is exactly the message the Note setter sends when 视频同步's row restates itself. Rows in
    /// cards that have never been opened have no containers yet, so there is nothing to tell — their bindings
    /// read the flag when they are first built.
    /// </summary>
    private void RefreshNoteVisibility()
    {
        foreach (var section in Sections)
            foreach (var row in section.Rows)
                row.RefreshNoteVisibility();
    }

    /// <summary>
    /// 快捷键：播放器那些键盘动作，做成参考图那样一行一个、右边一个可重绑的方框（「参考上图在设置中新增快捷键
    /// 功能」，2026-09-08）。21 行可改的走 <see cref="SettingShortcutRow"/>；Esc、回车两个固定键以只读行显示，
    /// 让人看到全貌（为什么固定见 <see cref="ShortcutCatalog.ReservedKeys"/>）；末一行一颗「恢复默认快捷键」，只清
    /// 快捷键、不动别的。判断全在 Core 的 <see cref="ShortcutCatalog"/>；这里只把动作翻成行、把方框敲定的键交回去。
    /// </summary>
    private SettingSection ShortcutsCard()
    {
        // 每次建卡都重来一份：ReloadAsync 会被再调（恢复默认那一趟就重建整页），握着的这份行要跟着换新。
        _shortcutRows.Clear();

        var effective = ShortcutCatalog.Resolve(Settings.Shortcuts.Bindings);
        var rows = new List<SettingRow>();

        foreach (var action in ShortcutCatalog.Actions)
        {
            var id = action.Id;
            var row = new SettingShortcutRow(action.Label, id, ShortcutCatalog.Format(effective[id]),
                token => ApplyShortcut(id, token));
            _shortcutRows.Add(row);
            rows.Add(row);
        }

        // 两个固定键，只读显示，让人看到全貌（为什么固定见 ShortcutCatalog.ReservedKeys）。
        // 2026-09-26（用户令）：确认跳过 Y→回车、关闭跳过提示 N→Esc —— Esc 因此先兼「关闭提示」那一档。
        rows.Add(Fact("退出全屏 / 停止", "固定，不可更改；跳过提示立着时先关闭提示", "Esc"));
        rows.Add(Fact("确认跳过片头 / 片尾", "固定，不可更改；只在出现跳过提示时有效", "回车"));

        // 只清快捷键、不动别的（列表最下面那颗「恢复默认」清的是全部设置，作用域比这颗宽）。
        rows.Add(Fact("恢复默认快捷键", "把上面这些快捷键改回装机时的默认，其他设置不受影响",
            $"{ShortcutCatalog.Actions.Count} 个可改快捷键", "恢复默认", ClearAllShortcuts));

        return new SettingSection("快捷键", "快捷键",
            "播放时的键盘快捷键。点一下右边的方框，再按你想要的组合键（可带 Ctrl / Alt / Shift）就改绑了，× 清除。"
            + "这些只在播放窗口里生效。", rows);
    }

    /// <summary>
    /// 一次重绑（方框敲定了一个键 token，空串=按了 ×）。判断在 <see cref="ShortcutCatalog"/>：冲突就拦下、提示，
    /// 一个绑定都不改（那一行方框还显示着原来的，因为 <c>ComboText</c> 没动）；否则写回、落盘、逐行刷新显示。
    /// 写回一份新字典即可，播放器那头每次按键现读同一个单例设置，所以已经开着的播放器也当场跟上。
    /// </summary>
    private void ApplyShortcut(string id, string token)
    {
        var bindings = Settings.Shortcuts.Bindings;

        if (token.Length == 0)
        {
            Settings.Shortcuts.Bindings = new Dictionary<string, string>(ShortcutCatalog.Clear(bindings, id), StringComparer.Ordinal);
        }
        else
        {
            if (!ShortcutCatalog.TryParse(token, out var stroke)) return;

            var result = ShortcutCatalog.Rebind(bindings, id, stroke);
            if (result.Conflict is { } occupant)
            {
                Notify("快捷键冲突",
                    $"「{ShortcutCatalog.Format(stroke)}」已经是「{ShortcutCatalog.Label(occupant)}」的快捷键了。先把那边清掉，再绑到这里。",
                    InfoBarSeverity.Warning);
                return;
            }

            Settings.Shortcuts.Bindings = new Dictionary<string, string>(result.Bindings, StringComparer.Ordinal);
        }

        Save();
        RefreshShortcutRows();
    }

    /// <summary>「恢复默认快捷键」：改动全清掉（Bindings 变回空＝全默认），落盘、刷新、说一声。</summary>
    private void ClearAllShortcuts()
    {
        Settings.Shortcuts.Bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        Save();
        RefreshShortcutRows();
        Notify(null, HasUnsavedChanges ? "快捷键已在本次运行恢复默认，但尚未保存。" : "播放器快捷键已恢复默认。其他设置没有动。",
            HasUnsavedChanges ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
    }

    /// <summary>照当前绑定把每一行的显示串重算一遍（重绑、清一个、清全部之后都走它）。</summary>
    private void RefreshShortcutRows()
    {
        var effective = ShortcutCatalog.Resolve(Settings.Shortcuts.Bindings);
        foreach (var row in _shortcutRows)
            row.ComboText = ShortcutCatalog.Format(effective[row.Id]);
        ShellPrefs.ApplyShortcuts();
    }

    /// <summary>
    /// MoviePilot 接入卡（用户的话，2026-09-14：「放设置页」）。一个开关、服务地址、用户名、密码、一颗测试按钮。
    /// <para>
    /// <b>为什么先做这张卡</b>：它是后面所有 MoviePilot 功能的地基，而且它自己是能独立验完的一步 —— 填上地址按下
    /// 测试，通没通当场看得见。缺集提醒、搜索订阅那几件都要先有「这台 MoviePilot 说什么话、连的是哪个 Emby」这些
    /// 事实，而那些事实正是这张卡测出来的。
    /// </para>
    /// <para>
    /// <b>密码为什么存得下来</b>：MoviePilot 的 API_TOKEN 那条路在这台服务器上是断的 —— 认证能过，但每个接口都
    /// 回「SUPERUSER 对应用户不存在、未启用或非超级管理员」（实测 2026-09-14）。账号密码换 JWT 那条路是通的，
    /// 所以这张卡收用户名密码，用 DPAPI 包起来存（<see cref="MoviePilotCredentials"/>），和 Emby 的密码同一套。
    /// 代价是它比一个长期 token 危险一点，这也是为什么它默认关着、密码框永远不回填。
    /// </para>
    /// <para>
    /// 输入框改了就地写盘；已启用时自动重新检查连接，测试按钮也可在关闭时验证配置。
    /// </para>
    /// </summary>
    private SettingSection MoviePilotCard()
    {
        var moviePilot = Settings.MoviePilot;

        // 行本身只拿读写对，不碰设置对象（同这一页每一条的规矩）。密码那一对是个例外里的例外：写进去的是明文、
        // 存下来的是密文，所以包一层 —— 行永远只见到明文，包和拆都在 MoviePilotCredentials 里。
        _moviePilotRow = new SettingMoviePilotRow(
            "MoviePilot",
            "接上你自己的 MoviePilot，之后就能在库里查缺集、直接订阅还搜得到资源的那一部。"
                + "协议选 http 或 https，地址填 MoviePilot 的主机，端口填它的 API 端口（默认 3001），"
                + "不是网页界面那个端口。",
            moviePilot.Enabled,
            moviePilot.Url,
            moviePilot.Username,
            moviePilot.HasSavedPassword,
            value => moviePilot.Enabled = value,
            value => moviePilot.Url = value,
            value => moviePilot.Username = value,
            value =>
            {
                // 空串＝把已存的清掉，不是「留空不动」—— 「留空则用已保存的那个」承诺在按钮那一边（测试时用
                // 存下来的），而一个用户主动清空的框该真的清掉。
                _moviePilotCredentials?.SetPassword(moviePilot, value);
                _moviePilotRow?.MarkPasswordSaved(moviePilot.HasSavedPassword);
            },
            Save,
            TestMoviePilotAsync);

        return new SettingSection("MoviePilot", "MoviePilot",
            "这是一个可选的服务：接上之后，这个程序能替你去 MoviePilot 那边找片、订阅、查缺集。不接也能正常用。",
            [_moviePilotRow]);
    }

    /// <summary>测试已保存的当前配置，共享连接状态；只记录测试时间，不替用户打开功能开关。</summary>
    private async Task<string> TestMoviePilotAsync(CancellationToken cancellationToken)
    {
        if (_moviePilot is null) throw new InvalidOperationException("设置页还没准备好，稍后再试");

        var moviePilot = Settings.MoviePilot;
        var status = await _moviePilot.TestConnectionAsync(cancellationToken).ConfigureAwait(true);
        moviePilot.LastUserName = moviePilot.Username.Trim();
        moviePilot.LastConnected = DateTimeOffset.Now;

        Save();

        var servers = status.MediaServers.Count > 0 ? string.Join("、", status.MediaServers) : "无";
        var downloaders = status.Downloaders.Count > 0 ? string.Join("、", status.Downloaders) : "无";

        return $"连接成功：MoviePilot {status.Version}"
            + (status.OperatingSystem.Length > 0 ? $"（{status.OperatingSystem}）" : "")
            + $"\n已连的媒体服务器：{servers}"
            + $"\n已连的下载器：{downloaders}";
    }

    // ── 截图保存目录 ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 截图往哪儿落 —— <see cref="AppPaths.ResolveScreenshotDirectory"/> 的那一句答案：设置里填了的（清掉
    /// 引号空白、展开成绝对）就是它，空着就是装机落点。没有 <see cref="_paths"/>（测试路径）时兜底也是空，
    /// 调用方各有空串的走法。
    /// </summary>
    private string ScreenshotDirectory() =>
        AppPaths.ResolveScreenshotDirectory(Settings.Mpv.ScreenshotDirectory, _paths?.ScreenshotDirectory) ?? "";

    /// <summary>
    /// 截图保存目录动过了，把两处「印着它」的地方当场带上：关于卡那行读数（不重开设置窗口，屏上说的也得是
    /// 新目录），和正播着的那一部 —— mpv 的 <c>screenshot-directory</c> 属性，下一张截图起就落进新目录，
    /// 画面菜单副标题里展开的也是它。推送没接上（测试路径）就略过；推送本身没有可败之处（<c>PlaybackService</c>
    /// 里裹着 try），同 <see cref="_pushSubtitleStyle"/> 的调法一样不另设防。
    /// </summary>
    private void AnnounceScreenshotDirectory()
    {
        var directory = ScreenshotDirectory();
        if (_screenshotFact is { } fact) fact.Value = directory;
        if (directory.Length > 0) _ = _pushScreenshotDirectory?.Invoke(directory);
    }

    /// <summary>
    /// 关于卡四个目录共用这一条打开入口。建目录也归 launcher，只调用一次；磁盘或启动失败由页面提示。
    /// 放进 Task 承接按钮动作，目录访问不阻塞 UI，失败回来仍在 UI 上更新通知。
    /// </summary>
    private async Task OpenDirectoryAsync(string directory)
    {
        if (_launcher is not { } launcher || string.IsNullOrWhiteSpace(directory)) return;

        try
        {
            await Task.Run(() => launcher.OpenFolder(directory)).ConfigureAwait(true);
        }
        catch (Exception error) { Report("打开目录失败", error); }
    }

    // ── 检查新版本 ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 「关于」卡上那颗「检查新版本」：现在就去发布页问一句，结果写回那一行读数；真有新版本再问一句要不要打开
    /// 下载页。和启动时那一趟（<c>ShellPage.CheckForUpdatesAsync</c>）共用 Core 的客户端与措辞，只有「要不要说
    /// 失败」这一点不同 —— 那一趟是程序自己动的，失败只写日志；这一趟是用户按的，失败必须说一句，否则一颗按下去
    /// 什么都不发生的按钮，看起来就是坏的。
    /// <para>
    /// 结果先写「正在检查…」：这一趟有网络、要几秒，不写的话那一行从头到尾一个字都不变，用户不知道按没按上。
    /// </para>
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        if (_updates is null || _updateRow is null || _checkingUpdates) return;

        var row = _updateRow;
        _checkingUpdates = true;
        row.Value = UpdateNotice.Checking;

        try
        {
            var current = new Version(AppIdentity.Version);
            var release = await _updates.FindNewerAsync(current, CancellationToken.None).ConfigureAwait(true);

            // 这一趟在等网络的时候页面可能被整页重建（恢复默认／恢复配置都重建），那就别往旧的那一行上写字 ——
            // 它已经不在树上了。
            if (!ReferenceEquals(row, _updateRow)) return;

            if (release is null)
            {
                row.Value = UpdateNotice.UpToDate(current);
                return;
            }

            row.Value = UpdateNotice.Found(release);
            if (await ConfirmAsync(UpdateNotice.Title, UpdateNotice.Prompt(release, current), UpdateNotice.OpenLabel)
                .ConfigureAwait(true))
                OpenReleasePage(release);
        }
        catch (Exception error)
        {
            if (!ReferenceEquals(row, _updateRow)) return;
            row.Value = UpdateNotice.Failed;
            Report("检查更新失败", error);
        }
        finally
        {
            _checkingUpdates = false;
        }
    }

    /// <summary>
    /// 把某一版的发布页交给 Windows 打开。<see cref="Platform.ISystemLauncher.OpenUrl"/> 打不开是抛出来的，
    /// 所以这里兜一句 —— 用户按了「打开下载页」、浏览器没起来、屏上又什么都不说，下一步多半是再按一遍。
    /// </summary>
    private void OpenReleasePage(AppRelease release)
    {
        try
        {
            _launcher?.OpenUrl(release.PageUrl);
        }
        catch (Exception error)
        {
            Report("打不开下载页", error);
        }
    }

    /// <summary>
    /// 关于：这份程序是哪一版、拿哪个内核在放、它的东西放在磁盘上哪儿。
    /// <para>
    /// 在这张卡之前，版本号只写进日志和自检报告 —— 界面上一次都没出现过，所以「你用的是哪一版」这句话答不上来；
    /// 而设置文件和缓存目录也没有一处能一键打开（诊断页那颗按钮只开日志）。三行读数由 Core 那边算
    /// （<see cref="AboutFacts"/>，读不到就明说读不到），四行目录各带一颗按钮。
    /// </para>
    /// <para>
    /// 没有 <see cref="AppPaths"/> 或者打不开资源管理器的时候（测试和自检的那一路）这张卡照旧建出来，只是那四颗
    /// 按钮不画：一张空卡比一张会抛的卡好，而「这一版是哪一版」不该因为拿不到路径就说不出来。
    /// </para>
    /// </summary>
    private SettingSection AboutCard()
    {
        var home = AppContext.BaseDirectory;
        var rows = new List<SettingRow>
        {
            // 三行读数不带说明行（他 2026-09-14：「不要显示这些字」）。原来那三句 ——「改动记在 PROGRESS.md 里，
            // 版本号不随每次改动走」「这份 exe 落到磁盘上的时间」「这个文件的版本不要换」—— 是写给做这个程序的
            // 人看的：它们讲的是版本号怎么走、构建时间取的是哪个时间、那个 dll 的版本为什么别动，不是读这张卡
            // 的人关心的事。规矩没丢，它们本来就在别处（版本号在 Directory.Build.props 与 PROGRESS.md，
            // 构建时间的取法在 AboutFacts.BuiltAt 的注释里）。
            // 底下四行目录的说明留着：那几句说的是「点开这颗按钮会看到什么」，本来就是给用它的人读的。
            Fact("客户端版本", null, AboutFacts.Client),
            Fact("构建时间", null, AboutFacts.BuiltAt(Path.Combine(home, "Momoka.exe"))),
            Fact("播放内核", null, AboutFacts.PlaybackCore(home)),

            // 「在设置中新增开启应用后自动检查有没有新版本的功能」。开关和下面那颗手动按钮是同一件事的两半：
            // 开关管「以后每次启动留意一下」，手动管「现在就知道结果」。装机默认开（用户令 2026-10-14），
            // 关掉就不再联网检查 —— 这一格是那件事唯一的开关。拨一下下一次启动生效，不在这里顺手查一次。
            Toggle(UpdateNotice.ToggleLabel,
                "打开之后，每次启动会去发布页问一句有没有比这一版新的，有新版本就弹一条提示，带一颗「打开下载页」；"
                    + "关掉就不再联网检查。默认开，拨一下下一次启动生效。",
                () => Settings.Ui.AutoCheckUpdates,
                value => Settings.Ui.AutoCheckUpdates = value)
        };

        // 手动那一颗：和开关无关，用户按的，开关关着也照样查。行握着（_updateRow），按完当场改它那一行读数。
        // 单列一句 Add 而不是写进上面的初始化器：那里出现一个赋值，整个大括号会被当成对象初始化器。
        _updateRow = Fact(UpdateNotice.RowLabel,
            "现在就去发布页问一句有没有比这一版新的，结果写在这一行；有新版本会再弹一条提示，带一颗「打开下载页」。"
                + "这一颗不受上面那个开关管 —— 它是你按的。",
            "", "检查更新", () => _ = CheckForUpdatesAsync());
        rows.Add(_updateRow);

        if (_paths is { } paths)
        {
            rows.Add(Fact("设置文件", "所有设置都在这一份 JSON 里，token 是 DPAPI 包过的", paths.SettingsFile,
                "打开所在文件夹", () => _ = OpenDirectoryAsync(paths.Root)));
            rows.Add(Fact("日志目录", "自检报告和界面树也在这儿", paths.LogDirectory,
                "打开", () => _ = OpenDirectoryAsync(paths.LogDirectory)));
            rows.Add(Fact("缓存目录", "海报和着色器缓存，删掉不会丢设置", Path.GetDirectoryName(paths.ImageCacheDirectory) ?? paths.Root,
                "打开", () => _ = OpenDirectoryAsync(Path.GetDirectoryName(paths.ImageCacheDirectory) ?? paths.Root)));

            // 播放器右键菜单 → 截图 的落点。这一行不是装饰：截图这个功能从前根本没做，理由正是
            // 「--no-config 之下没有 screenshot-directory，文件会落到 exe 旁边而不告诉用户」——
            // 所以「告诉用户落在哪儿」和截图本身是同一件事的两半。2026-09-29 起这个落点跟着设置走
            // （「截图保存目录」那一行，2026-10-11 起住播放行为分类的截图分区卡），这一行握在
            // _screenshotFact 上，那头一改它当场换。
            _screenshotFact = Fact("截图目录", "播放器右键菜单 → 截图 存到这儿，文件名是片名、播放时间和序号",
                ScreenshotDirectory(), "打开", () => _ = OpenDirectoryAsync(ScreenshotDirectory()));
            rows.Add(_screenshotFact);
        }

        // 图片缓存 2026-10-20 从「服务器」页与「界面」卡搬进关于：它是这台机器上的痕迹，不是某台服务器的属性。
        // 读数握在行上，清空之后当场重数；上限行同样握着，自检靠它验证设置文档与图片仓库之间那根绳子。
        _imageCache = Fact("图片缓存",
            "海报和剧照已经下载到本机的部分；清除后下次浏览会重新下载，不影响服务器、账号和任何设置",
            "正在统计…", "清除", () => _ = ClearImageCacheAsync());
        rows.Add(_imageCache);

        ImageBudget = Number("图片缓存上限（MB）",
            Emby.ImageCachePolicy.MinMegabytes,
            Emby.ImageCachePolicy.MaxMegabytes,
            () => Settings.Ui.ImageCacheMegabytes,
            value => Settings.Ui.ImageCacheMegabytes = value,
            $"海报和剧照在磁盘上最多占多少，装机是 {Emby.ImageCachePolicy.DefaultMegabytes} MB。这一行是填进去的，"
                + "不是用箭头拨的；填小了当场就会把最久没看过的那些删到新上限以下。缓存删掉不影响任何设置，"
                + "只是下次看到那些封面时要重新下载一遍。",
            after: () => ShellPrefs.Apply(Settings.Ui));
        rows.Add(ImageBudget);

        // 「把恢复默认设置移动到关于中，在关于中新增配置文件备份和恢复配置的功能」（用户令 2026-09-24）。三行都是
        // 动作行：只有标签、说明和一颗按钮，没有值（Value 空 → SettingFactRow.ValueVisibility 收起那一行）。三件事
        // 都只碰「偏好设置」那一摊，判据在 Core 的 SettingsPreferences —— 服务器、账号、登录状态和窗口位置一个字
        // 不动。破坏性的「恢复默认设置」摆在最末，是从上读到下的人最后才遇到的东西（同它从前在左边名单末尾那会儿）。
        // 备份 / 恢复配置要弹文件框，文件框由页面交进来（UseFilePickers）；没有页面（测试路径）时那两颗按下去
        // 什么都不发生，恢复默认仍能走（它只用确认框，不用文件框）。
        rows.Add(Fact("备份配置文件",
            "把你的偏好设置（播放、字幕、画质、音频、快捷键、主题、界面等）导出成一个文件；不含服务器、账号和登录信息，"
                + "可以带到别的机器或重装之后用来恢复", "",
            "备份到文件…", () => _ = BackupAsync()));
        rows.Add(Fact("恢复配置",
            "从之前备份的文件里读回偏好设置，覆盖当前这些设置。服务器、账号和登录不受影响；这一步不能撤销", "",
            "从文件恢复…", () => _ = RestoreFromFileAsync()));
        rows.Add(Fact("恢复默认设置",
            "把设置页管的每一项改回装机时的样子。服务器、账号、登录状态和窗口位置都不动；这一步不能撤销", "",
            "恢复默认", () => _ = RestoreDefaultsAsync()));

        return new SettingSection("关于", "关于",
            "版本、检查新版本、播放内核，这个程序在磁盘上的几个位置，图片缓存，以及配置的备份、恢复和恢复默认。", rows);
    }

    /// <summary>
    /// 数一遍图片缓存，把读数写进「关于」卡的「图片缓存」行。非阻塞：缓存目录的大小要扫一遍磁盘，不值得
    /// 挡住整页；扫完之前那一行写「正在统计…」。
    /// </summary>
    private async Task MeasureImageCacheAsync()
    {
        if (_images is not { } images || _imageCache is not { } row) return;

        var usage = await images.MeasureAsync().ConfigureAwait(true);
        if (!ReferenceEquals(row, _imageCache)) return;
        row.Value = DescribeImageCache(usage, images.MaxBytes);
    }

    /// <summary>
    /// 「关于」卡上那颗「清除」：删掉已下载的海报与缩略图。问一次 —— 删掉之后下次浏览要重新下载，网络慢的
    /// 服务器上这件事看得见。已解码的那些也要清，否则屏幕上正在显示的封面会继续从内存里画出来，而提示说已经清了。
    /// </summary>
    private async Task ClearImageCacheAsync()
    {
        if (_images is not { } images || _imageCache is not { } row) return;

        var confirmed = await ConfirmAsync(
            "清除图片缓存",
            $"将删除已下载的海报与缩略图（{row.Value}）。下次浏览会重新下载，不影响服务器、账号和任何设置。",
            "清除").ConfigureAwait(true);
        if (!confirmed) return;

        row.Value = "正在清除…";
        try
        {
            var removed = await images.ClearAsync().ConfigureAwait(true);
            PosterCache.Clear();
            Notify("已清除图片缓存", $"删除了 {removed:N0} 个文件。", InfoBarSeverity.Success);
        }
        catch (Exception error)
        {
            Report("清除图片缓存失败", error);
        }
        finally
        {
            await MeasureImageCacheAsync().ConfigureAwait(true);
        }
    }

    /// <summary>「上限」跟着读数走：1.1 GB 是多是少，要旁边那个预算才说得清。</summary>
    private static string DescribeImageCache(ImageCacheUsage usage, long max)
    {
        var limit = $"上限 {TimeFormat.FileSize(max)}";
        return usage.Files == 0
            ? $"暂无缓存 · {limit}"
            : $"{TimeFormat.FileSize(usage.Bytes)} · {usage.Files:N0} 个文件 · {limit}";
    }

    /// <summary>
    /// 「恢复默认设置」确认对话框那两句话。提出来是因为自检要盯「按下之前把『服务器和账号不动』讲清楚」的那句话
    /// 还在不在 —— 「关于」卡上那颗「恢复默认」按钮没有 ToolTip（Fact 行也不给），对话框成了按下之前唯一把这件事
    /// 讲全的地方，所以自检改盯这两个常量（见 ShellSelfCheck.Settings）。
    /// </summary>
    internal const string ResetDialogTitle = "恢复默认设置";

    /// <summary>同 <see cref="ResetDialogTitle"/>；「不会退出登录」四个字是自检认它的记号，别改掉。</summary>
    internal const string ResetDialogMessage =
        "所有设置都会改回装机时的样子，这一步不能撤销。\n\n"
        + "服务器和账号不会动（不会退出登录），窗口位置和大小、各媒体库的排序筛选视图、播放器音量也都保留。";

    /// <summary>
    /// 「恢复配置」确认对话框那两句话。和恢复默认同一副骨架，把「服务器、账号、登录不动」讲清楚 —— 这一步同样
    /// 不能撤销，覆盖的是当前那一摊偏好。
    /// </summary>
    internal const string RestoreDialogTitle = "恢复配置";

    /// <summary>同 <see cref="RestoreDialogTitle"/>。</summary>
    internal const string RestoreDialogMessage =
        "会用备份文件里的偏好设置覆盖当前的播放、字幕、画质、音频、快捷键、主题、界面等设置，这一步不能撤销。\n\n"
        + "服务器和账号不会动（不会退出登录），窗口位置和大小、各媒体库的排序筛选视图、播放器音量也都保留。";

    /// <summary>
    /// 「恢复默认设置」被按下之后的那一路（按钮住在「关于」卡上，见 <see cref="AboutCard"/>）。哪些回默认、哪些
    /// 不动由 Core 那一头判（<see cref="SettingsReset.Restore"/> → <see cref="SettingsPreferences"/>，单测钉着），
    /// 这里剩下的是「问一次」和「改完让屏上跟上」。<see cref="_restoring"/> 挡掉对话框还没合上时的第二次点击。
    /// 善后（重刷主题、喊 ShellPrefs、整页重建）和恢复配置共用 <see cref="ReapplyAndReloadAsync"/>，那一段说清了
    /// 三件为什么一件都不能少。
    /// </summary>
    private async Task RestoreDefaultsAsync()
    {
        if (_settings is null || _restoring) return;
        _restoring = true;
        try
        {
            var agreed = await ConfirmAsync(ResetDialogTitle, ResetDialogMessage, "恢复默认").ConfigureAwait(true);
            if (!agreed) return;

            SettingsReset.Restore(_settings.Settings);
            Save();

            // 顺带把「没动的是哪些」再讲一遍 —— 本来就都在默认值上的人按一下屏上什么都不变，一颗看起来没反应的
            // 按钮，下一步就是再按一遍。
            await ReapplyAndReloadAsync("设置已改回装机时的样子。服务器、账号、窗口位置和各媒体库的排序筛选都没有动。")
                .ConfigureAwait(true);
        }
        catch (Exception error)
        {
            Report("恢复默认设置失败", error);
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <summary>
    /// 「备份配置文件」：把偏好设置导出成用户选的一个文件。只含偏好，不含服务器、账号、令牌和窗口位置（判据在
    /// <see cref="SettingsPreferences"/>），所以这个文件可以安全带到别的机器。不问确认 —— 它不动现有的任何东西。
    /// 文件框由页面交进来（<see cref="UseFilePickers"/>）；没接上（没有页面的测试路径）就直接返回，不假装成功。
    /// </summary>
    private async Task BackupAsync()
    {
        if (_settings is null || _saveFile is null) return;

        var path = await _saveFile.Invoke($"Momoka-偏好设置-{DateTime.Now:yyyyMMdd-HHmmss}.json").ConfigureAwait(true);
        if (path is null) return;

        try
        {
            _settings.ExportPreferences(path);
            Notify(null, $"偏好设置已备份到：{path}", InfoBarSeverity.Success);
        }
        catch (Exception error)
        {
            Report("备份配置文件失败", error);
        }
    }

    /// <summary>
    /// 「恢复配置」：从备份文件读回偏好设置，盖掉当前这些偏好。服务器、账号、登录状态和窗口位置不受影响（同恢复
    /// 默认，判据在 <see cref="SettingsPreferences"/>）。不能撤销，所以<b>先读、确认是个有效备份、再问</b> —— 免得
    /// 用户点了「恢复」才发现挑错了文件。认不出来的文件在 <see cref="ISettingsService.ReadBackup"/> 里就抛了。
    /// 确认之后走的善后和恢复默认同一段（<see cref="ReapplyAndReloadAsync"/>）。
    /// </summary>
    private async Task RestoreFromFileAsync()
    {
        if (_settings is null || _openFile is null || _restoring) return;
        _restoring = true;
        try
        {
            var path = await _openFile.Invoke().ConfigureAwait(true);
            if (path is null) return;

            AppSettings loaded;
            try
            {
                loaded = _settings.ReadBackup(path);
            }
            catch (Exception error)
            {
                Notify("这个文件不是有效的配置备份", Failure.Describe(error), InfoBarSeverity.Warning);
                return;
            }

            var agreed = await ConfirmAsync(RestoreDialogTitle, RestoreDialogMessage, "恢复").ConfigureAwait(true);
            if (!agreed) return;

            SettingsPreferences.Apply(_settings.Settings, loaded);
            Save();
            await ReapplyAndReloadAsync("已从备份恢复偏好设置。服务器、账号、登录状态和窗口位置都没有动。")
                .ConfigureAwait(true);
        }
        catch (Exception error)
        {
            Report("恢复配置失败", error);
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <summary>
    /// 恢复默认 / 恢复配置 改完设置文档之后的善后（落盘那一步在各自调用方，因为两条各 Save 一次）。三件一件都
    /// 不能少，而少了哪一件屏上都只是「设置了但没用」：主题当场重刷（不然颜色要等下次启动才变），
    /// <see cref="ShellPrefs"/> 喊一声（侧边栏、图片缓存上限、主页版面改完当场生效的唯一一根线 —— 设置页开在
    /// 另一个窗口，手上没有主窗口的 HWND、也没有主页那一页），整页重建（每一行只在造出来时读一次设置、此后
    /// 只写，见类注释，不重建的话文件已经变了、屏上六十行还是旧的）。重建走 <see cref="ReloadAsync"/> 本身，
    /// 字体名单按进程缓存；音频设备每次重建重新枚举，旧名单不能当成设备仍在线的依据。
    /// </summary>
    internal async Task<bool> ProbeSubtitleRestoreAsync()
    {
        if (App.Instance?.SubtitleProbeActive != true || _settings is null || _paths is null) return false;
        var push = _pushSubtitleStyle;
        var count = 0;
        _pushSubtitleStyle = async () => { count++; if (push is not null) await push(); };
        UseConfirm((_, _, _) => Task.FromResult(true));
        try
        {
            Settings.Playback.SubtitleFontSize = 72;
            await RestoreDefaultsAsync();
            var reset = count == 1 && Settings.Playback.SubtitleFontSize == new PlaybackSettings().SubtitleFontSize;
            var backup = Path.Combine(_paths.Root, "subtitle-probe-backup.json");
            Settings.Playback.SubtitleFontSize = 64;
            _settings.ExportPreferences(backup);
            Settings.Playback.SubtitleFontSize = 80;
            _openFile = () => Task.FromResult<string?>(backup);
            await RestoreFromFileAsync();
            return reset && count == 2 && Settings.Playback.SubtitleFontSize == 64 && !HasUnsavedChanges;
        }
        finally { _pushSubtitleStyle = push; }
    }

    internal async Task<bool> ProbeSubtitlePushFailureAsync()
    {
        if (App.Instance?.SubtitleProbeActive != true) return false;
        var push = _pushSubtitleStyle;
        _pushSubtitleStyle = () => Task.FromException(new InvalidOperationException("离线模拟：字幕选项读数失败"));
        try
        {
            var applied = await PushSubtitleStyleAsync();
            return !applied && NoticeOpen && NoticeTitle == "当前播放的字幕外观未完全更新"
                && NoticeMessage?.Contains("字幕选项读数失败", StringComparison.Ordinal) == true;
        }
        finally { _pushSubtitleStyle = push; }
    }

    private async Task ReapplyAndReloadAsync(string notice)
    {
        var ui = Settings.Ui;
        ThemeHost.Apply(ui.Theme);
        ShellPrefs.Apply(ui);
        ShellPrefs.ApplyShortcuts();

        await ReloadAsync().ConfigureAwait(true);
        await ReapplyPlaybackPreferencesAsync(notice).ConfigureAwait(true);
    }

    /// <summary>恢复/导入共有的播放设置收尾；与窗口重建分开，使用同一批已恢复的内存设置。</summary>
    private async Task ReapplyPlaybackPreferencesAsync(string notice)
    {
        var subtitlesApplied = await PushSubtitleStyleAsync().ConfigureAwait(true);
        var screenshotsApplied = await PushScreenshotOptionsAsync().ConfigureAwait(true);
        AnnounceScreenshotDirectory();
        if (!subtitlesApplied || !screenshotsApplied) return;

        Notify(null, HasUnsavedChanges ? "偏好已在本次运行应用，但尚未保存；请重试保存。" : notice,
            HasUnsavedChanges ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
    }

    /// <summary>
    /// 主题色板。点中就立刻换，不等保存 —— 一套配色是看着挑的，不是填完表格再确认的。
    /// <para>
    /// 这里原先是个下拉框。一套主题该让人看见它的颜色，而不是读它的名字，所以现在是几块各自画着自己那套
    /// 配色的方块，点哪块是哪块（见 <see cref="SettingThemeRow"/>）。
    /// </para>
    /// <para>
    /// 直接调 <see cref="ThemeHost.Apply(UiTheme)"/>：它是这次换肤的那一个执行点，同一个程序集里的静态类，
    /// 再包一层接口注进来只是为了好看。写进设置文档的仍然只有 id，落盘由行自己的 <c>Save</c> 负责。
    /// </para>
    /// <para>
    /// 认不出来的 id 走不到这里 —— <c>SettingsMigration.Normalize</c> 已经在读盘时拨回默认那套了 —— 但真漏
    /// 过来一个，角标会落在实际生效的那一套上（见 <c>SettingThemeRow.Sync</c>），而不是几块全不带角标。
    /// </para>
    /// </summary>
    private SettingThemeRow ThemeSwatches(UiSettings ui) =>
        new(
            "主题",
            $"点中即生效，{UiThemes.All.Count} 套配色都过了正文对比度 4.5:1 的门槛。",
            UiThemes.All,
            () => ui.Theme,
            value =>
            {
                ui.Theme = value;
                ThemeHost.Apply(value);
            },
            Save);

    // ── Row factories ────────────────────────────────────────────────────────────────────────────────

    private SettingChoiceRow Pick<T>(string label, string? note, IEnumerable<(string Label, T Value)> options, Func<T> read, Action<T> write, IEqualityComparer<T> comparer, Func<T, string>? describe = null, Action? after = null)
    {
        var (choices, selected) = Options(options, read, write, comparer, describe);
        return new SettingChoiceRow(label, note, choices, selected, Save, after);
    }

    /// <summary>
    /// The entries of one drop-down and which of them is current. Split out of <see cref="Pick"/> for the one
    /// row whose list arrives after the page does — 音频输出设备, whose devices have to be read out of a
    /// throwaway libmpv context — so that refilling it goes through exactly the same arithmetic, the fallback
    /// entry below included, rather than a second copy of it.
    /// </summary>
    private static (List<SettingChoice> Choices, SettingChoice? Selected) Options<T>(
        IEnumerable<(string Label, T Value)> options,
        Func<T> read,
        Action<T> write,
        IEqualityComparer<T> comparer,
        Func<T, string>? describe)
    {
        var current = read();
        var choices = new List<SettingChoice>();
        SettingChoice? selected = null;

        foreach (var (text, value) in options)
        {
            var captured = value;
            var choice = new SettingChoice(text, () => write(captured));
            choices.Add(choice);
            if (selected is null && comparer.Equals(captured, current)) selected = choice;
        }

        // What is stored is none of the offered values — a language code this catalogue has no name for, a
        // shader group removed since it was picked, a pair of headphones that has been unplugged. Leaving the
        // box empty hides the setting, and worse, arms it: an unselected ComboBox takes whatever the next click
        // lands on, and the stored value is never written back, so opening the list to see what it says is
        // enough to lose it. It gets an entry of its own instead, marked as having come from the file, and
        // choosing it writes the same value again.
        if (selected is null && current?.ToString() is { Length: > 0 } stored)
        {
            selected = new SettingChoice(describe?.Invoke(current) ?? $"{stored}（设置文件中的值）", () => write(current));
            choices.Add(selected);
        }

        return (choices, selected);
    }

    private SettingChoiceRow Choice<T>(string label, IEnumerable<(string Label, T Value)> options, Func<T> read, Action<T> write, string? note = null, Func<T, string>? describe = null) =>
        Pick(label, note, options, read, write, EqualityComparer<T>.Default, describe);

    /// <summary>
    /// A drop-down over one of the mpv option catalogues. Matched case-insensitively, because these values
    /// go into a settings file a person may well have edited by hand, and mpv itself does not care.
    /// <para>
    /// <paramref name="mpvOption"/> is required rather than optional: every row built by this helper exists to
    /// set one named mpv option, so the one that forgets to say which is a compile error rather than a row the
    /// reader has to guess at. See <see cref="Annotate"/>.
    /// </para>
    /// </summary>
    private SettingChoiceRow Mpv(string label, IReadOnlyList<MpvChoice> options, Func<string> read, Action<string> write, string mpvOption, string? note = null, Action? after = null) =>
        Pick(label, Annotate(note, mpvOption), options.Select(option => (Label: option.Label, Value: option.Value)), read, write, StringComparer.OrdinalIgnoreCase, after: after);

    /// <summary>
    /// One of the three 字幕颜色 rows: <c>#RRGGBB</c> through the HTML 颜色选择器 the swatch opens, or the
    /// empty string for 「不设置，跟随 mpv 自己的默认」. The picker is the row's own business through the
    /// two-way binding on <see cref="SettingColorRow.Color"/> — this factory only builds the row.
    /// </summary>
    private SettingColorRow ColorRow(string label, Func<string> read, Action<string> write, string mpvOption, string? note = null) =>
        new(label, Annotate(note, mpvOption), read(), write, Save);

    /// <summary>
    /// 一行开关。The optional <c>after</c> hook mirrors the Choice row's: fired after the save, for the one
    /// switch whose flip means more than the document — 精简模式，整页的说明跟着它收放（2026-09-25）。
    /// </summary>
    private SettingToggleRow Toggle(string label, string note, Func<bool> read, Action<bool> write, string mpvOption = "", Action? after = null) =>
        new(label, Annotate(note, mpvOption) ?? "", read(), write, Save, after);

    private SettingNumberRow Number(string label, double minimum, double maximum, Func<int> read, Action<int> write, string? note = null, Action? after = null, string mpvOption = "") =>
        new(label, Annotate(note, mpvOption), minimum, maximum, read(), value => write((int)value), () => read(), Save, after);

    /// <summary>
    /// 一行滑块。<paramref name="after"/> 与 Toggle 行的同名钩子同一形状（写盘之后、有文档之外的后果的那一行
    /// 专用，2026-10-11 截图画质行加入）：截图质量/压缩拖一下就把整组实时推给正在播的那部。
    /// </summary>
    private SettingSliderRow Slider(string label, double minimum, double maximum, double step, Func<double> read, Action<double> write, string? note = null, string mpvOption = "", Action? after = null) =>
        new(label, Annotate(note, mpvOption), minimum, maximum, step, read(), write, Save, after);

    /// <summary>
    /// mpv 单位的滑块（描边大小、阴影共用，2026-09-06「把字号、描边大小，阴影也改成滑块」，取代上一批的
    /// 自由数字输入行）。设置文件里存的是字符串、空串是「不设置，mpv 自己说了算」，而滑块没有「不填」这个
    /// 状态，所以装值时把「不设置」落到 mpv 自己的默认上 —— 那是同一个样子换了个写法；拖一下就写实了。
    /// 写回去的是规范数字（和 <see cref="MpvOutputOptions.ClampSubtitleUnit"/> 落盘的格式一致），范围仍是
    /// mpv 自己的 0–10，手改设置文件越界的照样在每次读盘时被它拦回来。
    /// </summary>
    private SettingSliderRow UnitSlider(string label, double whenUnset, Func<string> read, Action<string> write, string? note = null, string mpvOption = "", Action? after = null) =>
        new(label, Annotate(note, mpvOption),
            MpvOutputOptions.SubtitleUnitMinimum, MpvOutputOptions.SubtitleUnitMaximum, 0.05,
            ParseUnit(read()) ?? whenUnset,
            value => write(value.ToString("0.###", CultureInfo.InvariantCulture)),
            Save, after);

    private static double? ParseUnit(string? value) =>
        double.TryParse((value ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// A row's note with the mpv option it writes named at the end — 「补偿刷新率不匹配造成的抖动（mpv：
    /// interpolation）」, or just 「mpv：gpu-api」 on a row that had no note of its own.
    /// <para>
    /// Composed here rather than typed into two dozen note strings, so the shape cannot drift row to row. It
    /// exists because the page could not answer 「哪一行是 interpolation」 — the notes said what each row does
    /// and never what mpv calls it, which is the name every piece of mpv documentation is indexed by. Rows whose
    /// setting is this client's own behaviour rather than an mpv option pass nothing and read as before.
    /// </para>
    /// </summary>
    private static string? Annotate(string? note, string mpvOption)
    {
        var option = mpvOption.Trim();
        if (option.Length == 0) return note;

        return string.IsNullOrWhiteSpace(note) ? $"mpv：{option}" : $"{note}（mpv：{option}）";
    }

    /// <summary>
    /// A searchable list of the machine's font families. Kept in a field as well as returned: the scan
    /// finishes after the card is built, and this is the row it has to be handed to.
    /// <para>
    /// Whatever has already been scanned goes in immediately, which is what a second visit to the page
    /// gets — the library reads the font files once per session, so only the first visit ever sees the
    /// row hold nothing but the stored value.
    /// </para>
    /// </summary>
    private SettingFontRow Font(string label, string? note, Func<string> read, Action<string> write, string mpvOption = "")
    {
        var row = new SettingFontRow(label, Annotate(note, mpvOption), read(), write, Save);

        if (_fonts is { Ready.Families.Count: > 0 } library) row.Fill(library.Ready);

        _subtitleFont = row;
        return row;
    }

    private SettingTextRow Text(string label, string placeholder, Func<string> read, Action<string> write, string? note = null) =>
        new(label, note, placeholder, read(), typed =>
        {
            var value = typed.Trim();
            write(value);
            return value;
        }, Save);

    /// <summary>
    /// 一行读数，见 <see cref="SettingFactRow"/>。不接设置，所以不带 <see cref="Save"/> —— 它只是把一件事
    /// 说出来，顺带给一个去处。
    /// </summary>
    private static SettingFactRow Fact(string label, string? note, string value, string? actionLabel = null, Action? act = null) =>
        new(label, note, value, actionLabel, act);

    /// <summary>
    /// A text box holding a filesystem path. <see cref="Text"/> with one pair of surrounding double quotes
    /// taken off as well as the whitespace: Explorer's 「复制为路径」 puts them on the clipboard, pasting one in
    /// is the ordinary way to fill such a box, and a double quote cannot occur in a Windows path — so a value
    /// wearing them is always a paste and never a filename. See <see cref="TypedPath.Clean"/>.
    /// </summary>
    private SettingTextRow PathBox(string label, string placeholder, Func<string> read, Action<string> write, string? note = null) =>
        new(label, note, placeholder, TypedPath.Clean(read()), typed =>
        {
            var value = TypedPath.Clean(typed);
            write(value);
            return value;
        }, Save);

    /// <summary>
    /// A comma-separated list in a text box. The box is redisplayed from the parsed list, so what is on
    /// screen once the box loses focus is what was actually stored — separators tidied, blanks and
    /// duplicates gone — rather than the raw typing.
    /// </summary>
    private SettingTextRow List(string label, string placeholder, Func<List<string>> read, Action<List<string>> write) =>
        new(label, null, placeholder, string.Join(", ", read()), typed =>
        {
            var items = Split(typed);
            write(items);
            return string.Join(", ", items);
        }, Save);

    /// <summary>
    /// A priority list picked from a drop-down of check-boxes rather than typed out — 字幕语言优先级 became this
    /// on 2026-09-22（「参考上图改成复选下拉」）. The list arrives ordered by <see cref="TrackLanguagePriority.OrderedChoices"/>
    /// (chosen first, in stored order, then the rest of the catalogue, then 其他字幕); ticking a language adds it,
    /// dragging or the arrows set its priority, and what is stored is the ticked names in that order. See
    /// <see cref="SettingLanguagesRow"/> — it and <see cref="Languages"/> reach the same
    /// <see cref="PlaybackSettings.SubtitleLanguages"/>, so a settings file written by either still reads back.
    /// <paramref name="placeholder"/> is what the drop-down button shows while nothing is ticked.
    /// </summary>
    private SettingLanguagesRow LanguagePriority(string label, string placeholder, Func<List<string>> read, Action<List<string>> write, string? note = null, IReadOnlyCollection<string>? exclude = null) =>
        new(label, note, placeholder, read, write, Save, exclude);

    /// <summary>
    /// 一张关键词规则表：一行一个词加 优先／默认／候补，可自定义添加。字幕标题、音轨格式与视频文件名三处共用同一套
    /// <see cref="KeywordRule"/>／<see cref="KeywordFilter"/>／<see cref="SettingTitleRulesRow"/>。
    /// </summary>
    private SettingTitleRulesRow KeywordRules(string label, Func<List<KeywordRule>> read, Action<List<KeywordRule>> write, string? note = null) =>
        new(label, note, read, write, Save);

    /// <summary>
    /// Splits a typed list of plain words. Both widths of comma and semicolon, plus the ideographic comma —
    /// 「动画、动漫」 is how the list this serves gets written on a Chinese keyboard, and it used to come out
    /// as one keyword. The priority marks <c>&gt;</c> and <c>→</c> are deliberately not separators here:
    /// these lists are unordered, and a word is a likelier thing to find around an arrow than a boundary.
    /// </summary>
    private static List<string> Split(string value) =>
        value.Split([',', '，', ';', '；', '、'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
