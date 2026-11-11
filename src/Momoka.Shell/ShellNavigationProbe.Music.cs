using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
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
        foreach (var track in tracks) scope.Bind(track);
        fixture.Transport.Reply = (request, _) =>
        {
            Require(request.Method == HttpMethod.Get, "音乐 UI 探针拒绝写入");
            if (request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(picture) });
            return Task.FromResult(Json(new ItemsResult { Items = [.. tracks], TotalRecordCount = tracks.Length }));
        };
        var playback = new PlaybackService(fixture.Session, fixture.Settings,
            () => new MusicProbeBackend(), new PlaybackPlanner(fixture.Settings, new ShaderGroupResolver(fixture.Settings.Shaders)), allowPlayback: false);
        using var vm = new MusicPlaybackViewModel(playback, fixture.Session, fixture.Images, fixture.Dispatcher, fixture.SettingsService);
        var frame = new Frame();
        var bar = new MusicPlayerBar { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(20, 0, 20, 16) };
        bar.Attach(vm);
        var container = new Grid();
        container.Children.Add(frame);
        container.Children.Add(bar);
        _root.Children.Add(container);
        ThemeHost.Register(container);
        var originalTheme = ThemeHost.Current;
        try
        {
            var shell = new ShellPage();
            Field(shell, "_music", vm);
            Field(shell, "_current", LibraryRequest.FavoritesTag);
            var navigation = Get<SelectorBar>(shell, "HomeNavigation");
            var navigationHost = Get<Viewbox>(shell, "HomeNavigationHost");
            navigation.Visibility = Visibility.Visible;
            navigationHost.Height = 36;
            navigationHost.Margin = new Thickness(0, 12, 0, 0);
            Call(shell, "UpdateHomeNavigationInset");
            Call(shell, "PlaceMusicBar");
            Require(shell.Pages.Margin.Top == 60 && shell.Pages.Margin.Bottom == 0,
                "未播放音乐时丢失首页导航预留");
            vm.Visible = true;
            Call(shell, "PlaceMusicBar");
            Require(shell.Pages.Margin.Top == 60 && shell.Pages.Margin.Bottom >= 92,
                "显示音乐栏清掉首页导航预留");
            navigation.Visibility = Visibility.Collapsed;
            Call(shell, "UpdateHomeNavigationInset");
            Require(shell.Pages.Margin.Top == 0 && shell.Pages.Margin.Bottom >= 92,
                "离开首页清掉音乐栏预留");
            navigation.Visibility = Visibility.Visible;
            Call(shell, "UpdateHomeNavigationInset");
            vm.Visible = false;
            Call(shell, "PlaceMusicBar");
            Require(shell.Pages.Margin.Top == 60 && shell.Pages.Margin.Bottom == 0,
                "停止音乐后首页导航预留没有保留");
            Field(shell, "_music", null);
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
            foreach (var theme in UiThemes.All)
            {
                ThemeHost.Apply(theme.Id);
                foreach (var width in new[] { 1280, 760, 360 })
                {
                    ResizeProbeClient(width, 760);
                    await LayoutAsync(container);
                    frame.Margin = new Thickness(0, 0, 0, bar.ActualHeight + 32);
                    await LayoutAsync(container);
                    Require(Get<Border>(bar, "Surface").Background is AcrylicBrush, "音乐栏不是亚克力画刷");
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
                }
            }
            var peer = new ButtonAutomationPeer(Get<Button>(bar, "StopButton"));
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => !vm.Visible && vm.Queue.Count == 0);
            Require(!playback.IsPlaying, "停止控件意外启动播放");
            page.Release();
        }
        finally
        {
            _root.Children.Remove(container);
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
