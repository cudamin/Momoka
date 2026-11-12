using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Music;
using Momoka.Playback;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Theming;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>Real WinUI controls with anonymous data and a backend that cannot start media.</summary>
    private async Task MusicAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var scope = fixture.Session.Capture();
        var picture = await FixtureImageAsync(160, 160);
        var tracks = Enumerable.Range(0, 12).Select(index => new EmbyItem
        {
            Id = "music-" + index,
            Name = index == 0 ? "夜色中的旋律" : "音乐曲目 " + (index + 1),
            Type = EmbyItemType.Audio,
            Artists = ["独立音乐人"],
            Album = "城市漫游",
            RunTimeTicks = TimeSpan.FromSeconds(215 + index).Ticks,
            ImageTags = new() { ["Primary"] = "music-art" }
        }).ToArray();
        // 封面墙那一半要的是专辑：方形封面加艺术家，第二行不该把专辑名再写一遍。
        var albums = Enumerable.Range(0, 9).Select(index => new EmbyItem
        {
            Id = "album-" + index,
            Name = "专辑 " + (index + 1),
            Type = EmbyItemType.MusicAlbum,
            AlbumArtists = [new EmbyNamedId { Id = "artist-1", Name = "独立音乐人" }],
            ChildCount = 10,
            ImageTags = new() { ["Primary"] = "album-art" }
        }).ToArray();
        foreach (var track in tracks) scope.Bind(track);
        foreach (var album in albums) scope.Bind(album);
        fixture.Transport.Reply = (request, _) =>
        {
            Require(request.Method == HttpMethod.Get, "音乐 UI 探针拒绝写入");
            if (request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(picture) });
            var wall = request.RequestUri.Query.Contains("MusicAlbum", StringComparison.Ordinal);
            return Task.FromResult(Json(wall
                ? new ItemsResult { Items = [.. albums], TotalRecordCount = albums.Length }
                : new ItemsResult { Items = [.. tracks], TotalRecordCount = tracks.Length }));
        };
        var playback = new PlaybackService(fixture.Session, fixture.Settings,
            () => new MusicProbeBackend(), new PlaybackPlanner(fixture.Settings, new ShaderGroupResolver(fixture.Settings.Shaders)), allowPlayback: false);
        using var vm = new MusicPlaybackViewModel(playback, fixture.Session, fixture.Images, fixture.Dispatcher, fixture.SettingsService);
        var shell = new ShellPage();
        // 外壳整棵上屏（侧栏探针同一条路）：把 Frame 从外壳里摘出来塞进别的容器会当场 COM 异常。
        // PlaceMusicBar 认的是 ContentFrame.Content，音乐页必须导航在外壳自己的 Frame 里；播放栏也直接用
        // 外壳那份，位置与 MaxWidth 全跟产品标记走。登录卡与外壳行由探针收起，只留浏览区和播放栏。
        Get<FrameworkElement>(shell, "SignIn").Visibility = Visibility.Collapsed;
        Get<FrameworkElement>(shell, "Chrome").Visibility = Visibility.Collapsed;
        var host = Get<Grid>(shell, "ContentHost");
        host.Visibility = Visibility.Visible;
        host.Padding = new Thickness(0);
        var frame = shell.Pages;
        var bar = shell.MusicBarHost;
        bar.Attach(vm);
        _root.Children.Add(shell);
        var originalTheme = ThemeHost.Current;
        void Place() => Call(shell, "PlaceMusicBar");
        try
        {
            Field(shell, "_music", vm);
            // AttachMusic 平时挂着这条反馈：栏从隐藏变显示，量过第一遍才有高度，避让要再算一次。
            // 探针不走 AttachMusic，手动挂同一条。
            bar.SizeChanged += (_, _) => Place();
            Field(shell, "_current", LibraryRequest.FavoritesTag);
            Place();
            Require(shell.Pages.Margin.Top == 0 && shell.Pages.Margin.Bottom == 0,
                "未播放音乐时残留旧首页导航占位");
            vm.Visible = true;
            Place();
            Require(shell.Pages.Margin.Top == 0 && shell.Pages.Margin.Bottom >= 92,
                "显示音乐栏后顶部多出导航占位或底部未留空间");
            shell.Favorites.HasItems = true;
            Place();
            Require(shell.Pages.Margin.Top == 0 && shell.Pages.Margin.Bottom >= 92,
                "收藏入口出现改变了音乐栏预留");
            vm.Visible = false;
            Place();
            // 收起是 140ms 淡出，避让在落定那一拍才归还。
            await UntilAsync(() => bar.Visibility == Visibility.Collapsed);
            Require(shell.Pages.Margin.Top == 0 && shell.Pages.Margin.Bottom == 0,
                "停止音乐后没有清除预留空间");
            var request = new LibraryRequest { Services = fixture.Services, Title = "音乐", ParentId = "music", CollectionType = "music", Source = scope };
            Require(frame.Navigate(typeof(MusicLibraryPage), request), "音乐页未导航");
            var page = (MusicLibraryPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady && page.ViewModel.Rows.Count == tracks.Length);
            vm.Title = tracks[0].Name;
            vm.Subtitle = "独立音乐人 · 城市漫游";
            vm.Artwork = new CardItem(tracks[0], fixture.Images, 48, aspect: 1);
            vm.Artwork.ClaimCard(bar);
            await vm.Artwork.EnsurePosterAsync(Task.CompletedTask);
            vm.Duration = 215;
            vm.Position = 78;
            vm.PositionClock = "1:18";
            vm.DurationClock = "3:35";
            vm.Volume = 130;
            foreach (var track in tracks) vm.Queue.Add(track);
            vm.Visible = true;
            vm.Paused = true;
            await LayoutAsync(shell);
            Place();
            await LayoutAsync(shell);
            Require(shell.Pages.Margin.Bottom == 0, "音乐页还在整行让位，胶囊压不到封面墙");
            Require(Get<ListView>(page, "MusicRows").Padding.Bottom >= bar.ActualHeight,
                "曲目表没有给悬浮播放栏留避让");

            // 曲目表里「正在放的是哪一首」：把播放栏换曲那一下交给页面真正的事件处理，不是直接改行。
            Field(page, "_music", vm);
            vm.CurrentKey = tracks[3].IdentityKey;
            Call(page, "OnMusicChanged", vm, new PropertyChangedEventArgs(nameof(MusicPlaybackViewModel.CurrentKey)));
            Require(page.ViewModel.Rows[3].IsPlaying && page.ViewModel.Rows.Count(row => row.IsPlaying) == 1,
                "正在播放的那一行没有被标出来");
            Require(Get<Grid>(page, "TrackHeader").Visibility == Visibility.Visible, "曲目表没有列头");

            foreach (var theme in UiThemes.All)
            {
                ThemeHost.Apply(theme.Id);
                foreach (var width in new[] { 1280, 760, 360 })
                {
                    ResizeProbeClient(width, 760);
                    await LayoutAsync(shell);
                    Place();
                    await LayoutAsync(shell);
                    var surface = Get<Border>(bar, "Surface");
                    Require(surface.Background is AcrylicBrush, "音乐栏不是亚克力画刷");
                    Require(bar.TransformToVisual(page).TransformPoint(new()).Y < page.ActualHeight - 8,
                        $"{width} 窗口下播放栏没有压在页面内容上");
                    if (width >= 480)
                    {
                        Require(bar.ActualHeight <= 80, $"{width} 窗口下播放栏不是单行胶囊：{bar.ActualHeight:0.#}");
                        Require(surface.CornerRadius.TopLeft >= bar.ActualHeight / 2 - 0.5,
                            $"{width} 窗口下胶囊两端不是半圆");
                    }
                    Require(Get<ListView>(page, "MusicRows").Padding.Bottom >= bar.ActualHeight,
                        $"{width} 窗口下曲目表没有给悬浮播放栏留避让");
                    Require(Get<Slider>(bar, "VolumeSlider").Maximum == AudioSettings.MaxVolume, "音量上限不一致");
                    foreach (var name in new[] { "PreviousButton", "PauseButton", "StopButton", "NextButton", "QueueButton", "VolumeButton" })
                    {
                        var button = Get<Button>(bar, name);
                        var point = button.TransformToVisual(bar).TransformPoint(new());
                        Require(button.ActualWidth > 0 && point.X >= -1 && point.X + button.ActualWidth <= bar.ActualWidth + 1,
                            $"{width} 窗口下 {name} 超出音乐栏");
                        Require(FrameworkElementAutomationPeer.CreatePeerForElement(button) is not null, name + " 缺少自动化节点");
                    }
                    await SaveProbeScreenAsync($"music-{theme.Id}-{width}-screen.png");

                    // 版面的读数：药丸、搜索框和页边的关系没法靠肉眼从截图里量准，写进同目录的文本供复核。
                    var search = Get<AutoSuggestBox>(page, "MusicSearch");
                    var searchLeft = search.TransformToVisual(page).TransformPoint(new()).X;
                    var searchRight = searchLeft + search.ActualWidth;
                    File.AppendAllText(Path.Combine(_options.Paths.LogDirectory, "music-layout.txt"),
                        $"{theme.Id} {width}: page={page.ActualWidth:0.#} pill={Get<Border>(page, "SectionSurface").ActualWidth:0.#} "
                        + $"search={search.Width:0.#}/{search.ActualWidth:0.#} [{searchLeft:0.#}..{searchRight:0.#}] "
                        + $"lane={page.ActualWidth - 32:0.#} rows={page.ViewModel.Rows.Count}{Environment.NewLine}");
                    Require(search.ActualWidth >= 179, $"{width} 窗口下搜索框窄到不可用：{search.ActualWidth:0.#}");
                    Require(searchRight <= page.ActualWidth - 31, $"{width} 窗口下搜索框越过页边：{searchRight:0.#}");
                }

                // 专辑那一半：同一份数据换成方形封面墙。曲目表与墙互斥，列头跟着收起。
                ResizeProbeClient(1280, 760);
                await page.ViewModel.SelectAsync(MusicSection.Albums);
                await UntilAsync(() => page.ViewModel.IsReady && page.ViewModel.Rows.Count == albums.Length);
                await LayoutAsync(shell);
                Place();
                await LayoutAsync(shell);
                Require(page.ViewModel.WallVisibility == Visibility.Visible
                    && page.ViewModel.TrackViewVisibility == Visibility.Collapsed, "专辑分区没有换成封面墙");
                Require(Get<Grid>(page, "TrackHeader").Visibility == Visibility.Collapsed, "封面墙上还留着曲目表列头");
                Require(Get<ItemsView>(page, "MusicWall").ActualWidth > 0, "封面墙没有画出宽度");
                Require(page.ViewModel.Rows[0].CardWidth == MusicCatalog.WallTileWidth, "封面墙格子宽度不是 170");
                Require(Get<ScrollViewer>(page, "WallScroll").Padding.Bottom >= bar.ActualHeight + 16,
                    "封面墙没有给悬浮播放栏留避让");
                await SaveProbeScreenAsync($"music-wall-{theme.Id}-1280-screen.png");

                await page.ViewModel.SelectAsync(MusicSection.Tracks);
                await UntilAsync(() => page.ViewModel.IsReady && page.ViewModel.Rows.Count == tracks.Length);
                await LayoutAsync(shell);
            }
            var peer = new ButtonAutomationPeer(Get<Button>(bar, "StopButton"));
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => !vm.Visible && vm.Queue.Count == 0);
            Require(!playback.IsPlaying, "停止控件意外启动播放");
            Place();
            // 停止后播放栏先淡出再收起，避让也在落定那一拍才归还。
            await UntilAsync(() => bar.Visibility == Visibility.Collapsed);
            Require(bar.Visibility == Visibility.Collapsed, "停止后播放栏没有收起");
            Require(Get<ScrollViewer>(page, "WallScroll").Padding.Bottom == 32
                && Get<ListView>(page, "MusicRows").Padding.Bottom == 0, "停止后没有归还滚动区避让");
            page.Release();
        }
        finally
        {
            _root.Children.Remove(shell);
            (frame.Content as IShellContent)?.Release();
            vm.Artwork?.AbandonPoster();
            ThemeHost.Apply(originalTheme.Id);
        }
    }
    private sealed class MusicProbeBackend : IPlaybackBackend
    {
        public string DisplayName => "离线音乐探针";
        public string? Validate() => null;
        public Task<IPlaybackHandle> StartAsync(PlaybackRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("音乐 UI 探针禁止真实播放");
    }
}
