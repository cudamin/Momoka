using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Music;
using Momoka.Playback;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>Real page, finite wall host and native invoke with anonymous 81/160-item pages.</summary>
    private async Task MusicPagingAsync(bool failEntryLocator = false)
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var server = new MusicPagingProbeServer();
        fixture.Transport.Reply = server.ReplyAsync;
        var playback = new PlaybackService(fixture.Session, fixture.Settings, () => new MusicProbeBackend(),
            new PlaybackPlanner(fixture.Settings, new ShaderGroupResolver(fixture.Settings.Shaders)), allowPlayback: false);
        using var music = new MusicPlaybackViewModel(playback, fixture.Session, fixture.Images, fixture.Dispatcher, fixture.SettingsService);
        var shell = new ShellPage();
        Get<FrameworkElement>(shell, "SignIn").Visibility = Visibility.Collapsed;
        Get<FrameworkElement>(shell, "Chrome").Visibility = Visibility.Collapsed;
        var host = Get<Grid>(shell, "ContentHost");
        host.Visibility = Visibility.Visible;
        host.Padding = new Thickness(0);
        shell.MusicBarHost.Attach(music);
        Field(shell, "_music", music);
        _root.Children.Add(shell);
        var evidence = new List<object>();
        var request = new LibraryRequest
        { Services = fixture.Services, Title = "音乐分页", ParentId = "music", CollectionType = "music", Source = fixture.Session.Capture() };
        try
        {
            Require(shell.Pages.Navigate(typeof(MusicLibraryPage), request), "音乐分页页未导航");
            var page = (MusicLibraryPage)shell.Pages.Content;
            await UntilAsync(() => page.ViewModel.IsReady && page.ViewModel.Rows.Count == MusicCatalog.PageSize);
            foreach (var section in new[] { MusicSection.Albums, MusicSection.Artists, MusicSection.Playlists })
            {
                var type = section switch
                {
                    MusicSection.Albums => EmbyItemType.MusicAlbum,
                    MusicSection.Artists => EmbyItemType.MusicArtist,
                    _ => EmbyItemType.Playlist
                };
                foreach (var total in new[] { 81, 160 })
                {
                    foreach (var width in new[] { 600, 760, 1280 })
                    {
                        server.Total = total;
                        server.Requests.Clear();
                        server.FailNextMore = false;
                        await page.ViewModel.SelectAsync(section);
                        Require(page.ViewModel.Rows.Count == MusicCatalog.PageSize && page.ViewModel.MoreAvailable, "墙首屏缺少实际余页");
                        ResizeProbeClient(width, 760);
                        music.Title = "匿名音乐栏";
                        music.Duration = 180;
                        music.Visible = true;
                        await LayoutAsync(shell);
                        Call(shell, "PlaceMusicBar");
                        await LayoutAsync(shell);
                        var more = Get<Button>(page, "WallMoreButton");
                        if (failEntryLocator) AutomationProperties.SetAutomationId(more, "MusicPagingWrongLocator");
                        Require(Descendants(page).OfType<Button>().SingleOrDefault(button => AutomationProperties.GetAutomationId(button) == "MusicWallLoadMore") == more,
                            "分页按钮的原生自动化定位失效");
                        Require(more.Visibility == Visibility.Visible && more.ActualWidth > 0 && more.ActualHeight > 0,
                            "封面墙分页按钮未渲染");
                        Require(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(more)) && more.Focus(FocusState.Keyboard),
                            "分页按钮缺少名称或不能键盘聚焦");
                        var point = more.TransformToVisual(page).TransformPoint(new());
                        var moreHeight = more.ActualHeight;
                        var barPoint = shell.MusicBarHost.TransformToVisual(page).TransformPoint(new());
                        Require(point.X >= -0.5 && point.X + more.ActualWidth <= page.ActualWidth + 0.5
                            && point.Y >= -0.5 && point.Y + more.ActualHeight <= barPoint.Y + 0.5,
                            "分页按钮越过页面或被悬浮播放栏覆盖");
                        Require(more.Margin.Bottom >= shell.MusicBarHost.ActualHeight + 32
                            && Get<ItemsView>(page, "MusicWall").Padding.Bottom >= shell.MusicBarHost.ActualHeight + 64,
                            "分页按钮/墙滚动区的音乐栏避让没有同步");
                        var first = page.ViewModel.Rows[0];
                        var showFailure = section == MusicSection.Playlists && total == 160 && width == 760;
                        if (showFailure)
                        {
                            server.FailNextMore = true;
                            InvokeMusicPaging(more);
                            await UntilAsync(() => !page.ViewModel.Busy);
                            await LayoutAsync(page);
                            Require(page.ViewModel.Rows.Count == MusicCatalog.PageSize && ReferenceEquals(first, page.ViewModel.Rows[0])
                                && page.ViewModel.NoticeOpen && page.ViewModel.MoreAvailable && more.Visibility == Visibility.Visible,
                                "余页失败丢失旧列表、错误或重试入口");
                        }
                        InvokeMusicPaging(more);
                        await UntilAsync(() => !page.ViewModel.Busy && page.ViewModel.Rows.Count == total);
                        await LayoutAsync(page);
                        MusicPagingProbeServer.ValidateLoaded(total, page.ViewModel.Rows.Select(row => row.Item).ToArray(), page.ViewModel.MoreAvailable, type);
                        Require(ReferenceEquals(first, page.ViewModel.Rows[0]) && more.Visibility == Visibility.Collapsed,
                            "余页重建首项或末页按钮未收起");
                        Require(server.Requests.Count(entry => entry.Offset == MusicCatalog.PageSize) == (showFailure ? 2 : 1)
                            && server.Requests.All(entry => entry.Limit == MusicCatalog.PageSize), "分页请求的边界或重试次数不符");
                        // Scroll the real wall to its loaded end, then confirm the last item owns a visible/focusable container.
                        var wall = Get<ItemsView>(page, "MusicWall");
                        var scroll = wall.ScrollView ?? throw new InvalidOperationException("音乐墙没有原生滚动宿主");
                        MusicArtworkProbeRules.Viewport(Get<Grid>(page, "WallScroll").ActualHeight, scroll.ViewportHeight);
                        var scrollKey = $"paging-{section}-{total}-{width}";
                        await ScrollMusicWallAsync(scroll, true, scrollKey);
                        var lastCard = await FocusMusicItemAsync(wall, total - 1,
                            MusicPagingProbeServer.ItemId(type, total - 1), scrollKey);
                        var repeater = Descendants(wall).OfType<ItemsRepeater>().Single();
                        var last = (FrameworkElement)repeater.TryGetElement(total - 1)!;
                        var endPoint = last.TransformToVisual(scroll).TransformPoint(new());
                        evidence.Add(new
                        {
                            section = section.ToString(),
                            total,
                            width,
                            rows = page.ViewModel.Rows.Count,
                            loadMoreX = point.X,
                            loadMoreY = point.Y,
                            loadMoreHeight = moreHeight,
                            barY = barPoint.Y,
                            lastId = lastCard.Item.Id,
                            endY = endPoint.Y,
                            retry = showFailure,
                            offsetRequests = server.Requests.Select(entry => entry.Offset).ToArray(),
                            physicalKeys = false
                        });
                        if (section == MusicSection.Albums && total == 160 && width == 1280
                            || section == MusicSection.Artists && total == 81 && width == 600
                            || showFailure)
                            await SaveProbeScreenAsync($"music-paging-{section}-{total}-{width}-screen.png");
                    }
                }
            }
            music.Visible = false;
            Call(shell, "PlaceMusicBar");
            await UntilAsync(() => shell.MusicBarHost.Visibility == Visibility.Collapsed);
            Require(Get<Button>(page, "WallMoreButton").Margin.Bottom == 0 && Get<ItemsView>(page, "MusicWall").Padding.Bottom == 32,
                "音乐栏关闭后分页避让没有恢复");
            Require(!playback.IsPlaying, "分页探针意外开始播放");
            File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, "music-paging-receipt.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            (shell.Pages.Content as IShellContent)?.Release();
            _root.Children.Remove(shell);
            await playback.ShutdownAsync();
        }
    }

    private static void InvokeMusicPaging(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
}
