using System.ComponentModel;
using Momoka.Infrastructure;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell;
using Momoka.Shell.ViewModels;
using Momoka.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Momoka.Shell.Views;

/// <summary>
/// What <see cref="SettingsPage"/> needs: somewhere to resolve its services from, and which entry in the
/// left-hand list to open on; see <see cref="LibraryRequest"/> for why it is passed rather than reached for.
/// <para>
/// <paramref name="Category"/> is what makes 服务器 and 诊断 reachable from anywhere: both are entries in
/// this page's own list now (需求 2), so 「open 设置 on 诊断」 is one navigation rather than a page of its
/// own. Empty means 「wherever it was left」 — the page is cached, and re-opening it on the card the user was
/// last reading is the reason it is.
/// </para>
/// </summary>
internal sealed record SettingsRequest(IServiceProvider Services, string Category = "");

/// <summary>
/// 设置页。左侧分类，右侧卡片，播放、字幕、视频输出、音频和着色器的修改会立即写入设置文件。
/// <para>
/// What each row is, what it reads and what it writes now lives in <see cref="SettingsViewModel"/>, and what
/// each row looks like lives in the templates in this page's XAML. What is left here is what only a page can
/// do: read the navigation parameter, host the pages that are not cards, and answer the self-check.
/// </para>
/// <para>
/// The procedural version this replaces built all sixty-odd rows in C#, which put the labels and ranges, the
/// layout, and the settings access in one file and gave the page a 703-line code-behind. It also needed two
/// mechanisms this does not have: a list of refresher closures replayed to push settings into controls, and
/// a page-wide 「syncing」 flag to stop those pushes from being written straight back. Each row now seeds
/// itself from its setting once and writes from then on, so neither exists.
/// </para>
/// </summary>
public sealed partial class SettingsPage : Page, IShellContent
{
    private const string Category = "设置";

    // DefaultListViewItemStyle 的 ListViewItemMinHeight（generic.xaml）。展开的条目用默认行高；
    // 收起的服务器组只把容器高度收到 0，不动默认样式 —— Momoka 那一组的行距和高亮照旧。
    private const double CategoryItemMinHeight = 40;

    private readonly HashSet<SettingsCategoryGroup> _observedGroups = [];
    private SettingsRequest? _request;
    private EmbySession? _session;
    private readonly LoadGeneration _activity = new();
    private CancellationToken _activityToken;

    public SettingsPage()
    {
        InitializeComponent();
        ObserveCategoryGroups();
        CategoryGroupsSource.Source = ViewModel.CategoryGroups;
        Loaded += (_, _) =>
        {
            if (!_activity.IsCurrent(_activityToken)) return;
            HomeMotion.PreferenceChanged -= ApplySettingsMotionPreference;
            HomeMotion.PreferenceChanged += ApplySettingsMotionPreference;
            ApplySettingsMotionPreference();
            ShellPrefs.DanmakuChanged -= ViewModel.OnDanmakuPreferencesChanged;
            ShellPrefs.DanmakuChanged += ViewModel.OnDanmakuPreferencesChanged;
            QueueSettingsEntrance();
        };
        Unloaded += (_, _) =>
        {
            if (XamlRoot is not null) return;
            HomeMotion.PreferenceChanged -= ApplySettingsMotionPreference;
            ShellPrefs.DanmakuChanged -= ViewModel.OnDanmakuPreferencesChanged;
            StopSettingsEntrance();
            DetachSettingsContentMotion();
        };

        // Kept alive across navigations, which is what makes half-typed text in a box — and which card was
        // open — survive a trip to another page and back. Signing out drops the frame's content entirely, so
        // nothing outlives a session; within one, leaving this page no longer costs an edit.
        NavigationCacheMode = NavigationCacheMode.Required;

        // 「恢复默认设置」按下之后要问一次，而对话框要这一页的 XamlRoot（见 ConfirmDialog）。同 ServersPage 的
        // 那一句，也是同一个理由：视图模型手上只有问题和答案，摆得出对话框的只有页面。
        ViewModel.UseConfirm(ConfirmDialog.For(this));

        // 「关于」卡上的「备份配置文件」「恢复配置」两颗按钮要弹文件框，而文件框同样要这一页的 XamlRoot 才拿得到
        // 窗口句柄（见 SettingsFile）。同上一句一手交出去。
        ViewModel.UseFilePickers(SettingsFile.SaveFor(this), SettingsFile.OpenFor(this));

        // The two hosted entries are selected the same way a card is, so the frame that holds them has to
        // follow the selection rather than only the navigation parameter.
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.CategoryGroups)) UpdateCategoryGroups();
            if (_activity.IsCurrent(_activityToken) && e.PropertyName == nameof(SettingsViewModel.SelectedCategory))
            {
                StopSettingsContentMotion();
                ShowHosted();
                QueueSettingsEntrance();
            }
        };
    }

    internal SettingsViewModel ViewModel { get; } = new();

    internal bool IsReady => ViewModel.IsReady;

    private void OnSettingsSearchClick(object sender, RoutedEventArgs args) => SubmitSettingsSearch(SettingsSearchBox, null);

    /// <summary>
    /// Whether the 字幕 card's font picker holds the machine's families yet. Read by the self-check only:
    /// the scan lands a moment after the page does, and a report written in that moment would be a report
    /// about a list that had not arrived.
    /// </summary>
    internal bool FontsReady => ViewModel.FontsReady;

    /// <summary>The font picker, put through a search and put back. Null before the 字幕 card exists.</summary>
    internal SettingFontRow.Probe? MeasureFontPicker() => ViewModel.MeasureFontPicker();

    /// <summary>
    /// 自检：主题色板 —— 那一行自己答的几件事（见 <see cref="SettingThemeRow.Measure"/>），加上屏上真的画出
    /// 了几块。null 表示界面那张卡片还没建出来。
    /// <para>
    /// 后半句非得在树上数不可：色板缺席在别的读数里一点动静都没有 —— 那一行本身照样是一个行容器，
    /// <see cref="Realised"/> 照样把它数进去。模板选择器少一个 case、或者模板里写错一个资源键，屏上就是一行
    /// 标题底下空着一块，而这一页每一个数字都对得上。
    /// </para>
    /// </summary>
    internal (bool Ok, string Detail)? MeasureThemeSwatches()
    {
        if (ViewModel.Themes is not { } row) return null;

        var drawn = 0;
        CountSwatches(this, ref drawn);

        var probe = row.Measure();
        return (probe.Ok && drawn == row.Swatches.Count, $"{probe.Detail}；屏上 {drawn} 块");
    }

    /// <summary>
    /// 自检：主页版面那张表 —— 那一行自己答的（见 <see cref="SettingsViewModel.MeasureHomeRows"/>），加上屏上
    /// 真的画出了几行、那几行装不装得下。null 表示主页那张卡片还没建出来。
    /// <para>
    /// 屏上那半非得在树上数：模板选择器少一个 case，屏上就是一行标题底下空着一块，而这一页别的数字一个都不会动
    /// （同 <see cref="MeasureThemeSwatches"/>）。
    /// </para>
    /// </summary>
    internal (bool Ok, string Detail)? MeasureHomeRows()
    {
        if (ViewModel.HomeRows is null) return null;

        // 这张表只在它那张卡显示着的时候量得准，也只在显示着的时候才被重填（见 SettingsViewModel.RefreshHomeRows）。
        // 分类走查结束时页面停在最后一张卡上，所以先把「主页」摆到前面 —— 这一下也顺带把它重填一次 —— 量完再
        // 还回原来那张。
        var previous = SelectedCategory;
        SelectedCategory = SettingsViewModel.FirstCardCategory;
        SettleHomeList();

        if (ViewModel.MeasureHomeRows() is not { } probe)
        {
            SelectedCategory = previous;
            return null;
        }

        var drawn = 0;
        CountHomeRows(this, ref drawn);

        var expected = ViewModel.HomeRows?.Rows.Count ?? 0;

        // 那张表是给死了高度的（ListHeight），所以一行只要变高，末一排就落在框外面 —— 而它照样算「画出来了」，
        // 上面那两个数一个都不会动。往一行里加控件（那两颗箭头就是）之后最容易踩这一脚。
        var (fits, room) = HomeRowsFit();

        SelectedCategory = previous;

        return (probe.Ok && drawn == expected && fits, $"{probe.Detail}；屏上 {drawn} 行、{room}");
    }

    /// <summary>
    /// 把那张表催到当前这一帧：重填是一记 Reset，容器要下一趟布局才建得出来，不催就会量到上一帧的读数。
    /// 这只是把读数摆到当下，不放宽判据 —— 表真少画一行、真装不下时，上面那两个数照样红。
    /// </summary>
    private void SettleHomeList()
    {
        if (FindHomeList(this) is not { } list) return;

        for (var pass = 0; pass < 4; pass++)
        {
            list.UpdateLayout();

            var drawn = 0;
            CountHomeRows(this, ref drawn);
            if (drawn >= list.Items.Count) return;
        }
    }

    /// <summary>
    /// 那张表装不装得下自己那几行：几行合起来多高，对上表自己多高。见 <see cref="MeasureHomeRows"/>。
    /// </summary>
    private (bool Fits, string Detail) HomeRowsFit()
    {
        if (FindHomeList(this) is not { } list) return (false, "找不到那张表");

        var tallest = 0d;
        var stack = 0d;

        for (var index = 0; index < list.Items.Count; index++)
        {
            if (list.ContainerFromIndex(index) is not FrameworkElement item) continue;

            tallest = Math.Max(tallest, item.ActualHeight);
            stack += item.ActualHeight;
        }

        return (stack > 0 && stack <= list.ActualHeight + 0.5,
            $"一行最高 {tallest:0}、合起来 {stack:0} 对表高 {list.ActualHeight:0}"
                + (stack <= list.ActualHeight + 0.5 ? "" : "（末一排落在框外面）"));
    }

    /// <summary>
    /// 自检：那张表真换得了次序 —— 「设置里新增拖拽排序」。
    /// <para>
    /// 拖那一下没法自动做一遍：这台机器上注不进鼠标事件（见 CLAUDE.md）。所以这一条问两件事。一是拖的那条路按
    /// 官方那份配方接齐了没有 —— <c>CanReorderItems</c> ＋ <c>AllowDrop</c> 才是「鼠标拖得动」的那两位；
    /// <c>CanDragItems</c> 管的是把一项当数据拖出去（以及收不收 <c>DragItemsStarting</c>），换位不经过它，每个
    /// 容器上的 <c>CanDrag</c> 也只是它的回声，所以那一位只报数不判红。二是不使指针的那条路真换得动：那两颗箭头
    /// 就地在一张三行的假表上按几下（<see cref="SettingHomeLayoutRow.Probe"/>），并且屏上这张表的两头真按
    /// 「到顶了」置了灰。
    /// </para>
    /// <para>
    /// 一张换不了次序的表在屏上和换得了的长得一模一样，行数、勾选、存档那几行读数也一个都不会差 —— 这一条是那件
    /// 事唯一的证人。
    /// </para>
    /// </summary>
    internal (bool Ok, string Detail)? MeasureHomeDrag()
    {
        if (FindHomeList(this) is not { } list) return null;

        var rows = list.Items.Count;
        var draggable = 0;

        for (var index = 0; index < rows; index++)
            if (list.ContainerFromIndex(index) is ListViewItem { CanDrag: true }) draggable++;

        // 屏上这张表的两头：头一行的「上移」和末一行的「下移」是灰的。
        var table = ViewModel.HomeRows;
        var ends = table is { Rows.Count: > 1 }
            && !table.Rows[0].CanUp && table.Rows[0].CanDown
            && table.Rows[^1].CanUp && !table.Rows[^1].CanDown;

        var arrows = SettingHomeLayoutRow.Probe();

        static string On(bool value) => value ? "开" : "关";

        return (rows > 0 && list.CanReorderItems && list.AllowDrop && arrows.Ok && ends,
            $"表里 {rows} 行，拖那条路 CanReorderItems {On(list.CanReorderItems)}、"
                + $"AllowDrop {On(list.AllowDrop)}、ReorderMode {list.ReorderMode}"
                + $"（另报：CanDragItems {On(list.CanDragItems)}、容器上 CanDrag {draggable} 行，换位用不到）；"
                + $"箭头那条路 {arrows.Detail}；屏上两头{(ends ? "置了灰" : "没置灰")}");
    }

    /// <summary>
    /// What the self-check reports: the cards and rows that were built, and the category on screen.
    /// </summary>
    internal (int Sections, int Rows, string SelectedCategory, int VisibleRows) Summary => (
        ViewModel.Cards.Count,
        ViewModel.RowCount,
        SelectedCategory,
        VisibleRows);

    /// <summary>How many row containers the card currently on screen holds.</summary>
    internal int VisibleRows => ViewModel.Sections
        .Where(section => section.IsVisible)
        .Sum(SettingsViewModel.Containers);

    /// <summary>The cards in order, and the containers each one holds. What the self-check walks.</summary>
    internal IReadOnlyList<(string Category, int Rows)> Cards => ViewModel.Cards;

    /// <summary>
    /// The left-hand list. Reported separately from <see cref="Cards"/> so the self-check can check the two
    /// against each other: the list and the cards are written out by hand in different places, and a typo in
    /// either is an entry that selects nothing or a card nothing selects.
    /// </summary>
    internal IReadOnlyList<string> Categories => [.. ViewModel.Categories.Select(entry => entry.Key)];

    /// <summary>
    /// Shows one card, the same as clicking its entry in the left-hand list — the list's selection is bound
    /// two-way, so it follows. Settable for the self-check's walk, which has to open each card to make it
    /// build its rows.
    /// </summary>
    internal string SelectedCategory
    {
        get => ViewModel.SelectedCategory;
        set => ViewModel.SelectedCategory = value;
    }

    /// <summary>
    /// How many setting rows really made it onto the visual tree, counted off the tree rather than off the
    /// view model.
    /// <para>
    /// The same distinction <see cref="ServersPage.Realised"/> and <see cref="DiagnosticsPage.Realised"/>
    /// exist for, and it matters more here than on either of them: those pages have one template, this page
    /// has eight and picks between them at runtime. A bad resource key inside one of them, a literal of the
    /// wrong type, or a shape the selector has no case for are all invisible to the build and show up as a
    /// row that is simply not there. Counting the rows that rendered is what turns that into a failure.
    /// </para>
    /// <para>
    /// This counts what is realised now, which is the visible card — collapsed cards do not build their
    /// rows. So the number to compare it against is <see cref="VisibleRows"/>, not
    /// <see cref="SettingsViewModel.RowCount"/>, and the self-check walks the categories to reach the
    /// templates that only later cards use.
    /// </para>
    /// </summary>
    internal int Realised
    {
        get
        {
            var rows = 0;
            Count(this, ref rows);
            return rows;
        }
    }

    public object? NavigationRequest => _request;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not SettingsRequest request)
        {
            Log.Warn(Category, "导航参数缺失");
            return;
        }

        Tag = "settings";

        // This page is cached, so coming back to it normally means everything is already built — including
        // any half-typed text in a box, which is the point of caching it. Rebuilt only when the container is
        // a different one, i.e. after signing out and back in, where every row's closure would otherwise
        // still be reading the previous session's settings document. The container is what is compared and
        // not the request: the shell builds a fresh request per navigation, so comparing those would rebuild
        // the page — and throw the typing away — on every visit.
        var built = ReferenceEquals(_request?.Services, request.Services) && ViewModel.IsReady;

        StopMoviePilotMonitoring();
        StopSourceMonitoring();
        StopSettingsSaveState();
        _activityToken = _activity.Begin();
        ViewModel.Activate();
        _request = request;
        // Hide 后 XAML 仍可保持 Loaded；重开不能只等另一次 Loaded 才恢复动效偏好通知。
        HomeMotion.PreferenceChanged -= ApplySettingsMotionPreference;
        HomeMotion.PreferenceChanged += ApplySettingsMotionPreference;
        ApplySettingsMotionPreference();

        if (!built)
        {
            ViewModel.Attach(
                request.Services.GetRequiredService<ISettingsService>(),
                request.Services.GetRequiredService<ShaderStaging>(),
                request.Services.GetRequiredService<FontLibrary>(),
                request.Services.GetRequiredService<AppPaths>(),
                request.Services.GetRequiredService<Platform.ISystemLauncher>(),

                // 开机自启那一格：它读写的不是设置文件而是机器上的启动项，所以这一页要的是「这台机器上这一条
                // 此刻在不在、拨一下改得动吗」。打包安装（MSIX）之后它自己答「办不到」，那一行整个不显示。
                request.Services.GetRequiredService<Platform.IStartupRegistration>(),
                request.Services.GetRequiredService<AudioDeviceCatalogue>(),

                // 字幕外观改一行就推给正在播的那部片子。交出去的是一个方法而不是 PlaybackService 本身：
                // 这一页要的是「重发一遍字幕外观」这一件事，不是播放器。同 ShellServices 里把后端工厂当方法组
                // 递出去的写法。
                request.Services.GetRequiredService<PlaybackService>().ApplySubtitleStyleAsync,

                // 截图保存目录同理：改一行就写进正播那部的 screenshot-directory 属性，交方法不交服务。
                request.Services.GetRequiredService<PlaybackService>().ApplyScreenshotDirectoryAsync,

                // 截图格式与画质（2026-10-11 用户令「参考文档新增自定义选项」）同一通道：那一行改完把整组
                // 推给正播的那部。服务自己读设置，这里只交「推一遍」这一件事。
                request.Services.GetRequiredService<PlaybackService>().ApplyScreenshotOptionsAsync,

                request.Services.GetRequiredService<MoviePilotService>(),
                request.Services.GetRequiredService<MoviePilotCredentials>(),

                // 「关于」卡上那颗手动「检查新版本」：去项目的发布页问一句有没有比这一版新的。打开那个页面仍走
                // 这一页本来就有的 ISystemLauncher，所以这里只交「查一次」这一件事的客户端。
                request.Services.GetRequiredService<UpdateClient>(),

                // 左栏的服务端管理入口只有服务器管理员才看得见，所以这一页要知道当前账号的身份（见
                // RefreshServerAdministrationAsync 与下面那一句）。
                request.Services.GetRequiredService<IServerCapabilities>(),

                // 「关于」卡上的图片缓存读数与清除：它数的是本机缓存目录，不是某台服务器，所以搬出了服务器页。
                request.Services.GetRequiredService<EmbyImageStore>(),
                request.Services.GetService(typeof(SubtitleTranslationCredentials)) as SubtitleTranslationCredentials);
            _ = ViewModel.ReloadAsync();
        }

        // Only when the page was cached, i.e. built on an earlier visit: the fresh path's ReloadAsync
        // already fills the device row. A cached page skips ReloadAsync by design (in-progress edits are
        // the point), but the device list is a reading, not an edit — it re-enumerates on re-entry.
        if (built)
        {
            // 其他页面也共用设置服务：只读回保存 flag，不用整页 Reload 丢掉输入。
            ViewModel.SyncSaveState();
            _ = ViewModel.RefreshAudioDevicesAsync();
        }

        WatchSettingsSaveState(request.Services.GetRequiredService<ISettingsService>());
        WatchMoviePilot(request.Services.GetRequiredService<MoviePilotService>());

        // 在线来源变化也会改变界面风味（第二台服务器上线 → 通用界面；只剩一台 → 那台的界面），
        // 设置窗口可能正开着，左栏要当场跟着收放服务端管理入口，不等下一次打开设置。
        _session = request.Services.GetRequiredService<EmbySession>();
        _session.SourcesChanged += OnSourceListChanged;
        _session.PermissionsChanged += OnPermissionsChanged;

        // 每一次打开都重新核一遍账号权限 —— 和上面那一行的理由同源：这一页是缓存的，换过账号之后它不重建，
        // 不重核就会留着上一个账号的答案（管理员看过的「控制台」那一组，普通账号再进来还列着）。这一趟先于
        // 下面 Select，好在两个路径上都已经发出去了：它回来得晚也只会把落点收回第一张卡，不会把选择拨乱。
        var hostedBeforeRefresh = HostedFrame.Content;
        _ = RefreshServerAdministrationAsync();

        // Both paths, and after the build: the cached path used to return before reading the parameter at
        // all, which is the whole of 「open 设置 on 诊断」 — the second time it was asked for, it opened on
        // whatever card was last read instead. ShowHosted is called either way, because a cached page whose
        // selection is already 诊断 has to put that page back in the frame after a session change dropped it.
        //
        // 只有在选择没动的时候才自己喊一次：动了的话 SelectedCategory 的通知已经把 ShowHosted 喊过了（见构造
        // 函数里那个订阅），紧接着再喊一遍就是把内嵌页面建两次 —— 头一个刚 OnNavigatedTo 就被第二次导航
        // OnNavigatedFrom 掉。第一次打开「服务器」「诊断」「通知」「网页控制台」正是选择会动的那一次，也就是说
        // 这几页每一次头回打开都白建一个，网页控制台那一页还白起一次 WebView2。
        // 同服换账号的同步刷新可能已经重建原生页；这一趟不能紧接着再重建、再起一次 GET。
        if (!Select(request.Category) && ReferenceEquals(hostedBeforeRefresh, HostedFrame.Content)) ShowHosted();
        QueueSettingsEntrance();
    }

    /// <summary>
    /// 重新核一遍当前账号能不能管服务器，并让左栏那一列跟上 —— 「非管理员账号自动隐藏服务端管理入口」那一令。
    /// <para>
    /// 权限那份读数与落点都归 <see cref="SettingsViewModel.RefreshServerAdministrationAsync"/>。这里多做的一件
    /// 事是**重指一次分组源**：分组表换的是一份新表而不是原地增删，因为 <c>CollectionViewSource</c> 的分组视图
    /// 不会因为一个组从集合里消失就连它的标题一起收走 —— 组底下的条目会没，标题留在屏上（2026-10-10 截图实量，
    /// 见探针 settings-server-admin-hidden.png）。换源会让 ListView 把自己的选择清空、并顺着 TwoWay 绑定写回
    /// 一个 null，所以落点先记下来，换完再拨回去。
    /// </para>
    /// </summary>
    private async Task RefreshServerAdministrationAsync()
    {
        var activity = _activityToken;
        if (!_activity.IsCurrent(activity)) return;

        var beforeCategory = ViewModel.SelectedCategory;
        var refresh = ViewModel.RefreshServerAdministrationAsync();
        UpdateCategoryGroups();
        await refresh;
        if (!_activity.IsCurrent(activity)) return;
        UpdateCategoryGroups();

        // 切到另一台服务器、或换了一个产品时，同名分类（两家的「控制台」都叫控制台，飞牛的两台服务器
        // 都有「媒体库」）会留在原位，选中项不变也就不会触发 ShowHosted；右半边还挂着上一台服务器/账号的
        // 页面。它们自己的会话已经不当前了，这时换掉它 —— 网页控制台与飞牛的原生页都算。
        if (beforeCategory == ViewModel.SelectedCategory
            && ViewModel.ShowsHosted
            && HostedFrame.Content is ISessionScopedPage page
            && (!page.IsCurrentSession
                || !string.Equals(page.SourceKey, ViewModel.SelectedSourceKey, StringComparison.Ordinal)))
        {
            ShowHosted();
        }
    }

    private void UpdateCategoryGroups()
    {
        if (ReferenceEquals(CategoryGroupsSource.Source, ViewModel.CategoryGroups)) return;

        ObserveCategoryGroups();
        var selection = ViewModel.SelectedCategory;
        CategoryGroupsSource.Source = ViewModel.CategoryGroups;
        if (ViewModel.SelectedCategory != selection) ViewModel.SelectedCategory = selection;
    }

    /// <summary>
    /// 分组表每次重算都是新实例，折叠状态却可能在选中条目时自己变（见
    /// <see cref="SettingsViewModel.SelectedEntry"/>），所以在这里统一盯住：组一展开/收起，就把已经
    /// 生成的条目的容器高度跟上。
    /// </summary>
    private void ObserveCategoryGroups()
    {
        foreach (var group in _observedGroups) group.PropertyChanged -= OnCategoryGroupPropertyChanged;
        _observedGroups.Clear();
        foreach (var group in ViewModel.CategoryGroups)
        {
            if (_observedGroups.Add(group)) group.PropertyChanged += OnCategoryGroupPropertyChanged;
        }
    }

    private void OnCategoryGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsCategoryGroup.IsExpanded) && sender is SettingsCategoryGroup group)
            ApplyCategoryItemHeights(group);
    }

    /// <summary>
    /// 新生成的条目容器：展开的组用默认行高，收起的组收到 0。收起的条目不把容器 Visibility 折掉 ——
    /// 那样分组列表在第一组收起后就不再生成后面的组标题（2026-10-19 探针实测），三台服务器只剩一个
    /// 折叠头。容器始终可见、只收高度，组标题照常生成。
    /// </summary>
    private void OnCategoryContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is SettingsCategoryEntry entry && args.ItemContainer is ListViewItem item)
        {
            item.MinHeight = entry.Group.IsExpanded ? CategoryItemMinHeight : 0;
            item.IsHitTestVisible = !entry.IsSection;
            item.IsTabStop = !entry.IsSection;
        }
    }

    private void ApplyCategoryItemHeights(SettingsCategoryGroup group)
    {
        foreach (var row in group.Rows)
            if (CategoryNavigation.ContainerFromItem(row) is ListViewItem item)
                item.MinHeight = group.IsExpanded ? CategoryItemMinHeight : 0;
    }

    /// <summary>多服务器分组那一行：点一下展开/收起。收起后条目只从屏上消失，选中页不被换走。</summary>
    private void OnServerGroupToggle(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SettingsCategoryGroup group }) group.IsExpanded = !group.IsExpanded;
    }

    /// <summary>
    /// Opens one entry in the left-hand list. Blank leaves the selection alone — the page is cached, and
    /// pressing 设置 again is a request to see it, not to go back to the first card. A name the list does not
    /// hold falls back to the first card rather than selecting nothing, which would show an empty column.
    /// </summary>
    /// <returns>
    /// True when the selection really moved, which means <see cref="ShowHosted"/> has already run off the
    /// property notification and must not be run again. See the call site.
    /// </returns>
    private bool Select(string category)
    {
        if (string.IsNullOrEmpty(category)) return false;
        if (category == "着色器") category = "视频输出";
        // 两家的「网页控制台」从 2026-10-19 起都并入服务器目录的第一项「控制台」；命令行旧名字仍认。
        if (category == SettingsViewModel.DashboardCategory && ViewModel.ConsoleCategory is { } console)
            category = console;

        var entry = ViewModel.EntryFor(category);
        var target = entry?.Key ?? SettingsViewModel.FirstCardCategory;

        if (string.Equals(target, ViewModel.SelectedCategory, StringComparison.Ordinal)) return false;

        ViewModel.SelectedCategory = target;
        return true;
    }

    /// <summary>
    /// Keeps <c>HostedFrame</c> in step with the selected category: 服务器, 诊断, 通知, 飞牛的帐号两页
    /// 与服务器管理四页，以及 Emby／Jellyfin 的网页控制台页。（飞牛四页 2026-10-16 退役，2026-10-19
    /// 随飞牛设置目录一起接回；这次是原生页。）
    /// <para>
    /// 飞牛的原生页先问选中项属于哪一种产品（<see cref="SettingsViewModel.OpensFnNasPage"/>）：
    /// 多服务器分组里 Emby／Jellyfin 的「媒体库」「用户」与飞牛同名，只看页名会把它们带进飞牛的原生页。
    /// </para>
    /// <para>
    /// Navigated afresh every time rather than left in place, because all of them load when they are navigated
    /// to and none is worth showing stale — a server list from five minutes ago, a log that stopped tailing
    /// and a console whose token has since been replaced are exactly what these exist to not be. Selecting a
    /// card cancels whatever the page had in flight, which is what the frame would have done had we navigated
    /// away from it.
    /// </para>
    /// </summary>
    private void ShowHosted()
    {
        if (_request is null || !_activity.IsCurrent(_activityToken)) return;

        if (!ViewModel.CanOpenServerCategory(ViewModel.SelectedCategory))
        {
            ViewModel.SelectedCategory = SettingsViewModel.FirstCardCategory;
            return;
        }

        if (!ViewModel.ShowsHosted)
        {
            (HostedFrame.Content as IShellContent)?.Release();
            return;
        }

        var services = _request.Services;
        var category = ViewModel.SelectedEntry?.Label ?? ViewModel.SelectedCategory;
        var sourceKey = ViewModel.SelectedSourceKey;
        // Emby／Jellyfin 的目录里也有「媒体库」「用户」：飞牛的原生页只认飞牛那一组，其余同名项走网页控制台。
        var fnNas = ViewModel.OpensFnNasPage(category);

        Type page;
        object parameter;

        if (category == "服务器")
        {
            page = typeof(ServersPage);
            parameter = new ServerRequest(services);
        }
        else if (category == "诊断")
        {
            page = typeof(DiagnosticsPage);
            parameter = new DiagnosticsRequest(services);
        }
        else if (category == SettingsViewModel.NotificationsCategory)
        {
            page = typeof(NotificationsPage);
            parameter = new NotificationsRequest(services, sourceKey);
        }
        else if (fnNas && category == SettingsViewModel.FnNasPasswordCategory)
        {
            page = typeof(FnNasPasswordPage);
            parameter = new FnNasPasswordRequest(services, sourceKey);
        }
        else if (fnNas && category == SettingsViewModel.FnNasPreferencesCategory)
        {
            page = typeof(FnNasPreferencesPage);
            parameter = new FnNasPreferencesRequest(services, sourceKey);
        }
        else if (fnNas && category == SettingsViewModel.FnNasLibrariesCategory)
        {
            page = typeof(FnNasLibrariesPage);
            parameter = new FnNasLibrariesRequest(services, sourceKey);
        }
        else if (fnNas && category == SettingsViewModel.FnNasUsersCategory)
        {
            page = typeof(FnNasUsersPage);
            parameter = new FnNasUsersRequest(services, sourceKey);
        }
        else if (fnNas && category == SettingsViewModel.FnNasSettingsCategory)
        {
            page = typeof(FnNasSettingsPage);
            parameter = new FnNasSettingsRequest(services, sourceKey);
        }
        else if (fnNas && category == SettingsViewModel.FnNasTasksCategory)
        {
            page = typeof(FnNasTasksPage);
            parameter = new FnNasTasksRequest(services, sourceKey);
        }
        else if (ViewModel.ServerRoute(ViewModel.SelectedCategory) is { } route)
        {
            // 已经停在同一台服务器、同一个内嵌浏览器上：把新路由交给它，网页自己换页；不再新建一次
            // WebView，也就不会每切一次都重新过一遍启动画面。换服务器时来源键不同，必须新建。
            if (HostedFrame.Content is DashboardPage dashboard
                && string.Equals(dashboard.SourceKey, sourceKey, StringComparison.Ordinal)
                && dashboard.TryShowRoute(route)) return;

            page = typeof(DashboardPage);
            parameter = new DashboardRequest(services, route, Immersive: true, SourceKey: sourceKey);
        }
        else
        {
            Log.Warn(Category, $"没有页面对应设置分类 {ViewModel.SelectedCategory}");
            return;
        }

        HostedFrame.Navigate(page, parameter);

        // Nothing in this frame goes back, and a stack that grows by one per visit keeps a page alive with it.
        HostedFrame.BackStack.Clear();
    }

    /// <summary>
    /// What is really on screen in the right-hand column: the hosted page when one is showing, otherwise
    /// this page. The self-check reads the settings tree through this, so 服务器 and 诊断 are reported on
    /// where they now live rather than where they used to be.
    /// </summary>
    internal object CurrentPage =>
        ViewModel.ShowsHosted && HostedFrame.Content is { } hosted ? hosted : this;

    /// <summary>需求 8 的内嵌控制台，只在它正显示时不为 null；自检通过它读那一页的状态。</summary>
    internal DashboardPage? Dashboard => HostedFrame.Content as DashboardPage;

    /// <summary>「通知」那一页正住在这个框里时的引用；自检读它。</summary>
    internal NotificationsPage? Notifications => HostedFrame.Content as NotificationsPage;

    /// <summary>
    /// Drops whatever the hosted frame is holding, without changing which category is selected.
    /// <para>
    /// For the settings window's two hide paths. That window is hidden and shown again rather than destroyed,
    /// and 需求 8's console is a live browser process — which must not keep running behind a window the user
    /// has closed. Releasing rather than navigating away keeps the selection, so reopening shows the same
    /// page, navigated afresh by <see cref="ShowHosted"/>.
    /// </para>
    /// </summary>
    internal void ReleaseHosted()
    {
        // Hide 不触发 Frame.OnNavigatedFrom；先结束整页活动期，旧队列和 await 都不能重建内嵌页。
        _activity.Cancel();
        ViewModel.Cancel();
        StopSourceMonitoring();
        StopSettingsSaveState();
        HomeMotion.PreferenceChanged -= ApplySettingsMotionPreference;
        StopMoviePilotMonitoring();
        StopSettingsEntrance();
        StopSettingsContentMotion();
        (HostedFrame.Content as IShellContent)?.Release();
        if (HostedFrame.Content is Page { Content: FrameworkElement layout }) HomeMotion.Stop(layout);
    }

    /// <summary>
    /// Ends this visit's reads and subscriptions, then lets go of the hosted page. The cached settings
    /// rows keep their closures and draft values; only reopening starts another active visit.
    /// <para>
    /// <b>The hosted page is why this body is not empty.</b> A nested frame's content gets no
    /// <c>OnNavigatedFrom</c> when the outer page is navigated away from or dropped, so without this line the
    /// 控制台 kept a live WebView2 — a browser process the user cannot see — behind whatever page they went
    /// to next, and its log tail and pending requests kept running with it. The settings *window* was never
    /// affected: it calls <see cref="ReleaseHosted"/> itself on both of its hide paths. The path that was
    /// leaking is the fallback one, where the window could not be created and this page opens in the shell's
    /// own frame (see <c>ShellPage.ShowSettings</c>) — that frame both navigates away from it and, on
    /// signing out, drops it through <c>ReleaseContent</c>.
    /// </para>
    /// <para>
    /// The frame's content is left in place rather than cleared: <see cref="ShowHosted"/> navigates afresh on
    /// every visit anyway, and keeping it means the selection and the page on screen still agree.
    /// </para>
    /// </summary>
    public void Release() => ReleaseHosted();

    private ISettingsPersistence? _saveSettings;
    private EventHandler? _saveStateChanged;

    private void WatchSettingsSaveState(ISettingsPersistence settings)
    {
        StopSettingsSaveState();
        var activity = _activityToken;
        if (!_activity.IsCurrent(activity)) return;
        _saveSettings = settings;
        _saveStateChanged = (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (_activity.IsCurrent(activity) && ReferenceEquals(_saveSettings, settings)) ViewModel.SyncSaveState();
        });
        settings.SaveStateChanged += _saveStateChanged;
        ViewModel.SyncSaveState();
    }

    private void StopSettingsSaveState()
    {
        if (_saveSettings is { } settings && _saveStateChanged is { } handler) settings.SaveStateChanged -= handler;
        _saveSettings = null;
        _saveStateChanged = null;
    }

    private MoviePilotService? _moviePilot;

    internal void WatchMoviePilot(MoviePilotService service)
    {
        StopMoviePilotMonitoring();
        if (!_activity.IsCurrent(_activityToken)) return;
        _moviePilot = service;
        _moviePilot.AvailabilityChanged += OnMoviePilotAvailabilityChanged;
        ViewModel.RefreshHomeRows();
    }

    private void OnMoviePilotAvailabilityChanged(object? sender, EventArgs args)
    {
        var activity = _activityToken;
        if (!_activity.IsCurrent(activity) || !ReferenceEquals(sender, _moviePilot)) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_activity.IsCurrent(activity) && ReferenceEquals(sender, _moviePilot)) ViewModel.RefreshHomeRows();
        });
    }

    private void StopMoviePilotMonitoring()
    {
        if (_moviePilot is { } service) service.AvailabilityChanged -= OnMoviePilotAvailabilityChanged;
        _moviePilot = null;
    }

    private void StopSourceMonitoring()
    {
        if (_session is not null)
        {
            _session.SourcesChanged -= OnSourceListChanged;
            _session.PermissionsChanged -= OnPermissionsChanged;
        }
        _session = null;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Release();
        base.OnNavigatedFrom(e);
    }

    // 只刷新管理目录与已打开页；同身份权限通知不重建Shell来源、也不改设置保存状态。
    private void OnPermissionsChanged(object? sender, EmbySessionScope source) => RefreshAfterSessionChange(sender, rebuildHomeRows: false);

    private void OnSourceListChanged(object? sender, EventArgs e) => RefreshAfterSessionChange(sender, rebuildHomeRows: true);

    private void RefreshAfterSessionChange(object? sender, bool rebuildHomeRows)
    {
        var activity = _activityToken;
        if (!_activity.IsCurrent(activity) || !ReferenceEquals(sender, _session)) return;
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (!_activity.IsCurrent(activity) || !ReferenceEquals(sender, _session)) return;
            try
            {
                if (rebuildHomeRows) ViewModel.RefreshHomeRows();
                await RefreshServerAdministrationAsync();
            }
            catch (Exception error)
            {
                if (_activity.IsCurrent(activity) && ReferenceEquals(sender, _session))
                    Log.Warn(Category, "刷新服务器管理目录失败", error);
            }
        });
    }

    /// <summary>
    /// Counts realised row containers. <c>Content</c> rather than <c>DataContext</c>, for the reason spelled
    /// out on <see cref="ServersPage"/>'s counter: everything inside a realised row inherits that row as its
    /// data context, so counting by data context counts the controls in a row instead.
    /// </summary>
    private static void Count(DependencyObject node, ref int rows)
    {
        if (node is ContentPresenter { Content: SettingRow }) rows++;

        var children = VisualTreeHelper.GetChildrenCount(node);
        for (var index = 0; index < children; index++)
            Count(VisualTreeHelper.GetChild(node, index), ref rows);
    }

    /// <summary>Same walk, for the theme row's swatches. See <see cref="MeasureThemeSwatches"/>.</summary>
    private static void CountSwatches(DependencyObject node, ref int swatches)
    {
        if (node is ContentPresenter { Content: ThemeSwatch }) swatches++;

        var children = VisualTreeHelper.GetChildrenCount(node);
        for (var index = 0; index < children; index++)
            CountSwatches(VisualTreeHelper.GetChild(node, index), ref swatches);
    }

    /// <summary>同上，数主页版面那张表画出来的行。见 <see cref="MeasureHomeRows"/>。</summary>
    private static void CountHomeRows(DependencyObject node, ref int rows)
    {
        if (node is ContentPresenter { Content: HomeRowChoice }) rows++;

        var children = VisualTreeHelper.GetChildrenCount(node);
        for (var index = 0; index < children; index++)
            CountHomeRows(VisualTreeHelper.GetChild(node, index), ref rows);
    }

    /// <summary>
    /// 主页版面那张表本身，按「装的是版面项」认它 —— 这一页上再没有第二张这样的表。见
    /// <see cref="MeasureHomeDrag"/>，那一条要问的是表和它的容器，不是模板里的那几件东西。
    /// </summary>
    private static ListView? FindHomeList(DependencyObject node)
    {
        if (node is ListView list && list.Items.Count > 0 && list.Items[0] is HomeRowChoice) return list;

        var children = VisualTreeHelper.GetChildrenCount(node);
        for (var index = 0; index < children; index++)
            if (FindHomeList(VisualTreeHelper.GetChild(node, index)) is { } found) return found;

        return null;
    }
}
