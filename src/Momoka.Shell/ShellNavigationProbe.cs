using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.Platform;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Shell.Windowing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell;

/// <summary>真实 Shell 状态和控件的离线回归；只允许假传输、临时设置及拒绝播放的外壳。</summary>
internal sealed partial class ShellNavigationProbe
{
    internal static int ExitCode { get; private set; } = 1;
    private readonly StartupOptions _options;
    private readonly List<Check> _checks = [];
    private readonly List<Skipped> _skipped = [];
    private readonly HashSet<string>? _sections;
    private readonly Grid _root = new();
    private HostWindow? _window;

    private ShellNavigationProbe(StartupOptions options)
    {
        _options = options;
        _sections = options.ProbeSections is null ? null : new(options.ProbeSections, StringComparer.Ordinal);
    }
    internal static Task RunAsync(StartupOptions options) => new ShellNavigationProbe(options).RunAsync();

    private async Task RunAsync()
    {
        try
        {
            ThemeHost.Apply(_options.Theme ?? "midnight");
            _window = new HostWindow { Content = _root, FreeSizing = true };
            _window.Show(false, _options.Screen, new WindowBounds(80, 80, 1420, 980));
            await LayoutAsync(_root);
            // inspect 是留给人手点的那一版检查台：它自己建假身份和假数据，前面的整套用例既拖时间又
            // 会占着同一块桌面。直接进检查台，截图和手动操作都只针对它。
            if (_options.InspectShell)
            {
                await InspectAsync();
                ExitCode = 0;
                SaveScope();
                return;
            }
            await CaseAsync("登录页两步流程、主题与宽窄布局", SignInAppearanceAsync, 45);
            await CaseAsync("侧边栏模式、导航与宽窄布局", SidebarAsync, 45);
            await CaseAsync("侧栏 MoviePilot 订阅入口、可用性显隐与订阅列表页", MoviePilotSidebarAsync, 45);
            await CaseAsync("剧页面无封面播放按钮", () => DetailArtworkActionsAsync(EmbyItemType.Series), 30, section: ShellProbeSections.Library);
            await CaseAsync("季页面无封面播放按钮", () => DetailArtworkActionsAsync(EmbyItemType.Season), 30, section: ShellProbeSections.Library);
            await CaseAsync("电影页面无封面播放按钮", () => DetailArtworkActionsAsync(EmbyItemType.Movie), 30, section: ShellProbeSections.Library);
            // 集页封面只在宽版式里画（紧凑版式整个收掉），加上集页紧凑版式的那张背景图、以及尾部那条窄带在
            // 两档上的紧凑度：四档宽度各量一遍，截图 `detail-episode-band-<宽度>.png`。
            await CaseAsync("集页封面与窄窗背景图和尾部间距", EpisodeBandAsync, 30);
            await CaseAsync("电视媒体库封面显示网页版年份", SeriesCardDatesAsync, 30);
            await CaseAsync("电影与电视剧网页分类、工具栏、作用域及主题", LibraryBrowseAsync, 120);
            await CaseAsync("Jellyfin 媒体库标签、排序、筛选、布局与建议", JellyfinLibraryAsync, 90);
            await CaseAsync("飞牛媒体库工具栏到封面的间距与筛选面板", FnNasLibrarySpacingAsync, 45);
            await CaseAsync("飞牛剧集卡片右上角的集数角标", FnNasEpisodeCountAsync, 45);
            await CaseAsync("飞牛主页货架标题、无单集封面与轮播", FnNasHomeAsync, 45);
            await CaseAsync("电视节目收藏分排、分页、失败恢复与宽窄布局", LibraryFavoritesAsync, 60);
            await CaseAsync("电视节目预告日期分组、加载动画、分页和恢复", LibraryUpcomingAsync, 45);
            await CaseAsync("切回 Emby 恢复查询代次", ResumeQueryAsync);
            await CaseAsync("切季与整页刷新交错不清空", SeasonRefreshRaceAsync);
            await CaseAsync("旧导航恢复不补新搜索", NavigationRestoreRaceAsync);
            await CaseAsync("分页失败立即停止", PagingFailureAsync);
            await CaseAsync("离页不会复活补页", PagingCancelAsync);
            await CaseAsync("新查询失败不混旧页", QueryFailureAsync);
            await CaseAsync("同词重试并保留导航参数", SearchStateAsync);
            await CaseAsync("搜索分类、最近搜索和三种来源分流", SearchInteractionAsync);
            await CaseAsync("AniBT 搜索、资源、取消下载和连接隔离", AniBtSearchAsync);
            await CaseAsync("遗漏集来源菜单、AniBT 分集查询与下载选项", AniBtEpisodeSearchAsync);
            await CaseAsync("AniBT 逐文件识别、候选绑定和取消重新整理", AniBtScrapeAsync);
            await CaseAsync("MoviePilot 连接控制入口、断线回退与恢复", MoviePilotAvailabilityAsync);
            await CaseAsync("主页媒体库与订阅混排、显隐及偏好恢复", HomeOrderAsync, 45);
            await CaseAsync("所有账号的媒体库与最近添加、聚合搜索分页与详情来源", AggregateBrowsingAsync, 45);
            await CaseAsync("主页多账号标服务器名、浏览不标且版本仅多服务器显示", SourceCaptionTransitionsAsync, 45);
            await CaseAsync("指定账号登录、添加账号与取消返回", MultiSourceSignInAsync, 30);
            await CaseAsync("默认账号失效后保留其他来源主页", MultiSourceDefaultExpiryAsync, 30);
            // 「继续观看」那一格标记已看之后的去处（用户令 2026-10-15）：主页那一排与点进去的那一页各走一遍。
            await CaseAsync("继续观看标记已看后换成下一集或撤掉", WatchedResumeSlotAsync, 30);
            await CaseAsync("MoviePilot 关闭后清理历史与操作弹窗", MoviePilotUnavailableNavigationAsync);
            await CaseAsync("MoviePilot 关闭后收起菜单且保留普通菜单", MoviePilotMenusAsync);
            await CaseAsync("MoviePilot 搜索站点多选、清空、默认及离页取消", MoviePilotSearchSitesAsync);
            await CaseAsync("MoviePilot 资源卡片、季集筛选和下载选项", MoviePilotResourceOptionsAsync, 30);
            await CaseAsync("MoviePilot 搜索海报、资源卡片与多选筛选布局", MoviePilotSearchCardsAsync, 60, ShellProbeSections.MoviePilot);
            await CaseAsync("搜索宽窄布局和媒体库工具栏隔离", SearchHeaderLayoutAsync);
            await CaseAsync("加载中禁止播放全部", PlaybackBusyAsync);
            await CaseAsync("字母查询过期不继续", JumpCancelAsync);
            await CaseAsync("切季回到原季撤销旧请求", SeasonCancelAsync);
            await CaseAsync("切季目标失败不部分提交", SeasonFailureAsync);
            await CaseAsync("空季保留选择器", EmptySeasonAsync);
            await CaseAsync("离页后写操作不重开详情", DetailWriteAsync);
            await CaseAsync("文件选项长名称、断点与隐藏行", DetailPickerLayoutAsync);
            // 展开态：框架在弹层打开那一拍把选中项的文本收掉，框的宽度塌回 MinWidth，右邻被弹层压住
            // （用户 2026-10-16）。这一条量的是展开态的宽度与弹层右沿。
            await CaseAsync("文件选项下拉展开的位置与宽度", PickerDropDownAsync);
            await CaseAsync("隐藏首页行不请求", HiddenHomeAsync);
            await CaseAsync("通知空态属性通知", NotificationEmptyAsync);
            await CaseAsync("离开通知页不弹旧编辑器", NotificationCancelAsync);
            await CaseAsync("过期卡片不发送菜单命令", StaleCardAsync);
            await CaseAsync("封面选择取消不接受迟到上传", CoverPickRaceAsync);
            await CaseAsync("封面列表及绑定跟随刷新", CoverRefreshAsync);
            await CaseAsync("封面确认复用对话框且取消安全", CoverCancelAsync);
            await CaseAsync("封面写入期间等待关闭", CoverWriteAsync);
            await CaseAsync("封面结果不明须先刷新", CoverUncertainAsync);
            await CaseAsync("封面迟到图片不复活", CoverLateImageAsync);
            await CaseAsync("完整返回历史和主页按钮", ShellHistoryAsync);
            await CaseAsync("收藏入口显隐、跳转及取消最后一项", FavoritesNavigationAsync);
            await CaseAsync("收藏页聚合所有在线账号与分账号筛选", FavoriteAccountsAsync, 30);
            await CaseAsync("收藏刷新、切服和退出的迟到结果隔离", FavoritesRaceAsync);
            await CaseAsync("删除确认、失败和成功保持列表位置", DeletePreservesListAsync);
            await CaseAsync("删除与分页、查询切换交错", DeletePagingRaceAsync);
            await CaseAsync("整剧删除清除后代并保留新查询", DeleteContainerAsync);
            await CaseAsync("详情封面删除保持其他货架与播放目标", DeleteDetailCardAsync);
            await CaseAsync("播放返回主页不补播入场", HomePlaybackReturnAsync, 30);
            // 2026-10-11 起该用例多四帧分区截图（播放行为分类的资源筛选、截图卡滚进视口各两帧），12 秒的
            // 默认限时不够了 —— 与视频基础输出用例同款理由（多轮控件切换与截图设 45 秒）。
            await CaseAsync("Anime4K 固定配置组选择与恢复（宽窗）", () => SettingsVideoAvailabilityAsync(1100, anime4KOnly: true), 30, section: ShellProbeSections.Shaders);
            await CaseAsync("Anime4K 固定配置组选择与恢复（窄窗）", () => SettingsVideoAvailabilityAsync(760, anime4KOnly: true), 30, section: ShellProbeSections.Shaders);
            await CaseAsync("设置分类动效与快速切换", SettingsEntranceAsync, 45);
            await CaseAsync("快捷键搜索、改绑、空态与恢复（宽窗）", () => SettingsShortcutSearchAsync(1100), 60, section: ShellProbeSections.Shortcuts);
            await CaseAsync("快捷键搜索、改绑、空态与恢复（窄窗）", () => SettingsShortcutSearchAsync(760), 60, section: ShellProbeSections.Shortcuts);
            await CaseAsync("新增快捷键命令、失败回执与队列隔离", PlayerAdditionalShortcutsAsync, 30, section: ShellProbeSections.Shortcuts);
            await CaseAsync("弹幕设置、凭据、显隐、快捷键与关于（宽窗）", () => SettingsDanmakuAsync(1100), 60, section: ShellProbeSections.Settings);
            await CaseAsync("弹幕设置、凭据、显隐、快捷键与关于（窄窗）", () => SettingsDanmakuAsync(760), 60, section: ShellProbeSections.Settings);
            await CaseAsync("弹幕输入栏、发送回执与截断搜索", DanmakuSendAsync, 60, section: ShellProbeSections.Playback);
            await CaseAsync("播放弹幕面板与过期交互（宽窗）", () => DanmakuPanelAsync(1100), 30, section: ShellProbeSections.Playback);
            await CaseAsync("播放弹幕面板与过期交互（窄窗）", () => DanmakuPanelAsync(360), 30, section: ShellProbeSections.Playback);
            await CaseAsync("播放控制条宽窄布局与负向溢出", PlayerTransportAsync, 30, section: ShellProbeSections.Playback);
            await CaseAsync("集成跳过按钮单行、缩放、点击与键盘", PlayerSkipAsync, 45);
            await CaseAsync("三处关键词拖拽优先级与弹簧（宽窗）", () => SettingsPriorityFiltersAsync(1100), 45, section: ShellProbeSections.Settings);
            await CaseAsync("三处关键词拖拽优先级与弹簧（窄窗）", () => SettingsPriorityFiltersAsync(760), 45, section: ShellProbeSections.Settings);
            await CaseAsync("从属设置显隐动画与偏好恢复（宽窗）", () => SettingsConditionalRowsAsync(1100), 45, section: ShellProbeSections.Settings);
            await CaseAsync("从属设置显隐动画与偏好恢复（窄窗）", () => SettingsConditionalRowsAsync(760), 45, section: ShellProbeSections.Settings);
            // 主页卡新增一行「主页媒体库位置」（2026-10-16 从界面卡挪来、同时改名），宽窄两帧截图 + 三档写回。
            await CaseAsync("主页卡上的主页媒体库位置三档", SettingsHomeLibraryPlacementAsync, 30);
            // 设置 → 界面新增一行「开机自启」：拨开关写的是机器上的启动项（假实现），办不到时整行不显示，
            // 另留宽窄两帧截图。
            await CaseAsync("界面卡上的开机自启开关", SettingsStartupRegistrationAsync, 30);
            // 每档包含多轮真实控件切换与截图，单独计时，原有用例仍保持 12 秒超时。
            await CaseAsync("视频基础输出显隐与偏好恢复（宽窗）", () => SettingsVideoAvailabilityAsync(1100), 45, section: ShellProbeSections.Settings);
            await CaseAsync("视频基础输出显隐与偏好恢复（窄窗）", () => SettingsVideoAvailabilityAsync(760), 45, section: ShellProbeSections.Settings);
            await CaseAsync("自定义 mpv.conf 显隐与恢复（宽窗）", () => SettingsVideoAvailabilityAsync(1100, true), 30, section: ShellProbeSections.Settings);
            await CaseAsync("自定义 mpv.conf 显隐与恢复（窄窗）", () => SettingsVideoAvailabilityAsync(760, true), 30, section: ShellProbeSections.Settings);
            await CaseAsync("非管理员账号隐藏服务器管理入口", SettingsAdministrationGateAsync, 45);
            await CaseAsync("多服务器设置按服务器分组、默认收起与权限隐藏", MultiServerSettingsGroupsAsync, 45);
            await CaseAsync("飞牛非默认来源读取保存与改密离页确认", FnNasExplicitSourceAsync, 45);
            await CaseAsync("服务器页聚合模式开关、在线账号与凭据保留", ServersPageSummaryAsync, 45);
            await CaseAsync("Jellyfin 控制台只走外层路由并落到 Jellyfin Web 地址", JellyfinConsoleNavigationAsync, 45);
            await CaseAsync("Jellyfin 通用对话框能力显隐", JellyfinDialogCapabilitiesAsync, 30);
            await CaseAsync("MoviePilot 订阅分区、封面与管理操作", MoviePilotSubscriptionsAsync);
            // 这一条从 2026-10-12 起多两帧「下方三段」的截图与一段等待（演职员／相关推荐／相似作品那三排卡片
            // 的图是异步抓的），12 秒的默认限时不够 —— 同视频基础输出用例的理由。
            await CaseAsync("MoviePilot 订阅详情、入库缺集与编辑", MoviePilotSubscriptionDetailAsync, 30);
            await CaseAsync("MoviePilot 电影订阅详情不再有入库面板与空季卡", MoviePilotMovieSubscriptionDetailAsync);
            await CaseAsync("MoviePilot 日本动画订阅的 AniBT 入口", MoviePilotAnimeSubscriptionDetailAsync);
            await CaseAsync("MoviePilot 订阅切服、旧确认与离页取消", MoviePilotSubscriptionIdentityAsync);
            await CaseAsync("MoviePilot 海报刷新重试与原地更新", MoviePilotSubscriptionPosterRefreshAsync);
            await CaseAsync("MoviePilot 订阅同号季隔离、播放恢复与最窄布局", MoviePilotSubscriptionFilesRecoveryAsync, 25);
            await CaseAsync("MoviePilot 取消后重新订阅同步新编号", MoviePilotSubscriptionRecreateAsync, 25);
            await CaseAsync("MoviePilot 详情操作待核对与恢复", MoviePilotSubscriptionReviewAsync);
            await CaseAsync("MoviePilot 媒体详情、剧集组浏览和真实资源入口", MoviePilotMediaBrowsingAsync);
            await CaseAsync("MoviePilot 按季订阅、三种模式与取消草稿", MoviePilotSeasonSelectionAsync);
            await CaseAsync("MoviePilot 剧集组与入库状态迟到隔离", MoviePilotMediaReadRacesAsync);
            // 同时检查季折叠栏的五套主题、摘要恢复与展开/折叠实屏截图。
            await CaseAsync("MoviePilot 背景按页宽等比、下滑遮罩与纸面", MoviePilotMediaBackgroundAsync, 30);
            await CaseAsync("MoviePilot 按季提交部分失败与核对", MoviePilotSeasonPartialSubmitAsync);
            await CaseAsync("MoviePilot 字幕列表、保存路径、取消与站点变更", MoviePilotMediaSubtitlesAsync);
            await CaseAsync("季页面右侧播放图标与原播放路由", SeasonTrailingPlayAsync);
            await CaseAsync("旧登录目录不覆盖新身份", ShellIdentityAsync);
            await CaseAsync("切服逆序完成不退旧登录", ShellSwitchRaceAsync);
            await CaseAsync("目录首次失败可重试", ShellDirectoryRetryAsync);
            await CaseAsync("日志淘汰后显示空态", DiagnosticsEmptyAsync);
            await CaseAsync("对话框构造自检路径", DialogConstructionAsync);
            SaveScope();
            ExitCode = _checks.Count > 0 && _checks.All(check => check.Passed) ? 0 : 1;
        }
        catch (Exception error)
        {
            _checks.Add(new Check("探针宿主", false, error.ToString()));
        }
        finally
        {
            Save();
            SaveScope();
            foreach (var settingsWindow in _shortcutProbeWindows) settingsWindow.Close();
            _window?.Dispose();
            Application.Current.Exit();
        }
    }

    private async Task CaseAsync(string name, Func<Task> run, int timeoutSeconds = 12, string? section = null)
    {
        var area = section ?? CaseSections.GetValueOrDefault(run.Method.Name);
        if (area is null)
        {
            _checks.Add(new Check("探针分区", false, $"用例「{name}」没有分区；请在 ShellNavigationProbe.Sections.cs 登记或显式传入 section"));
            Save();
            return;
        }
        if (_sections is not null && !_sections.Contains(area))
        {
            _skipped.Add(new Skipped(area, name));
            SaveScope();
            return;
        }
        try
        {
            await run().WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
            _checks.Add(new Check(name, true, ""));
        }
        catch (Exception error) { _checks.Add(new Check(name, false, error.ToString())); }
        Save();
    }

    private void Save() => File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, "shell-probe.json"),
        JsonSerializer.Serialize(_checks, new JsonSerializerOptions { WriteIndented = true }));

    private void SaveScope()
    {
        var selected = _sections is null ? "全量" : string.Join(", ", _sections.OrderBy(section => section, StringComparer.Ordinal));
        var unselected = _sections is null
            ? "无"
            : string.Join(", ", ShellProbeSections.All.Where(section => !_sections.Contains(section)));
        if (unselected.Length == 0) unselected = "无";
        var lines = new List<string>
        {
            $"执行区域：{selected}",
            $"未执行区域：{unselected}",
            $"未执行检查：{_skipped.Count}",
        };
        foreach (var group in _skipped.GroupBy(item => item.Section, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            lines.Add($"[{group.Key}]");
            lines.AddRange(group.Select(item => "- " + item.Name));
        }
        File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, "shell-probe-scope.txt"),
            string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(false));
    }

    private Fixture CreateFixture(HttpMessageHandler? moviePilot = null, HttpMessageHandler? aniBt = null) => new(Path.Combine(_options.Paths.Root, "fixtures", Guid.NewGuid().ToString("N")), moviePilot, aniBt);
    private static TaskCompletionSource<T> Pending<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static object? Call(object target, string name, params object?[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static void Field(object target, string name, object? value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static T Get<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static async Task SettleAsync() => await Task.Delay(80);
    private static async Task UntilAsync(Func<bool> ready)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (ready()) return;
            await Task.Delay(25);
        }
        throw new TimeoutException("未等到预期状态");
    }
    private static async Task LayoutAsync(FrameworkElement element)
    {
        await UntilAsync(() =>
        {
            element.UpdateLayout();
            return element.XamlRoot is not null && element.ActualWidth > 0 && element.ActualHeight > 0;
        });
        await Task.Delay(150);
    }

    /// <summary>ScrollTo 是异步操作；按返回的关联号等完成，不把固定延时当成已经滚到目标。</summary>
    private static async Task ScrollProbeAsync(ScrollView scroll, double verticalOffset)
    {
        var completed = Pending<bool>();
        var seen = new HashSet<int>();
        var correlation = -1;
        void OnCompleted(ScrollView sender, ScrollingScrollCompletedEventArgs args)
        {
            seen.Add(args.CorrelationId);
            if (args.CorrelationId == correlation) completed.TrySetResult(true);
        }
        scroll.ScrollCompleted += OnCompleted;
        try
        {
            correlation = scroll.ScrollTo(0, verticalOffset, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
            if (correlation != -1 && !seen.Contains(correlation))
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scroll.UpdateLayout();
        }
        finally { scroll.ScrollCompleted -= OnCompleted; }
    }

    private void ResizeProbeClient(int width, int height)
    {
        var scale = _root.XamlRoot.RasterizationScale;
        var window = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_window!.Handle));
        window.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
    }

    private async Task SaveProbeScreenAsync(string name)
    {
        if (_window is null) throw new InvalidOperationException("探针窗口尚未创建");
        var topMost = _window.TopMost;
        try
        {
            _window.TopMost = true;
            await Task.Delay(80);
            await CompositionPlaybackProbe.SaveScreenAsync(_window, Path.Combine(_options.Paths.LogDirectory, name));
        }
        finally { _window.TopMost = topMost; }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private sealed record Check(string Name, bool Passed, string Detail);
    private sealed record Skipped(string Section, string Name);

    private sealed class Fixture : IDisposable
    {
        public AppSettings Settings { get; } = new();
        public FakeTransport Transport { get; } = new();
        public FakeActions Actions { get; } = new();
        public EmbySession Session { get; }
        public ISettingsService SettingsService { get; }
        public EmbyImageStore Images { get; }
        public FakeCapabilities Capabilities { get; } = new();
        public ISystemLauncher Launcher { get; } = new FakeLauncher();
        public FakeStartup Startup { get; } = new FakeStartup();

        /// <summary>
        /// 探针跑在自己的线程上、没有 UI 队列，所以给一个「就地跑」的实现。<c>HomeViewModel</c> 的实时通道要
        /// 用它把通知排回页面那一头 —— 离线探针里那条通道本来就连不上（实时通道走真 WebSocket，而这里的
        /// FakeTransport 只管假的 HTTP），所以这个实现实际只保证「真要排的时候排得动」。
        /// </summary>
        public IUiDispatcher Dispatcher { get; } = new InlineDispatcher();

        public IServiceProvider Services { get; }
        public Fixture(string root, HttpMessageHandler? moviePilotTransport = null, HttpMessageHandler? aniBtTransport = null)
        {
            Settings.Servers.Clear();
            // 探针绝大多数场景要两台服务器同时在线；聚合模式在设置里默认关着，所以这里显式打开。
            // 非聚合模式的收口由单测与「服务器页」那一条探针单独核对。
            Settings.AggregateMode = true;
            Settings.Ui.PageSize = 20;
            Settings.Ui.ShowHomeBanner = false;
            var paths = new AppPaths(root);
            paths.EnsureCreated();
            var store = new SettingsStore(paths, PassthroughSecretProtector.Instance);
            var vault = new CredentialVault(PassthroughSecretProtector.Instance);
            SettingsService = new SettingsService(store, Settings);
            Session = new EmbySession(Settings, store, vault, DeviceIdentity.Create("offline-shell", "test"), Transport);
            Images = new EmbyImageStore(Session, Path.Combine(root, "images"));
            var moviePilot = new MoviePilotService(new MoviePilotClient(moviePilotTransport ?? new RejectNetwork()),
                new MoviePilotCredentials(PassthroughSecretProtector.Instance), Settings);
            Services = new ServiceCollection().AddSingleton(Session).AddSingleton(SettingsService).AddSingleton(paths)
                .AddSingleton(new Momoka.AniBt.AniBtClient(aniBtTransport ?? new RejectNetwork()))
                .AddSingleton<Momoka.AniBt.AniBtService>()
                // 按接口注册，不按 FakeCapabilities：页面解析的是 IServerCapabilities，而泛型参数写死之后
                // 注册进去的就是那个具体类型。
                .AddSingleton(Images).AddSingleton<IServerCapabilities>(Capabilities).AddSingleton(vault).AddSingleton(moviePilot)
                .AddSingleton<IShellActions>(Actions).AddSingleton(Launcher).AddSingleton<IStartupRegistration>(Startup)
                .AddSingleton(Dispatcher)
                .BuildServiceProvider();
        }
        public async Task SelectAsync(string host = "a.invalid", string user = "user", MediaServerKind kind = MediaServerKind.Emby)
        {
            var server = new ServerProfile { Name = host, Url = $"https://{host}/media", ServerKind = kind };
            var account = new AccountProfile
            {
                Username = user,
                UserId = user,
                ProtectedAccessToken = PassthroughSecretProtector.Instance.Protect("offline-fixture-token")
            };
            server.Accounts.Add(account);
            Settings.Servers.Add(server);
            Require(await Session.TryRestoreAsync(server, account, CancellationToken.None), "假身份恢复失败");
        }
        public LibraryViewModel Library(string term = "first")
        {
            var vm = new LibraryViewModel();
            vm.Attach(LibraryRequest.Search(Services, term), Actions, SettingsService, Session, Images, Capabilities);
            return vm;
        }
        public DetailViewModel Detail()
        {
            var vm = new DetailViewModel();
            vm.Attach(DetailRequest.For(Services, Series()), Actions, SettingsService, Session, Images);
            return vm;
        }
        public void Dispose()
        {
            Session.Dispose();
            (Services as IDisposable)?.Dispose();
        }
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new InvalidOperationException("离线探针禁止真实网络"));
    }

    /// <summary>
    /// <see cref="IUiDispatcher"/> 的就地版：两个成员都当场执行。探针线程就是它自己的「UI 线程」，没有队列可排。
    /// </summary>
    private sealed class InlineDispatcher : IUiDispatcher
    {
        public bool Post(Action work)
        {
            work();
            return true;
        }

        public void Run(Action work) => work();
    }

    private sealed class FakeTransport : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>?>? Reply { get; set; }
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Require(request.RequestUri!.Host.EndsWith(".invalid", StringComparison.Ordinal), "探针拒绝真实网络目标");
            Requests.Add(request.Method.Method + " " + request.RequestUri.PathAndQuery);
            return Reply?.Invoke(request, cancellationToken) ?? Task.FromResult(Standard(request));
        }
        internal static HttpResponseMessage Standard(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/Views", StringComparison.Ordinal)) return Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 });
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal)) return Json(new { ServerName = "离线服务器", Version = "4.10.1.0" });
            if (path.Contains("/Images/", StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (path.EndsWith("/Seasons", StringComparison.Ordinal)) return Json(new { Items = new[] { Season("a"), Season("b") } });
            if (path.EndsWith("/Episodes", StringComparison.Ordinal))
            {
                var season = Query(request, "SeasonId") ?? "a";
                return Json(new { Items = new[] { Episode(season) } });
            }
            if (path.EndsWith("/Similar", StringComparison.Ordinal)) return Items(0, 0);
            if (path.EndsWith("/Items/show", StringComparison.Ordinal)) return Json(Series());
            if (path.Contains("/Items/ep-", StringComparison.Ordinal)) return Json(Episode(path.Split("ep-")[1]));
            if (path.Contains("PlayedItems", StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.NoContent);
            if (path.EndsWith("/Items", StringComparison.Ordinal)) return Items(2, 2);
            return Json(Array.Empty<object>());
        }
    }

    private static string? Query(HttpRequestMessage request, string name) => request.RequestUri!.Query.TrimStart('?')
        .Split('&').Select(value => value.Split('=', 2)).Where(pair => pair.Length == 2 && pair[0].Equals(name, StringComparison.OrdinalIgnoreCase))
        .Select(pair => Uri.UnescapeDataString(pair[1])).FirstOrDefault();
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Items(int count, int total, string prefix = "item") => Json(new
    {
        Items = Enumerable.Range(0, count).Select(index => new EmbyItem { Id = prefix + index, Name = prefix + index, Type = EmbyItemType.Movie }).ToArray(),
        TotalRecordCount = total
    });
    private static EmbyItem Series() => new() { Id = "show", Name = "离线测试剧集", Type = EmbyItemType.Series };
    private static EmbyItem Season(string id) => new() { Id = id, Name = "第 " + id + " 季", Type = EmbyItemType.Season, SeriesId = "show", IndexNumber = id == "a" ? 1 : 2 };
    private static EmbyItem Episode(string season) => new()
    { Id = "ep-" + season, Name = "离线单集 " + season, Type = EmbyItemType.Episode, SeriesId = "show", SeasonId = season, IndexNumber = 1 };

    private sealed class FakeCapabilities : IServerCapabilities
    {
        public MediaServerKind ServerKind { get; set; }
        public bool IsConnected { get; set; } = true;

        /// <summary>多来源探针要能拨「通用界面」，好核服务端管理入口整组收起。</summary>
        public ServerInterfaceFlavor InterfaceFlavor { get; set; } = ServerInterfaceFlavor.Emby;

        /// <summary>假服务器报的账号身份。设置页左栏那一组的显隐靠它拨；可写，好让一条探针走两种账号。</summary>
        public bool? IsAdministrator { get; set; } = true;

        /// <summary>服务器插件页；空表时设置左栏只有固定三段。</summary>
        public IReadOnlyList<ServerSettingsPluginPage> AdminPages { get; set; } = [];

        /// <summary>多服务器探针用的在线来源表；空表沿用单服务器路径。</summary>
        public IReadOnlyList<ServerSettingsSource> Sources { get; set; } = [];

        /// <summary>假服务器报的版本。照用户那一台 4.10.1.0 —— 排序里的「导演」（4.9.0.48 起）、
        /// 「上次发布集日期」（4.9.0.17 起）与筛选里的「重制」（4.9.0.42 起）都靠它才放得出来。</summary>
        public Version? ServerVersion => new(4, 10, 1, 0);
        public Task<IReadOnlyList<ServerSettingsPluginPage>> GetAdminMenuPagesAsync(CancellationToken token = default) =>
            Task.FromResult(AdminPages);
        public Task ProbeAsync(CancellationToken token = default) => Task.CompletedTask;
    }
    private sealed class FakeLauncher : ISystemLauncher
    {
        public void OpenFolder(string path) => throw new InvalidOperationException("探针不打开外部程序");
        public void OpenUrl(string url) => throw new InvalidOperationException("探针不打开外部程序");
    }

    /// <summary>
    /// 假的启动项：探针绝不能真写 HKCU 里那条开机自启 —— 那会在跑探针这台机器上留下一条真的启动项。拨一下只
    /// 记在内存里，并照真实实现那样「写完读回来」，好让用例验得动「写进去了没有」这条线。
    /// </summary>
    private sealed class FakeStartup : IStartupRegistration
    {
        /// <summary>这一台机器上办不办得到。置假就是打包安装那一档，设置页上那一行该整个不显示。</summary>
        public bool Available { get; set; } = true;

        /// <summary>置真时 <see cref="Apply"/> 什么都不改，模拟组策略或安全软件挡下 —— 走「开关拨回去」那条路。</summary>
        public bool Refuse { get; set; }

        public bool IsEnabled { get; private set; }

        /// <summary>拨过哪几档，按次序。探针拿它验「拨一下真写下去了没有」。</summary>
        public List<bool> Requests { get; } = [];

        public bool Apply(bool enabled)
        {
            Requests.Add(enabled);
            if (Refuse) return false;

            IsEnabled = enabled;
            return true;
        }
    }

    private sealed class FakeActions : IShellActions
    {
        public int Plays { get; private set; }
        public int Opened { get; private set; }
        public Action<EmbyItem, IReadOnlyList<EmbyItem>?>? CapturePlay { get; set; }
        public void OpenItem(EmbyItem item) => Opened++;
        public Task PlayAsync(EmbyItem item, EmbyItem? parent = null, PlaybackChoice? choice = null, IReadOnlyList<EmbyItem>? episodes = null)
        {
            Plays++;
            if (CapturePlay is { } capture) { capture(item, episodes); return Task.CompletedTask; }
            throw new InvalidOperationException("探针禁止播放");
        }
        public void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational) { }
        public void ReportMarkedWatched(EmbyItem item) { }
        public Action<EmbyItem?>? FavoritesChanged { get; set; }
        public void RefreshFavorites(EmbyItem? changed = null) => FavoritesChanged?.Invoke(changed);
        public bool TryOpenLibrary(string id, EmbySessionScope? source = null) => false;
        public void OpenGenre(string genre, EmbySessionScope? source = null) { }
        public void OpenRow(HomeLayout.HomeRowTarget target, EmbySessionScope? source = null) { }
        public void OpenSignIn(ServerProfile? server = null, bool newAccount = false) { }
        public (ServerProfile Server, AccountProfile? Account)? SwitchedProfile { get; private set; }
        public Task SwitchProfileAsync(ServerProfile server, AccountProfile? account)
        {
            SwitchedProfile = (server, account);
            return Task.CompletedTask;
        }
        public void SetTitleStrip(TitleStrip strip) { }
        public bool MoviePilotEnabled => false;
        public event EventHandler? MoviePilotAvailabilityChanged { add { } remove { } }
        public void SearchMoviePilotVersions(EmbyItem item) { }
        public void SearchAniBtVersions(EmbyItem item) { }
        public void ShowMoviePilotReorganize(Momoka.MoviePilot.MoviePilotTransferContext context) { }
        public void ShowAniBtScrape(EmbySessionScope scope, EmbyItem item) { }
        public void ShowMissingEpisodes(EmbyItem item, IReadOnlyList<EmbyItem> missing) { }
    }
}
