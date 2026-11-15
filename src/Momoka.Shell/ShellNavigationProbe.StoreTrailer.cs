using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.Views;
using Windows.Media.Core;
using Windows.Media.Playback;
using MediaSource = Windows.Media.Core.MediaSource;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task StoreTrailerAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        fixture.Settings.Ui.EnableImmersive = false;
        var poster = await FixtureImageAsync(260, 375);
        var backdrop = await StoreBackdropFixtureAsync();
        var movie = new EmbyItem
        {
            Id = "preview-movie",
            Name = "商店预告 · 匿名本地",
            Type = EmbyItemType.Movie,
            ImageTags = new() { ["Primary"] = "poster" },
            BackdropImageTags = ["backdrop"],
            Overview = string.Concat(Enumerable.Repeat("两行剧情说明之外的全文通过原生提示阅读。", 8)),
            MediaSources = [new() { Id = "movie", Container = "mkv" }]
        };
        fixture.Transport.Reply = (request, _) => request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal)
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(
                request.RequestUri.AbsolutePath.Contains("/Images/Primary", StringComparison.Ordinal) ? poster : backdrop)
            })
            : request.RequestUri!.AbsolutePath.EndsWith("/Items/preview-movie", StringComparison.Ordinal)
            ? Task.FromResult(Json(movie)) : Task.FromResult(FakeTransport.Standard(request));
        var host = new Grid { Margin = new Thickness(0, 48, 0, 0) };
        var frame = new Frame();
        host.Children.Add(frame);
        _root.Children.Add(host);
        try
        {
            frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, movie));
            var page = (DetailPage)frame.Content;
            page.AttachWindow(_window!);
            await UntilAsync(() => page.IsReady && page.IsLoaded && page.ViewModel.StillImage is not null && page.ViewModel.HeroImage is not null);
            await ResizeResponsiveAsync(page, 1280, 900);
            Require(Math.Abs(Get<Grid>(page, "StoreBackdrop").ActualWidth - TrailerPreview.BackgroundWidth(1280)) < 1,
                "静态背景未与预告共用右侧区域");
            await SaveProbeScreenAsync("store-backdrop-wide-screen.png");
            var overview = Get<TextBlock>(page, "Overview");
            var overviewBody = Get<Grid>(page, "OverviewBody");
            var overviewFooter = Get<StackPanel>(page, "OverviewFooter");
            var expectedLines = DetailHero.OverviewLines(Math.Max(0, overviewBody.ActualHeight - overviewFooter.ActualHeight
                - overviewFooter.Margin.Top - overviewFooter.Margin.Bottom), overview.LineHeight);
            Require(overview.MaxLines == expectedLines && page.ViewModel.ReadMoreVisibility == Visibility.Collapsed,
                "商店剧情未跟随封面右栏高度或保留原生提示模式");
            Call(page, "StopStoreTrailer");
            page.ViewModel.CurrentItem!.RemoteTrailers = [new() { Url = "https://youtu.be/Abc_123-xyz" }];
            Field(page, "_storeTrailerSubject", page.ViewModel.CurrentItem);
            var generation = Get<int>(page, "_storeTrailerGeneration");
            var video = Get<Grid>(page, "StoreTrailerContent");
            var trailerHost = Get<Grid>(page, "StoreTrailerHost");
            Call(page, "LayoutStoreTrailer");
            var source = MediaSource.CreateFromUri(new Uri(Path.Combine(AppContext.BaseDirectory, "ProbeAssets", "store-trailer.mp4")));
            var captions = new TimedMetadataTrack("fixture-subtitle", "zh", TimedMetadataKind.Subtitle);
            captions.Label = "匿名字幕";
            var cue = new TimedTextCue { StartTime = TimeSpan.Zero, Duration = TimeSpan.FromSeconds(2) };
            cue.Lines.Add(new TimedTextLine { Text = "独立预告字幕" });
            captions.AddCue(cue);
            source.ExternalTimedMetadataTracks.Add(captions);
            Call(page, "StartStoreMedia", source, generation);
            await UntilAsync(() => Get<bool>(page, "_storeTrailerOpened"));
            var player = Get<MediaPlayer>(page, "_storeTrailerPlayer");
            await UntilAsync(() => player.PlaybackSession.NaturalVideoWidth == 160 && player.PlaybackSession.Position > TimeSpan.FromMilliseconds(100));
            Require(player.IsMuted && player.IsLoopingEnabled, "预告没有默认静音循环");
            var element = Get<MediaPlayerElement>(page, "_storeTrailerElement");
            Require(!element.AreTransportControlsEnabled && !element.IsHitTestVisible && !element.IsTabStop,
                "背景预告仍接收指针/键盘或显示内置控件");
            Require(element.Stretch == Stretch.UniformToFill
                && Math.Abs(trailerHost.ActualWidth - TrailerPreview.BackgroundWidth(page.ViewModel.PageWidth)) < 1
                && trailerHost.HorizontalAlignment == HorizontalAlignment.Right && trailerHost.ActualWidth < page.ViewModel.PageWidth,
                "预告未限制在顶部右侧背景");
            Require(Math.Abs(video.ActualHeight - trailerHost.ActualHeight) < 1 && trailerHost.Margin == new Thickness(0, 0, DetailHero.StorePreviewGap, 0),
                "预告仍保留独立面板边距或控件占位");
            var foundation = Get<Border>(page, "StoreTrailerFoundation");
            var fade = Get<Border>(page, "StoreTrailerFade");
            Require(foundation.Visibility == Visibility.Visible
                && Math.Abs(foundation.ActualWidth - page.ViewModel.PageWidth) < 1
                && Math.Abs(foundation.ActualHeight - trailerHost.ActualHeight - StoreBackdropBlend.FoundationTail) < 1
                && foundation.Background is ImageBrush && fade.Background is ImageBrush
                && Math.Abs(fade.ActualWidth - video.ActualWidth) < 1 && Math.Abs(fade.ActualHeight - video.ActualHeight) < 1,
                "视频局部底层或叠图没有匹配实际视口");
            var audio = Get<AppBarButton>(page, "StoreTrailerAudio");
            var subtitle = Get<AppBarButton>(page, "StoreTrailerSubtitles");
            await UntilAsync(() => audio.Visibility == Visibility.Visible && subtitle.Visibility == Visibility.Visible);
            Call(page, "OnStoreTrailerAudio", audio, new RoutedEventArgs());
            Require(Get<MenuFlyout>(page, "_storeTrailerTracksMenu").Items.Count == 2, "预告音轨菜单没有列出自身两轨");
            Get<MenuFlyout>(page, "_storeTrailerTracksMenu").Hide();
            Call(page, "OnStoreTrailerSubtitles", subtitle, new RoutedEventArgs());
            Require(Get<MenuFlyout>(page, "_storeTrailerTracksMenu").Items.Count == 2, "字幕菜单没有关闭与实际字幕轨");
            Get<MenuFlyout>(page, "_storeTrailerTracksMenu").Hide();
            await Task.Delay(300);
            foreach (var button in Get<CommandBar>(page, "StoreTrailerControls").PrimaryCommands.OfType<AppBarButton>())
            {
                Require(!string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(button)), "预告控制缺少UIA标识");
                Require(button.ActualWidth == 40 && button.ActualHeight == 40 && button.CornerRadius.TopLeft == 20, "预告控制不是40圆形");
            }
            await SaveProbeScreenAsync("store-trailer-decoded-screen.png");
            Call(page, "OnStoreTrailerPause", Get<AppBarButton>(page, "StoreTrailerPause"), new RoutedEventArgs());
            await UntilAsync(() => player.PlaybackSession.PlaybackState == MediaPlaybackState.Paused);
            Field(page, "_storeTrailerScrollPaused", true);
            Call(page, "OnStoreTrailerPause", Get<AppBarButton>(page, "StoreTrailerPause"), new RoutedEventArgs());
            await Task.Delay(100);
            Require(player.PlaybackSession.PlaybackState == MediaPlaybackState.Paused, "解除用户暂停绕过了离屏暂停");
            Field(page, "_storeTrailerScrollPaused", false);
            Call(page, "OnStoreTrailerMute", Get<AppBarButton>(page, "StoreTrailerMute"), new RoutedEventArgs());
            Require(!player.IsMuted, "预告静音切换没有生效");
            player.IsMuted = true;
            await StoreTrailerWebAsync(page);
            Require(fixture.Actions.Plays == 0 && !fixture.Transport.Requests.Any(request => request.Contains("/Playing", StringComparison.Ordinal)), "预告调用了正片播放或上报");
            page.SetStorePreviewActive(false);
            Require(trailerHost.Visibility == Visibility.Collapsed && Get<MediaPlayer?>(page, "_storeTrailerPlayer") is null
                && foundation.Visibility == Visibility.Collapsed && foundation.Background is null && fade.Background is null,
                "主播放开始没有释放独立预告或局部渐变");
            var rejected = false;
            try { Require(trailerHost.Visibility == Visibility.Visible, "负向对照：已释放预告不可见"); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "预告生命周期检查没有拒绝反例");
            page.Release();
        }
        finally
        {
            if (frame.Content is IShellContent content) content.Release();
            _root.Children.Remove(host);
        }
    }

    // 明暗细条作为真实模糊对照，避免深色渐变让未生效的亚克力也看起来通过。
    private static async Task<byte[]> StoreBackdropFixtureAsync(bool ambient = false)
    {
        const int width = 1280;
        const int height = 720;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                var blue = ambient ? x < width / 2 : x % 32 < 16;
                pixels[offset] = blue ? (byte)240 : (byte)20;
                pixels[offset + 1] = blue ? (byte)130 : (byte)220;
                pixels[offset + 2] = blue ? (byte)20 : (byte)240;
                pixels[offset + 3] = 255;
                if (ambient)
                {
                    // 匿名的蓝底与右侧红色光团，用连续色区检查扩散；硬切双色只用于Core的边界回归。
                    var dx = (x / (double)width - 0.72) / 0.25;
                    var dy = (y / (double)height - 0.36) / 0.5;
                    var glow = Math.Exp(-0.5 * (dx * dx + dy * dy));
                    pixels[offset] = (byte)Math.Round(242 - 136 * glow);
                    pixels[offset + 1] = (byte)Math.Round(191 - 107 * glow);
                    pixels[offset + 2] = (byte)Math.Round(66 + 181 * glow);
                }
            }
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Straight,
            width, height, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var reader = new Windows.Storage.Streams.DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[stream.Size];
        reader.ReadBytes(bytes);
        return bytes;
    }
}
