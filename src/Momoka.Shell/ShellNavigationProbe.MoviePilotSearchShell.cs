using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.Interop;
using Momoka.Shell.Platform;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Updates;
using Windows.Foundation;
using Windows.Graphics;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 「在 MoviePilot 搜索版本」独立窗口的搜索区，与设置页 MoviePilot 卡上的「默认搜索站点」。
    /// <para>
    /// 三件事在这里量：窗口标题栏左上角那颗图标换成了全透明的（<see cref="BlankCaptionIcon"/>，大档不动）；
    /// 搜索框是 40 高、10 圆角的输入框、站点入口住在框里放大镜左边（与搜索页同一份 <see cref="SearchSiteSlot"/>），
    /// 框上框下不再有那两行字；设置页那一条只在连接可用时露出来，选完对搜索生效，断线收起、恢复后回来。
    /// </para>
    /// <para>
    /// 第二窗口与设置页都是真控件、真模板、真实屏幕取样；后端是 <see cref="SearchProbeServer"/> 与假连接，
    /// 不登录真实 MoviePilot、不读真实站点目录、不下载。
    /// </para>
    /// </summary>
    private async Task MoviePilotSearchShellAsync()
    {
        var hostApp = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_window!.Handle));
        var hostBounds = new RectInt32(hostApp.Position.X, hostApp.Position.Y, hostApp.Size.Width, hostApp.Size.Height);
        var server = new SearchProbeServer();
        using var fixture = CreateFixture(server);
        await fixture.SelectAsync();
        await ConnectMoviePilotAsync(fixture);
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var scale = _root.XamlRoot.RasterizationScale;
        var work = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
            Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_window!.Handle),
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;

        var created = MoviePilotWindow.TryCreate(_window);
        Require(created is not null, "MoviePilot 版本窗口建不出来");
        var window = created!;
        var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)window.AppWindow.Presenter;
        var wasTopMost = presenter.IsAlwaysOnTop;
        var hidden = false;
        try
        {
            presenter.IsAlwaysOnTop = true;
            window.Show(service, "测试资源", fixture.Launcher);
            var view = window.View;
            // 先把这一扇窗真的排过版（模板应用、Loaded 跑过、站点入口搬进框里）再读控件：
            // 搜索在假服务器上快得来不及渲染，只等 IsReady 会读到一颗还没有模板的 AutoSuggestBox。
            await LayoutAsync(view);
            await UntilAsync(() => view.ViewModel.IsReady && !view.ViewModel.Busy);
            window.Activate();

            var input = Get<AutoSuggestBox>(view, "SearchInput");
            var picker = Get<MoviePilotSitePicker>(view, "SearchSitePicker");
            var query = Descendants(input).OfType<Button>().Single(button => button.Name == "QueryButton");

            // 页面上那两行字去了（用户 2026-10-30：「移除…搜索框下方…搜索站点」）。
            Require(!Descendants(view).OfType<TextBlock>().Any(text => text.Text == "搜索 MoviePilot 资源"),
                "搜索框上方仍有「搜索 MoviePilot 资源」那行标题");
            Require(input.Header is null or "", "搜索框仍带着 Header 标题");
            // 框与搜索页同高、同一档圆角，不改变框内站点的空间。
            Require(Math.Abs(input.MinHeight - 40) < 0.5 && input.CornerRadius == new CornerRadius(10),
                $"搜索框不是 40 高、10 圆角的输入框：{input.MinHeight} / {input.CornerRadius}");
            RequireSearchRadius(input);
            // 站点入口住在框的模板里、放大镜左边 —— 不是框下另起一行。
            Require(ReferenceEquals(picker.Parent, VisualTreeHelper.GetParent(query)),
                "站点入口没有和搜索按钮同住在搜索框模板里");
            Require(picker.Visibility == Visibility.Visible, "窗口里的站点入口没有显示");

            foreach (var width in new[] { 900, 640, 480 })
            {
                var height = width switch { 900 => 680, 640 => 560, _ => 520 };
                window.AppWindow.MoveAndResize(new RectInt32(work.X + 60, work.Y + 60,
                    (int)Math.Round(width * scale), (int)Math.Round(height * scale)));
                window.Activate();
                await LayoutAsync(view);
                await Task.Delay(120);

                var inputBox = Box(view, input);
                var pickerBox = Box(view, picker);
                var queryBox = Box(view, query);
                Require(inputBox.Width > 200 && inputBox.Height >= 39,
                    $"{width} 宽时搜索框量出来的尺寸不对：{inputBox}");
                Require(pickerBox.Left >= inputBox.Left && pickerBox.Right <= queryBox.Left
                    && queryBox.Right <= inputBox.Right && pickerBox.Bottom <= inputBox.Bottom,
                    $"{width} 宽时站点入口不在搜索框内、或压到了搜索按钮：picker={pickerBox}, query={queryBox}, input={inputBox}");
                Require(pickerBox.Width <= (width < 520 ? 108 : 180) + 1,
                    $"{width} 宽时站点入口超过了这一档的宽度上限：{pickerBox.Width}");

                var handle = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
                Require(Native.SetWindowPos(handle, Native.HwndTopMost, 0, 0, 0, 0,
                    Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate), "版本窗口截图前无法提升本轮窗口");
                await Task.Delay(80);
                Require(Native.GetVisibleFrame(handle, out var frame), "版本窗口截图前无法读取边框");
                var hits = new List<string>();
                foreach (var (x, y) in new[] { (0.05, 0.2), (0.95, 0.2), (0.05, 0.8), (0.95, 0.8) })
                {
                    var point = new NativePoint { X = frame.Left + 4 + (int)((frame.Width - 8) * x), Y = frame.Top + 4 + (int)((frame.Height - 8) * y) };
                    var actual = Native.GetAncestor(Native.WindowFromPoint(point), Native.GaRoot);
                    Native.GetWindowThreadProcessId(actual, out var owner);
                    hits.Add($"{point.X},{point.Y}=0x{actual:X} pid={owner}");
                }
                File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, $"mp-window-{width}-owner.txt"),
                    $"target=0x{handle:X}; foreground=0x{Native.GetForegroundWindow():X}; frame={frame.Left},{frame.Top},{frame.Width},{frame.Height}; " + string.Join("; ", hits));
                // 标题栏小档图标就是那颗全透明的；大档仍是应用图标（任务栏、Alt-Tab 不该跟着变）。
                var small = Native.SendMessage(handle, Native.WmGetIcon, (IntPtr)0, IntPtr.Zero);
                var big = Native.SendMessage(handle, Native.WmGetIcon, (IntPtr)1, IntPtr.Zero);
                Require(small == BlankCaptionIcon.Handle && small != IntPtr.Zero,
                    "标题栏左上角那颗图标不是全透明的那一颗");
                Require(big != BlankCaptionIcon.Handle, "大档图标也被换掉了：任务栏和 Alt-Tab 会一起没图");
                await CompositionPlaybackProbe.SaveWindowScreenAsync(handle,
                    Path.Combine(_options.Paths.LogDirectory, $"mp-window-{width}-screen.png"));
            }

            // 只能 Hide，不能 Close：这是本进程唯一的 WinUI Window，真关掉会触发「最后一个窗口关了 → 进程退出」
            //（MoviePilotWindow 头上那段说的就是这个坑）。探针进程最后走 Application.Exit。
            window.Hide();
            hidden = true;
            await SettleAsync();

            var page = CreateMoviePilotSettingsPage(fixture, service);
            await page.ViewModel.ReloadAsync();
            _root.Children.Add(page);
            try
            {
                BeginSettingsFixtureVisit(page);
                // 这条是 OnNavigatedTo 里的那根线；直接挂页的夹具不走导航，得自己接上，断线/恢复才看得见。
                page.WatchMoviePilot(service);
                await LayoutAsync(page);
                ResizeInspect((int)Math.Round(1100 * scale), (int)Math.Round(900 * scale));
                page.SelectedCategory = "MoviePilot";
                await LayoutAsync(page);

                var row = page.ViewModel.Sections.SelectMany(section => section.Rows)
                    .OfType<SettingMoviePilotRow>().Single();
                var sites = Descendants(page).OfType<MoviePilotSitePicker>()
                    .Single(picker => picker.Name == "DefaultSearchSites");
                // x:Bind 收的是外面那一截（StackPanel）；选择器自己的 Visibility 一直是 Visible，
                // 被父级收起来才是屏幕上真的没有了。
                var sitesBlock = (FrameworkElement)sites.Parent;
                Require(row.SitesAvailable && sitesBlock.Visibility == Visibility.Visible,
                    "连接可用时设置页没有「默认搜索站点」这一条");
                Require(Get<TextBlock>(sites, "SiteLabel").Text == "默认搜索站点",
                    "设置页站点入口前面的字不是「默认搜索站点」");
                Require(Box(page, sites).Height > 0 && Box(page, sites).Width > 0, "设置页站点入口没有画出来");
                Require(row.SearchSites.Summary == "默认站点", $"设置页站点入口的初始读数不是默认站点：{row.SearchSites.Summary}");

                // 窄一档的设置窗口里这一条也不能顶出卡片：设置窗口最窄就是 760。
                ResizeInspect((int)Math.Round(760 * scale), (int)Math.Round(900 * scale));
                await LayoutAsync(page);
                var narrow = Box(page, sites);
                Require(narrow.Width > 80 && narrow.Right <= page.ActualWidth - 20,
                    $"760 宽设置页里「默认搜索站点」被裁切或顶出卡片：{narrow}，页宽 {page.ActualWidth}");
                await SaveProbeScreenAsync("settings-moviepilot-sites-760-screen.png");
                ResizeInspect((int)Math.Round(1100 * scale), (int)Math.Round(900 * scale));
                await LayoutAsync(page);

                var (dialog, opening) = await OpenSiteDialogAsync(sites, "设置页");
                var draft = dialog.ViewModel;
                await UntilAsync(() => draft.Sites.Count == 3);
                Get<CheckBox>(dialog, "UseDefaultSites").IsChecked = false;
                draft.Sites.Single(site => site.Id == 2).Selected = true;
                await UntilAsync(() => dialog.IsPrimaryButtonEnabled);
                ClickDialogPrimary(dialog);
                Require(await opening == ContentDialogResult.Primary, "设置页站点网格没有以确认关闭");
                await UntilAsync(() => service.SearchSitePreference().SiteIds.Contains(2));
                Require(!service.SearchSitePreference().UseDefault, "设置页选了站点但存下来仍是默认站点");
                await LayoutAsync(page);
                await SaveProbeScreenAsync("settings-moviepilot-sites-screen.png");

                // 断线：这一截整块收起（不是置灰），连接配置和测试按钮照旧在；恢复后原选择跟着回来。
                fixture.Settings.MoviePilot.Enabled = false;
                await UntilAsync(() => !row.SitesAvailable);
                await LayoutAsync(page);
                Require(sitesBlock.Visibility == Visibility.Collapsed, "断线后「默认搜索站点」仍显示");
                Require(Descendants(page).OfType<ToggleSwitch>().Any(toggle => toggle.Header as string == "启用"),
                    "断线后连「启用」开关也一起收起了");
                await SaveProbeScreenAsync("settings-moviepilot-sites-offline-screen.png");
                fixture.Settings.MoviePilot.Enabled = true;
                Require(await service.RefreshAvailabilityAsync(), "恢复后 MoviePilot 仍不可用");
                await UntilAsync(() => row.SitesAvailable);
                await LayoutAsync(page);
                Require(sitesBlock.Visibility == Visibility.Visible, "连接恢复后「默认搜索站点」没有回来");
                Require(!row.SearchSites.UseDefault && row.SearchSites.Selection?.Ids.Contains(2) == true,
                    "收起再恢复之后原来的站点选择丢了");

                // 设置页选的站点真的进下一次搜索：窗口那一支按同一份设置搜，请求里带着编号。
                server.Paths.Clear();
                var reopened = MoviePilotWindow.TryCreate(_window);
                Require(reopened is not null, "第二次建 MoviePilot 版本窗口失败");
                var again = reopened!;
                try
                {
                    again.Show(service, "设置页默认站点", fixture.Launcher);
                    await UntilAsync(() => again.View.ViewModel.IsReady && !again.View.ViewModel.Busy);
                    Require(server.Paths.Any(path => path.Contains("sites=2", StringComparison.Ordinal)),
                        "设置页选的默认站点没有进入关键词搜索请求");
                    Require(Get<MoviePilotSitePicker>(again.View, "SearchSitePicker").ViewModel.Summary == "站点乙",
                        "窗口里的站点入口没有显示设置页选的那一个");
                }
                finally { again.Hide(); }
            }
            finally
            {
                page.Release();
                // 弹层是独立 popup 根：页面离树之前先关掉，免得后一个用例在主窗口上开弹层时撞到它。
                ClosePopups();
                _root.Children.Remove(page);
            }
        }
        finally
        {
            presenter.IsAlwaysOnTop = wasTopMost;
            fixture.Settings.MoviePilot.Enabled = false;
            ClosePopups();
            if (!hidden) window.Hide();
            hostApp.MoveAndResize(hostBounds);
            _window.Activate();
            await SettleAsync();
        }

        void ClosePopups()
        {
            if (_root.XamlRoot is not { } root) return;
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root)) popup.IsOpen = false;
        }

        Rect Box(FrameworkElement root, FrameworkElement element)
        {
            var origin = element.TransformToVisual(root).TransformPoint(default);
            return new Rect(origin, new Size(element.ActualWidth, element.ActualHeight));
        }
    }

    /// <summary>挂一张真的设置页：连接用夹具里那一个已经验证过的 MoviePilot 服务，其余都是本机隔离件。</summary>
    private SettingsPage CreateMoviePilotSettingsPage(Fixture fixture, MoviePilotService service)
    {
        var fonts = new FontLibrary();
        Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "settings-moviepilot-sites"));
        paths.EnsureCreated();
        var page = new SettingsPage();
        page.ViewModel.Attach(fixture.SettingsService, new ShaderStaging(fixture.Settings), fonts, paths,
            fixture.Launcher, fixture.Startup, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask,
            _ => Task.CompletedTask, () => Task.CompletedTask, service,
            new MoviePilotCredentials(PassthroughSecretProtector.Instance), new UpdateClient(new RejectNetwork()),
            fixture.Capabilities, fixture.Images);
        return page;
    }
}
