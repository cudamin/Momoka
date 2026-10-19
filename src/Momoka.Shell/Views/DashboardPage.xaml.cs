using System.Text.Json;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Platform;
using Momoka.Theming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;

namespace Momoka.Shell.Views;

/// <summary>导航到网页控制台时传入的容器。</summary>
/// <param name="Route">
/// 控制台里的哪一页（<c>dashboard</c>、<c>dashboard/settings</c>、<c>users</c>……）。空表示产品默认页。
/// </param>
/// <param name="LoadContent">
/// 真正启动内嵌浏览器。离线探针只验证左栏与宽窄布局时传 false —— WebView2 在探针窗口里的合成会让
/// 整页截不到内容，也会在退出时留下一个 CLR 崩溃；不启动它，探针读的仍然是真的页面与真的导航。
/// </param>
/// <param name="Immersive">
/// 不给这一页留 Momoka 自己的标题、地址和按钮，也不留卡片的边距与边框：服务器设置页面直接用网页内容
/// 铺满设置窗口右半边。Emby 与 Jellyfin 都走这一档。
/// </param>
internal sealed record DashboardRequest(
    IServiceProvider Services, string? Route = null, bool LoadContent = true, bool Immersive = false,
    string? SourceKey = null);

/// <summary>
/// 需求 8：Emby／Jellyfin 网页端的控制台，用 <c>WebView2</c> 嵌在设置里，并且是已经登录好的状态。
/// <para>
/// The WebView2 assembly comes with the Windows App SDK, so there is no package reference to add; what the
/// app does have to bring is a browser profile directory of its own (see
/// <see cref="AppPaths.WebViewDirectory"/>) — a non-packaged app otherwise gets one next to the executable,
/// which in the publish directory need not be writable.
/// </para>
/// <para>
/// Sign-in is seeded, not typed: <see cref="EmbyWebConsole.SignInScript"/> runs at document start and leaves
/// this session's credentials exactly where the web client's own sign-in would have. The token therefore
/// never appears in a URL, in a log line or in the self-check report — only inside that script string.
/// </para>
/// <para>
/// 深浅跟着本应用当前的主题走，同样是文档开始脚本加一位浏览器设置 —— 见
/// <see cref="EmbyWebConsole.ThemeScript"/> 和 <see cref="StartAsync"/> 里那一段。
/// </para>
/// </summary>
public sealed partial class DashboardPage : Page, IShellContent, ISessionScopedPage
{
    private const string Category = "控制台";

    /// <summary>How long to wait for a page that neither loads nor fails before saying so.</summary>
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Long enough for the client's boot to have restored the seeded session before sampling it.</summary>
    private static readonly TimeSpan SampleDelay = TimeSpan.FromSeconds(2.5);

    private object? _request;
    private bool _loadContent = true;
    private bool _immersive;
    private ISystemLauncher? _launcher;
    private string _url = "";
    private bool _core;
    private bool _booted;
    private bool _seeded;
    private bool? _navigated;
    private string _error = "";
    private string _client = "尚未取样";
    private bool _settled;
    private bool _released;
    private EmbySessionScope? _scope;
    private string? _sourceKey;
    private readonly CancellationTokenSource _lifetime = new();
    private string _storageKey = EmbyWebConsole.StorageKey;

    /// <summary>
    /// 这一页第几次去载入控制台：导航进来算一次，每按一下「重新载入」再算一次。两条带延时的后续
    /// （<see cref="SampleAsync"/> 等 2.5 秒、<see cref="GiveUpAsync"/> 等 15 秒）醒来之后先对一下这个数，
    /// 不是自己那一轮就闭嘴走开。
    /// <para>
    /// 少了它，上一轮那个 15 秒的看门狗会把刚开始的这一轮判成失败：刷新把 <c>_settled</c> 放回 false，而它
    /// 只看这一个字段 —— 于是那一行读数写上「15 秒内没有结果」，而新的一趟才刚发出去一秒。
    /// </para>
    /// <para>
    /// 管不着的那一半照实写在这儿：<c>NavigationCompleted</c> 自己不带轮次（WebView2 那边只有 NavigationId，
    /// 要接就得从 NavigationStarting 一路记下来），所以一次被顶掉的导航仍然会以「失败」的身份回来。那一下屏上
    /// 是一句错话，下一轮真的完成时会盖回去。
    /// </para>
    /// </summary>
    private int _generation;

    /// <summary>页顶那一行读数的两半：控制台地址，和它此刻的载入状态。见 <see cref="Say"/>。</summary>
    private string _address = "尚未确定地址";
    private string _state = "";

    public DashboardPage()
    {
        InitializeComponent();
        Loaded += (_, _) => HomeMotion.Reveal(PageLayout);
        Unloaded += (_, _) => HomeMotion.Stop(PageLayout);
    }

    /// <summary>当前设置页选中项的路由；外层左栏换页时先改它，再改地址。</summary>
    private string _route = ServerSettingsMenu.DefaultRoute;

    /// <summary>
    /// True once there is nothing more to wait for: the console is up and sampled, or it has failed and said
    /// why. The self-check's <c>Settled</c> gate reads this, so every terminal path has to set it — including
    /// the ones that never reach the network.
    /// </summary>
    internal bool IsReady => _settled;

    /// <summary>
    /// What the self-check reports. <see cref="Client"/> is a sentence about the web client's own state,
    /// sampled out of the page; deliberately no token, no cookie and no user id in any of it.
    /// </summary>
    internal (bool Ready, string Url, bool Core, bool Seeded, bool? Navigated, string Error, string Client) Summary =>
        (_settled, _url, _core, _seeded, _navigated, _error, _client);

    internal string CurrentNavigationAddress => _url;

    internal string CurrentRoute => _route;

    private bool Live => !_released && _scope is { } scope
        && (SourceKey is null ? scope.IsCurrent : scope.IsAvailable);

    /// <summary>这一页绑定的服务器身份还在线吗；设置页在切换服务器后据此重建右半边。</summary>
    public bool IsCurrentSession => Live;

    /// <summary>这一页绑定的是哪一条在线身份；null 表示当前来源。</summary>
    public string? SourceKey => _sourceKey;

    public object? NavigationRequest => _request;

    /// <summary>
    /// 把地址和载入状态写到页顶那一行读数上（左边那一头，见 xaml 里那段说明）。两个都是机器串，等宽字里
    /// 它们是同一种东西，所以合成一行而不是各占一行。
    /// </summary>
    private void Say(string state)
    {
        _state = state;
        Readout.Text = (_address, _state) switch
        {
            ("", var only) => only,
            (var only, "") => only,
            var (address, status) => $"{address} · {status}"
        };
    }

    /// <summary>
    /// 外层设置栏换到另一个服务器页时走这里：同一台服务器、同一个 DashboardPage 就留在原地，
    /// 只让网页自己换路由。返回 false 表示这一页不适合接手（会话已换／已经释放），调用方仍走整页导航。
    /// Emby 与 Jellyfin 都暴露 <c>Emby.Page.show</c>，两边共用这条同文档切页的路。
    /// </summary>
    internal bool TryShowRoute(string route)
    {
        // 切到 Momoka 卡片时这一页已经 Release，Frame 里留着的只是外壳；再切回服务器页必须新建。
        if (!Live) return false;
        if (ServerSettingsMenu.NormalizeRoute(route) is not { } normalized
            || !ServerSettingsMenu.IsSafeRoute(normalized)) return false;

        NavigateToRoute(normalized);
        return true;
    }

    private void NavigateToRoute(string route)
    {
        if (!Live || _scope is not { } scope || !ServerSettingsMenu.IsSafeRoute(route)) return;

        _generation++;
        _settled = false;
        _navigated = null;
        _error = "";
        _route = route;
        _url = EmbyWebConsole.RouteUrl(scope.Connection, route);
        _address = _url;

        NoticePanel.Visibility = Visibility.Collapsed;
        Web.Visibility = Visibility.Visible;

        if (Web.CoreWebView2 is not { } core)
        {
            Say("正在启动内嵌浏览器…");
            return;
        }

        Say("正在载入服务器设置…");

        // 首个文档已经启动好之后，切页走网页自己的路由：不重载整页，也就不会再闪一次启动画面。
        // Emby 与 Jellyfin 的网页端都提供 Emby.Page.show；首次载入（或上一趟整页还没完成）仍然用整页导航。
        if (_booted)
            _ = NavigateInlineAsync(core, route, _url);
        else
            core.Navigate(_url);
        _ = GiveUpAsync(_generation);
    }

    /// <summary>
    /// 在当前文档里换页。脚本返回 <c>false</c>（网页还没启动好）或调用失败时落回整页导航；
    /// 网页路由自己失败时，脚本里的 catch 也会落回整页地址。
    /// </summary>
    private async Task NavigateInlineAsync(CoreWebView2 core, string route, string url)
    {
        var generation = _generation;
        try
        {
            var result = await core.ExecuteScriptAsync(EmbyWebConsole.RouteScript(route, url));
            if (_released || generation != _generation) return;

            if (!string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
            {
                core.Navigate(url);
                return;
            }

            // Emby.Page.show 已经把视图切过去了；等待焦点和内容落一帧再把这行读数定下来。
            _navigated = true;
            await Task.Delay(350, _lifetime.Token);
            if (_released || generation != _generation) return;
            _settled = true;
            Say("已载入");
        }
        catch (OperationCanceledException) when (_released || _lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (_released || generation != _generation) return;
            Log.Debug(Category, $"网页内切页失败，改走整页：{error.Message}");
            try { core.Navigate(url); } catch (Exception) { }
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not DashboardRequest request)
        {
            Log.Warn(Category, "导航参数缺失");
            return;
        }

        Attach(request);
    }

    /// <summary>
    /// 页面真正开始干活的那一段。单独拎出来是给离线探针用的：探针把这一页直接挂到自己的窗口里，
    /// 不经过 Frame（Frame 的导航转场在探针窗口里会把整页留在不透明度 0），生产路径仍走
    /// <see cref="OnNavigatedTo"/>。
    /// </summary>
    internal void Attach(DashboardRequest request)
    {
        _request = request;
        _loadContent = request.LoadContent;
        _immersive = request.Immersive;
        Tag = "dashboard";
        _generation++;

        if (_immersive)
        {
            HeaderPanel.Visibility = Visibility.Collapsed;
            PageLayout.Margin = new Thickness(0);
            PageLayout.RowSpacing = 0;
            WebFrame.Margin = new Thickness(0);
            WebFrame.CornerRadius = new CornerRadius(0);
            WebFrame.BorderThickness = new Thickness(0);
        }

        var services = request.Services;
        _launcher = services.GetRequiredService<ISystemLauncher>();
        var session = services.GetRequiredService<EmbySession>();
        _sourceKey = request.SourceKey;

        // 多服务器分组给出的是这一台自己的来源键；单服务器与旧调用仍走「当前来源」。
        if (request.SourceKey is { Length: > 0 } sourceKey)
        {
            _scope = session.Sources.FirstOrDefault(source => string.Equals(source.Key, sourceKey, StringComparison.Ordinal));
            if (_scope is not { IsAvailable: true })
            {
                _url = "";
                _address = "这台服务器已离线";
                Say("");
                Show("这台服务器已经退出登录，请刷新设置后重试。");
                _settled = true;
                return;
            }
        }
        else
        {
            // Nothing to embed and nothing to seed: the console is per-user, and this app has no user yet.
            if (session.Connection is not null) _scope = session.Capture();
        }

        if (_scope?.Connection is not { } connection)
        {
            _url = "";
            _address = "尚未登录服务器";
            Say("");
            Show("先在「服务器」里登录管理员账号，服务器的控制台就会显示在这里。");
            _settled = true;
            return;
        }

        try
        {
            _storageKey = EmbyWebConsole.CredentialStorageKey(connection);
            var requested = ServerSettingsMenu.NormalizeRoute(request.Route);
            _route = requested is not null && ServerSettingsMenu.IsSafeRoute(requested)
                ? requested
                : JellyfinServerSettingsMenu.DefaultRouteFor(connection.ServerKind);
            _url = EmbyWebConsole.RouteUrl(connection, _route);
        }
        catch (ArgumentException)
        {
            _settled = true;
            Say("服务器返回的配置页面地址无效。");
            return;
        }
        _address = _url;
        Say("正在启动内嵌浏览器…");
        _ = StartAsync(connection, EmbyWebConsole.ProfileDirectory(connection,
            services.GetRequiredService<AppPaths>().WebViewDirectory));
    }

    /// <summary>
    /// Brings the control up on its own profile, registers the sign-in seed, then navigates.
    /// <para>
    /// Order matters: the seed has to be registered before the navigation that needs it, because
    /// <c>AddScriptToExecuteOnDocumentCreatedAsync</c> only affects documents created after it returns.
    /// Every <c>await</c> is followed by a <see cref="_released"/> check — the settings window can be closed
    /// while the browser is still starting, and continuing then would touch a closed control.
    /// </para>
    /// </summary>
    private async Task StartAsync(EmbyConnection connection, string profile)
    {
        try
        {
            if (_scope is null) return;
            var system = await _scope.ExecuteAsync((client, token) => client.GetSystemInfoAsync(token), _lifetime.Token);
            if (!Live) return;
            if (system.IsRestricted)
            {
                _url = "";
                _settled = true;
                Show("需要服务器管理员权限。");
                return;
            }
            connection = _scope.Connection;

            // 离线探针只读左栏与落点，不把 WebView2 拉进探针窗口（见 DashboardRequest.LoadContent）。
            if (!_loadContent)
            {
                _settled = true;
                Say("离线探针不载入网页");
                return;
            }

            Directory.CreateDirectory(profile);

            var environment = await CoreWebView2Environment
                .CreateWithOptionsAsync("", profile, new CoreWebView2EnvironmentOptions());
            if (!Live) return;

            await Web.EnsureCoreWebView2Async(environment);
            if (!Live || Web.CoreWebView2 is not { } core) return;
            _core = true;

            // 控制台跟着当前主题的深浅走，两半都在这儿：这一位是浏览器的 prefers-color-scheme，另一半是
            // 下面那段脚本把网页端的主题设成 auto（见 EmbyWebConsole.ThemeScript）。设在导航之前，因为
            // 网页端是在启动时读一次深浅来挑主题的。
            //
            // 只在这里读一次 ThemeHost.Current，不订阅换主题的通知：主题方块在设置的「界面」那张卡上，
            // 而这一页和那张卡在设置里是互斥的两个选项 —— 换主题必然先离开这一页（离开就 Release），
            // 回来时 SettingsPage.ShowHosted 是重新导航一次，于是这一行本来就会重新跑。
            core.Profile.PreferredColorScheme = ThemeHost.Current.IsDark
                ? CoreWebView2PreferredColorScheme.Dark
                : CoreWebView2PreferredColorScheme.Light;

            Web.NavigationCompleted += OnNavigationCompleted;
            core.NewWindowRequested += OnNewWindowRequested;

            // 缺少公开服务器 ID 时保留网页自己的登录流程，不构造一个猜测的 Jellyfin 身份。
            if (!connection.IsJellyfin || !string.IsNullOrWhiteSpace(connection.ServerId))
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(EmbyWebConsole.SignInScript(connection));
                if (!Live) return;
                _seeded = true;
            }

            if (!connection.IsJellyfin)
                await core.AddScriptToExecuteOnDocumentCreatedAsync(
                    EmbyWebConsole.ThemeScript(connection.ApiBase, connection.UserId));
            if (!Live) return;

            // 网页端自己的背景、文字与强调色也跟 Momoka：只改这个 WebView 的样式，不写服务器设置，
            // 手机和其他客户端不受影响。背景取「窗口」色而不是「面」色 —— 设置页的画布就是窗口色，
            // 网页铺在它上面才不会在右半边露出一块更亮的矩形；卡片仍是面色的那一档。
            // 两家的类名与主题机制不同，各用自己的一份脚本，不混用。
            var colors = ThemeHost.Current.Colors;
            // 导航前后的空白也取窗口色，网页还没画出来时不闪一块白。
            Web.DefaultBackgroundColor = ThemeHost.ToColor(colors.Window);
            if (!connection.IsJellyfin)
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(
                    EmbyWebConsole.ThemeVariablesScript(colors.Window, colors.Text, colors.Accent, colors.TextOnAccent));
                if (!Live) return;
                await core.AddScriptToExecuteOnDocumentCreatedAsync(EmbyWebConsole.NativeSkinScript(colors));
                if (!Live) return;
            }
            else
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(EmbyWebConsole.JellyfinNativeSkinScript(colors));
                if (!Live) return;
            }

            // 左栏已经是本应用画的那一份；网页端自己的抽屉收起来。Emby 与 Jellyfin 的抽屉类名不同，
            // 各用一段脚本：Emby 那套还顺手收掉顶栏那颗开抽屉的按钮，Jellyfin 则把永久抽屉与 MUI
            // 顶栏让出的空间还回去。
            await core.AddScriptToExecuteOnDocumentCreatedAsync(connection.IsJellyfin
                ? EmbyWebConsole.HideJellyfinDrawerScript()
                : EmbyWebConsole.HideDrawerScript());
            if (!Live) return;

            Say("正在载入服务器设置…");
            core.Navigate(_url);
            _ = GiveUpAsync(_generation);
        }
        catch (OperationCanceledException) when (_released || _lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            // Most likely no WebView2 runtime on the machine. Say so instead of showing an empty box.
            _error = error.Message;
            _settled = true;
            Log.Warn(Category, $"内嵌控制台启动失败：{error.Message}");
            if (!_released) Show($"内嵌浏览器没能启动：{error.Message}\n可以点右上角「用浏览器打开」，用系统浏览器看同一个页面。");
        }
    }

    /// <summary>
    /// Records how the navigation ended and, when it worked, asks the page what the web client made of the
    /// seeded credentials. A failure is reported rather than retried: retrying an unreachable server just
    /// makes the self-check wait longer for the same answer.
    /// </summary>
    private void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_released) return;

        _navigated = e.IsSuccess;
        if (e.IsSuccess)
        {
            // 网页已经启动过一次：之后外层栏的切页就可以走 Emby.Page，不再整页重载。
            _booted = true;
            Say("已载入");
            _ = SampleAsync(_generation);
            return;
        }

        _error = e.WebErrorStatus.ToString();
        _settled = true;
        Log.Warn(Category, $"控制台页面没能打开：{e.WebErrorStatus}");
        Show($"打不开 {_url}\n（{e.WebErrorStatus}）");
    }

    /// <summary>
    /// A link the console wants to open in a new window — its 「帮助」 entries, mostly. Hands it to the
    /// system browser: a second WebView with no chrome is a dead end for the person looking at it.
    /// </summary>
    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        Open(e.Uri);
    }

    /// <summary>
    /// One look at what the web client ended up doing with the seeded session: which route it settled on,
    /// which theme it resolved to, and whether it has a server record. Jellyfin receives its server id in
    /// the seed, so this record is configuration evidence, not proof of authentication. No credential is returned.
    /// <para>
    /// 主题读的是 <c>&lt;html&gt;</c> 上那个 <c>theme-*</c> 类名（<c>skinmanager.js</c> 挑好主题后就加在
    /// 那儿），所以它答的是「网页端最后真的用了哪套」，而不是「我们请求了哪套」—— 跟随主题这件事只有
    /// 这一句话能证明成或不成。
    /// </para>
    /// </summary>
    private async Task SampleAsync(int generation)
    {
        await Task.Delay(SampleDelay);
        if (_released || generation != _generation || Web.CoreWebView2 is null) return;

        try
        {
            var recordLabel = _scope?.Connection.IsJellyfin == true ? "服务器记录已配置 " : "服务器 id ";
            var json = await Web.ExecuteScriptAsync($$"""
                (function () {
                  try {
                    var data = JSON.parse(localStorage.getItem({{JsonSerializer.Serialize(_storageKey)}}) || "null");
                    var servers = data && Array.isArray(data.Servers) ? data.Servers : [];
                    var id = "未填";
                    for (var i = 0; i < servers.length; i++) if (servers[i] && servers[i].Id) { id = "已填"; break; }

                    var theme = "(无)";
                    var names = (document.documentElement.className || "").split(" ");
                    for (var j = 0; j < names.length; j++)
                      if (names[j].indexOf("theme-") === 0) { theme = names[j].substring(6); break; }

                    return "路由 " + (location.hash || "(无)") + "  ·  主题 " + theme + "  ·  " + {{JsonSerializer.Serialize(recordLabel)}} + id;
                  } catch (e) { return "取样出错：" + e; }
                })();
                """);
            if (_released || generation != _generation) return;

            var text = JsonSerializer.Deserialize<string>(json);
            _client = string.IsNullOrWhiteSpace(text) ? "没有取到" : text!;
            Say(_client);
        }
        catch (Exception error)
        {
            _client = $"取样失败：{error.Message}";
        }
        finally
        {
            // 上一轮的取样不许替这一轮宣布「问完了」—— 自检的 Settled 那一关读的就是这个字段。
            if (generation == _generation) _settled = true;
        }
    }

    /// <summary>
    /// The 自检 must not sit on a page that will never answer. An unreachable address can leave a navigation
    /// hanging for far longer than the walk's own budget, so past this point the page settles and says it did
    /// not finish rather than pretending it is still working.
    /// </summary>
    private async Task GiveUpAsync(int generation)
    {
        await Task.Delay(LoadTimeout);
        if (_released || _settled || generation != _generation) return;

        _settled = true;
        _client = $"{LoadTimeout.TotalSeconds:0} 秒内没有结果";
        Log.Warn(Category, $"控制台 {LoadTimeout.TotalSeconds:0} 秒内既没载入也没报错");
        Say("载入很慢，还在等服务器…");
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2 is not { } core || _url.Length == 0 || !Live) return;

        _navigated = null;
        _settled = false;
        _error = "";
        _generation++;
        NoticePanel.Visibility = Visibility.Collapsed;
        Web.Visibility = Visibility.Visible;
        Say("正在重新载入…");
        core.Navigate(_url);
        _ = GiveUpAsync(_generation);
    }

    private void OnOpenInBrowser(object sender, RoutedEventArgs e)
    {
        if (Live) Open(_url);
    }

    private void Open(string url)
    {
        if (_launcher is null || string.IsNullOrWhiteSpace(url)) return;

        try
        {
            _launcher.OpenUrl(url);
        }
        catch (Exception error)
        {
            Log.Warn(Category, $"打不开 {url}：{error.Message}");
        }
    }

    /// <summary>Puts a sentence where the console would have been, and takes the empty control off screen.</summary>
    private void Show(string message)
    {
        Notice.Text = message;
        NoticePanel.Visibility = Visibility.Visible;
        Web.Visibility = Visibility.Collapsed;
        Say("");
    }

    /// <summary>
    /// Lets go of the browser. Called both on navigating away and when the settings window is hidden, because
    /// a hidden window holding a live WebView2 is a browser process the user cannot see and did not ask for.
    /// The page instance is not reused afterwards — the hosted frame builds a new one on the next visit — so
    /// closing rather than merely stopping is the right cleanup.
    /// </summary>
    public void Release()
    {
        if (_released) return;
        _released = true;
        _lifetime.Cancel();
        _lifetime.Dispose();

        try
        {
            Web.NavigationCompleted -= OnNavigationCompleted;
            if (Web.CoreWebView2 is { } core)
                core.NewWindowRequested -= OnNewWindowRequested;
            Web.Close();
        }
        catch (Exception error)
        {
            Log.Debug(Category, $"关闭内嵌控制台时出错：{error.Message}");
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Release();
        base.OnNavigatedFrom(e);
    }
}
