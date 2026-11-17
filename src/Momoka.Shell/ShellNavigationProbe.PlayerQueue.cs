using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Danmaku;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>The current player and native picker are real; the handle cannot load media or report history.</summary>
    private async Task PlayerQueueAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync("queue-picker.invalid");
        var scope = fixture.Session.Capture();
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "queue-picker"));
        paths.EnsureCreated();
        using var client = new DandanPlayClient(fixture.Settings, Path.Combine(paths.Root, "cache"), new RejectNetwork());
        var shaders = new ShaderGroupResolver(fixture.Settings.Shaders);
        var playback = new PlaybackService(fixture.Session, fixture.Settings,
            () => throw new InvalidOperationException("队列选择探针禁止创建真实后端"), new PlaybackPlanner(fixture.Settings, shaders), allowPlayback: false);
        var vm = new PlayerViewModel(playback, fixture.SettingsService, fixture.Session, fixture.Images, shaders, fixture.Dispatcher, client);
        var page = new PlayerPage();
        page.Attach(vm, new ShellPage(), _window!);
        var playing = scope.Bind(new EmbyItem { Id = "playing", Name = "本次播放 · 离线夹具", Type = EmbyItemType.Movie });
        var intent = Get<StartIntent>(vm, "_startIntent").Begin();
        Field(vm, "_activeIntent", intent);
        Field(vm, "_nowPlaying", playing);
        Field(vm, "_playbackScope", scope);
        Get<SessionPlaybackQueue>(vm, "_sessionQueue").Begin();
        Field(playback, "_current", new SkipProbeHandle());
        _root.Children.Add(page);
        page.ShowChromeForShot(null);
        fixture.Transport.Reply = (request, token) =>
        {
            token.ThrowIfCancellationRequested();
            Require(request.Method == HttpMethod.Get, "本次队列只读探针禁止服务器写入");
            return Task.FromResult(Query(request, "SearchTerm") is not null ? Json(new
            {
                Items = new[] { new EmbyItem { Id = "next", Name = "下一部 · 本次队列", Type = EmbyItemType.Movie } },
                TotalRecordCount = 1
            }) : FakeTransport.Standard(request));
        };
        try
        {
            foreach (var width in new[] { 1280, 760, 360 })
            {
                ResizeProbeClient(width, 900);
                await LayoutAsync(page);
                Require(vm.CanArrangeQueue, "正在播放的离线句柄没有排队入口");
                Call(page, "RebuildPictureMenu");
                var menu = Get<MenuFlyout>(page, "PictureMenu");
                var queue = menu.Items.OfType<MenuFlyoutSubItem>().Single(item => item.Name == "PlayerSessionVideoQueue");
                Require(queue.Items.OfType<MenuFlyoutItem>().Any(item => item.Name == "PlayerQueueNext")
                    && queue.Items.OfType<MenuFlyoutItem>().Any(item => item.Name == "PlayerQueueAppend"), "播放画面菜单缺少队列操作");
                var choosing = page.ShowQueueDialogAsync(_root.XamlRoot, next: true);
                await UntilAsync(() => DialogsFor(_root.XamlRoot).Length == 1);
                var dialog = DialogsFor(_root.XamlRoot)[0];
                Require(DialogDismiss.GetClosesOnOutsideClick(dialog), "播放队列选择框未继承点外取消");
                // Popup 里出现 ContentDialog 不等于模板已展开：等搜索框长进视觉树，超时仍如实报缺控件。
                TextBox? term = null;
                await UntilAsync(() => (term = Descendants(dialog).OfType<TextBox>()
                    .FirstOrDefault(element => element.Name == "VideoQueueSearchTerm")) is not null);
                term!.Text = "下一部";
                Button? search = null;
                await UntilAsync(() => (search = Descendants(dialog).OfType<Button>()
                    .FirstOrDefault(element => element.Name == "VideoQueueSearch")) is not null);
                new ButtonAutomationPeer(search!).Invoke();
                ListView? results = null;
                await UntilAsync(() => (results = Descendants(dialog).OfType<ListView>()
                    .FirstOrDefault(element => element.Name == "VideoQueueSearchResults")) is not null);
                await UntilAsync(() => results!.Items.Count > 0);
                results!.SelectedIndex = 0;
                await LayoutAsync(dialog);
                await SaveProbeScreenAsync($"video-queue-picker-{width}-screen.png");
                new ButtonAutomationPeer(Descendants(dialog).OfType<Button>().Single(element => element.Name == "PrimaryButton")).Invoke();
                await choosing;
                Require(vm.PendingQueue.Count == 1 && vm.PendingQueue[0].Item.Id == "next", "原生确认未安排本次队列");
                vm.ClearQueue();
                var cancel = page.ShowQueueDialogAsync(_root.XamlRoot, next: false);
                await UntilAsync(() => DialogsFor(_root.XamlRoot).Length == 1);
                DialogsFor(_root.XamlRoot)[0].Hide();
                await cancel;
                Require(!vm.HasPendingQueue, "取消选择仍安排媒体");
            }
            Require(fixture.Actions.Plays == 0 && fixture.Transport.Requests.All(request => request.StartsWith("GET ", StringComparison.Ordinal)),
                "本次队列选择意外起播或服务器写入");
        }
        finally
        {
            Field(playback, "_current", null);
            page.Detach();
            page.Shutdown();
            _root.Children.Remove(page);
            page.ReleaseVideoSurface();
            await vm.ShutdownAsync();
        }
    }

    private static ContentDialog[] DialogsFor(XamlRoot root) => VisualTreeHelper.GetOpenPopupsForXamlRoot(root)
        .SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>().Distinct().ToArray();
}
