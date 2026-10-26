using Momoka.Emby;
using Momoka.Configuration;
using Momoka.FnNas;
using Momoka.Playback;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Mpv;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json.Nodes;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task InspectAsync()
    {
        using var fixture = CreateFixture(new SearchProbeServer());
        await fixture.SelectAsync();
        fixture.Settings.MoviePilot.Enabled = true;
        fixture.Settings.MoviePilot.Url = "https://moviepilot.invalid:3001";
        fixture.Settings.MoviePilot.Username = "fixture";
        new Momoka.Configuration.MoviePilotCredentials(Momoka.Configuration.PassthroughSecretProtector.Instance)
            .SetPassword(fixture.Settings.MoviePilot, "fixture");
        var image = await FixtureImageAsync();
        var item = Series();
        item.Name = "离线示例 · 媒体导航与封面管理";
        item.BackdropImageTags = ["a", "b"];
        item.ImageTags["Primary"] = "poster";
        item.ImageTags["Logo"] = "logo";
        fixture.Transport.Reply = (request, _) => request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal)
            ? Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(image) })
            : Task.FromResult(FakeTransport.Standard(request));
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12) };
        var frame = new Frame();
        Grid.SetRow(frame, 1);
        layout.Children.Add(controls);
        layout.Children.Add(frame);
        _root.Children.Add(layout);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = false;
        using var settingsMoviePilot = new MoviePilotClient(new RejectNetwork());
        var animationPreference = HomeMotion.PreferenceEnabled;
        void Add(string id, string label, Func<Task> action)
        {
            var button = new Button { Content = label };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += async (_, _) =>
            {
                if (busy) return;
                busy = true;
                try { await action(); }
                finally { busy = false; }
            };
            controls.Children.Add(button);
        }
        Add("InspectHome", "首页", () =>
        {
            frame.Navigate(typeof(HomePage), new HomeRequest(fixture.Services,
                [new EmbyItem { Id = "library", Name = "离线媒体库", Type = EmbyItemType.CollectionFolder, CollectionType = "movies", ImageTags = new() { ["Primary"] = "library" } }], null));
            return Task.CompletedTask;
        });
        Add("InspectLibrary", "媒体库", () =>
        {
            frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services, "离线示例"));
            return Task.CompletedTask;
        });
        Add("InspectDetail", "剧集详情", () =>
        {
            frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item));
            return Task.CompletedTask;
        });
        Add("InspectResources", "资源搜索", () =>
        {
            frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services));
            Get<CommunityToolkit.WinUI.Controls.Segmented>((LibraryPage)frame.Content, "SourceTabs").SelectedIndex = 2;
            return Task.CompletedTask;
        });
        Add("InspectFile", "单集文件", () =>
        {
            frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, Episode("a")));
            return Task.CompletedTask;
        });
        Add("InspectSettingsMotion", "设置动画", async () =>
        {
            if (frame.Content is IShellContent previous) previous.Release();
            fixture.Settings.Playback.AudioFormatRules =
                [new("Atmos", TitlePreference.Prefer), new("FLAC", TitlePreference.Prefer), new("DTS-HD", TitlePreference.Neutral)];
            fixture.Settings.Playback.SubtitleTitleRules =
                [new("双语", TitlePreference.Prefer), new("SDH", TitlePreference.Exclude)];
            fixture.Settings.Playback.VideoFileRules =
                [new("REMUX", TitlePreference.Prefer), new("HDR", TitlePreference.Prefer)];
            fixture.Settings.Mpv.ScreenshotFormat = "webp";
            fixture.Settings.Mpv.ScreenshotWebpLossless = false;
            fixture.Settings.Mpv.Pipeline = VideoPipelineKind.Standalone;
            var paths = new AppPaths(Path.Combine(_options.Paths.Root, "inspect-settings-motion"));
            paths.EnsureCreated();
            var page = CreateMotionSettingsPage(fixture, settingsMoviePilot, paths);
            frame.Content = page;
            BeginSettingsFixtureVisit(page);
            HomeMotion.ApplyPreference(true);
            await page.ViewModel.ReloadAsync();
            page.SelectedCategory = "播放行为";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
        });
        Add("InspectNotifications", "通知", () =>
        {
            frame.Navigate(typeof(NotificationsPage), new NotificationsRequest(fixture.Services));
            return Task.CompletedTask;
        });
        Add("InspectCover", "封面管理", async () =>
        {
            var dialog = new CoverDialog(item,
                (_, _) => Task.FromResult(new RemoteImageResult { Images = [new RemoteImageInfo { Url = "https://fixture.invalid/art", ProviderName = "离线候选", Width = 320, Height = 200 }] }),
                (_, _, _, _, _) => Task.FromResult<byte[]?>(image),
                (_, type, _, _) => { item.ImageTags[type] = "changed"; return Task.CompletedTask; },
                (_, type, index) =>
                {
                    if (type == "Backdrop" && index is { } at) item.BackdropImageTags.RemoveAt(at);
                    else item.ImageTags.Remove(type);
                    return Task.CompletedTask;
                },
                (_, _, _) => throw new InvalidOperationException("检查面板不向服务器上传本机文件"),
                _ => Task.FromResult<EmbyItem?>(item), _ => Task.FromResult<byte[]?>(image))
            { XamlRoot = _root.XamlRoot };
            await dialog.ShowAsync();
        });
        Add("InspectFnNasEdit", "飞牛编辑元数据", async () =>
        {
            var detail = JsonNode.Parse("""
                {"title":"离线示例","overview":"一段离线简介，用来看编辑表的长相。","rating":8.4,
                 "release_date":"2024-05-01","genres":[2,7],"posters":["p"]}
                """)!.AsObject();
            var dialog = new FnNasMetadataDialog(detail) { XamlRoot = _root.XamlRoot };
            dialog.Opened += async (_, _) =>
            {
                await SaveSettledFrameAsync(dialog, "inspect-fn-nas-edit.png");
                dialog.Hide();
            };
            await dialog.ShowAsync();
        });
        Add("InspectFnNasMatch", "飞牛手动匹配", async () =>
        {
            var dialog = new FnNasMatchDialog("离线示例", (_, _) => Task.FromResult<IReadOnlyList<FnNasScrapResult>>(
            [
                new("离线候选一", "tmdb", "1", "Movie", "2024-01-01"),
                new("离线候选二", "tmdb", "2", "TV", "2023-01-01")
            ]))
            { XamlRoot = _root.XamlRoot };
            dialog.Opened += async (_, _) =>
            {
                await SaveSettledFrameAsync(dialog, "inspect-fn-nas-match.png");
                dialog.Hide();
            };
            await dialog.ShowAsync();
        });
        Add("InspectFnNasVersions", "飞牛管理版本", async () =>
        {
            var dialog = new FnNasVersionsDialog("离线示例",
            [
                new("file1", "/media/离线示例.1080p.mkv", 2_147_483_648, "1080p"),
                new("file2", "/media/离线示例.2160p.mkv", 8_589_934_592, "4K")
            ])
            { XamlRoot = _root.XamlRoot };
            dialog.Opened += async (_, _) =>
            {
                await SaveSettledFrameAsync(dialog, "inspect-fn-nas-versions.png");
                dialog.Hide();
            };
            await dialog.ShowAsync();
        });
        Add("InspectNarrow", "窄窗口", () => { ResizeInspect(1000, 880); return Task.CompletedTask; });
        Add("InspectWide", "宽窗口", () => { ResizeInspect(1420, 900); return Task.CompletedTask; });
        Add("InspectDone", "结束检查", () => { finished.TrySetResult(); return Task.CompletedTask; });
        frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services, "离线示例"));
        await LayoutAsync(layout);
        File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, "shell-probe-ready.txt"), Environment.ProcessId.ToString());
        await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromMinutes(8)));
        if (frame.Content is IShellContent page) page.Release();
        _root.Children.Remove(layout);
        HomeMotion.ApplyPreference(animationPreference);
    }

    private void ResizeInspect(int width, int height)
    {
        if (_window is null) return;
        var app = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_window.Handle));
        app.Resize(new Windows.Graphics.SizeInt32(width, height));
    }

    /// <summary>
    /// 探针用的假图，默认 320×200 的横图（背景图、横幅那一类）。尺寸可选，好让某一条用例要一张竖的 ——
    /// 详情页那一格封面是 260:375，拿横图去装它，画出来是一条矮带子，截图里看着不像一张海报。
    /// </summary>
    private static async Task<byte[]> FixtureImageAsync(int width = 320, int height = 200)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                var ridge = y > height / 2 + (int)(Math.Sin(x / 40.0) * 30);
                // 夹一下上限：默认那一档（320×200）本来就到不了 255，改了尺寸的（竖图 400×600）会到，而
                // (byte) 溢出是绕回去 —— 一张渐变上突然出现一道暗带，看着像图坏了。
                pixels[offset] = (byte)Math.Min(255, ridge ? 65 : 100 + y / 3);
                pixels[offset + 1] = (byte)Math.Min(255, ridge ? 90 : 125 + y / 3);
                pixels[offset + 2] = (byte)Math.Min(255, ridge ? 45 : 45 + x / 3);
                pixels[offset + 3] = 255;
            }
        }
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[stream.Size];
        reader.ReadBytes(bytes);
        return bytes;
    }
}
