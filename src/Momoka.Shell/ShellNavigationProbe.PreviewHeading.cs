using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>实际预览收起、媒体库详情事件与Frame交接；只用内存GET，不移动指针或播放。</summary>
    private async Task PreviewHeadingMotionAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync("preview-heading.invalid");
        var item = new EmbyItem
        {
            Id = "heading-" + Guid.NewGuid().ToString("N"), Name = "匿名悬停徽标与片名交接",
            Type = EmbyItemType.Movie, MediaType = "Video", Overview = "只验证预览标题与徽标的实际导航交接。",
            ImageTags = new() { ["Primary"] = "poster", ["Logo"] = "logo", ["Thumb"] = "thumb" },
            BackdropImageTags = ["backdrop"]
        };
        var posterBytes = await FixtureImageAsync(260, 375);
        var logoBytes = await FixtureImageAsync(420, 88);
        fixture.Transport.Reply = (request, _) =>
        {
            Require(request.Method == HttpMethod.Get, "标题交接探针拒绝写入");
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new ByteArrayContent(path.EndsWith("/Images/Logo", StringComparison.Ordinal) ? logoBytes : posterBytes) });
            if (path.EndsWith("/Items/" + item.Id, StringComparison.Ordinal)) return Task.FromResult(Json(item));
            if (path.EndsWith("/Items", StringComparison.Ordinal))
                return Task.FromResult(Json(new ItemsResult { Items = [item], TotalRecordCount = 1 }));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var frame = new Frame { CacheSize = 2 };
        _root.Children.Add(frame);
        var savedMotion = HomeMotion.PreferenceEnabled;
        DetailPage? target = null;
        PosterCard? source = null;
        Storyboard? board = null;
        var samples = new List<object>();
        try
        {
            ResizeProbeClient(1280, 850);
            HomeMotion.ApplyPreference(true);
            Require(HomeMotion.AnimationsEnabled, "系统关闭动画，标题飞行原生覆盖不可执行");
            Require(frame.Navigate(typeof(LibraryPage), new LibraryRequest
            {
                Services = fixture.Services, Title = "匿名标题交接库", ParentId = "heading-library",
                CollectionType = "movies", Source = fixture.Session.Capture()
            }, new SuppressNavigationTransitionInfo()), "标题交接源库导航失败");
            var library = (LibraryPage)frame.Content;
            await UntilAsync(() => library.IsReady && !library.ViewModel.Busy && library.ViewModel.Cards.Count == 1);
            await LayoutAsync(library);
            var wall = Get<ItemsView>(library, "CardWall");
            await UntilAsync(() =>
            {
                source = Descendants(wall).OfType<PosterCard>().FirstOrDefault(card => card.Card?.Item.Id == item.Id
                    && card.ConnectedPoster.IsLoaded && card.ConnectedPoster.ActualWidth > 0);
                return source is not null;
            });
            await source!.Card!.EnsurePosterAsync();
            await UntilAsync(() => source.ConnectedPoster.Source is BitmapSource { PixelWidth: > 0 });
            item = source.Card!.Item;
            // 原生产LibraryPage事件先Prepare并取走capture，再由假外壳的OpenItem驱动真实Frame导航。
            // 不直接Prepare/Attach/Begin，不另造导航事件。
            fixture.Actions.CaptureOpen = opened =>
            {
                Require(source!.TakePreviewHeading() is null, "生产媒体库事件没有取走悬停交接");
                Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, opened),
                    new SuppressNavigationTransitionInfo()), "悬停详情正式导航失败");
                target = (DetailPage)frame.Content;
                if (target.IsLoaded) Freeze(target);
                else target.Loaded += FreezeLoaded;
            };
            for (var round = 0; round < 3; round++)
            {
                HomeMotion.ApplyPreference(true);
                target = null;
                board = null;
                Require(source.ShowPreviewForShot() is not null, "真实PosterCard预览没有打开");
                var preview = Get<CardPreview>(source, "_preview");
                var popup = Get<Popup>(source, "_previewHost");
                preview.IsHitTestVisible = false; // 显式状态防用户指针影响，UIA仍走真实Click。
                await UntilAsync(() => popup.IsOpen && !Get<bool>(preview, "_running")
                    && Get<Image>(preview, "LogoImage").Source is BitmapImage { PixelWidth: > 0 });
                await LayoutAsync(preview);
                var capture = preview.CaptureHeading(popup.HorizontalOffset, popup.VerticalOffset, source.Card!.Item);
                Require(capture is { Plate: not null, PlatePicture: not null } && capture.Title.Width > 0 && capture.TitleFontSize > 0,
                    "真实预览没有量到完整片名与徽标");
                if (round == 0) await SaveProbeScreenAsync("preview-heading-source-screen.png");
                var opened = fixture.Actions.Opened;
                ((IInvokeProvider)new ButtonAutomationPeer(Get<Button>(preview, "DetailsButton"))
                    .GetPattern(PatternInterface.Invoke)).Invoke();
                await UntilAsync(() => target is not null && target.IsLoaded && board is not null
                    && fixture.Actions.Opened == opened + 1 && !popup.IsOpen);
                Require(Get<object?>(target!, "_previewHeadingFlight") is not null, "生产Attach没有启动标题徽标交接");
                var title = Get<TextBlock>(target!, "TitleText");
                var plate = Get<Image>(target!, "TitlePlate");
                Require(target!.ViewModel.CurrentItem?.IdentityKey == item.IdentityKey
                    && plate.Source is BitmapImage { PixelWidth: > 0 }, "详情身份或徽标没有接上");
                var initial = ReadHeading(title, plate);
                if (round == 0)
                {
                    Require(initial.TitleX != 0 || initial.TitleY != 0, "片名起跑没有实际偏移");
                    Require(initial.PlateX != 0 || initial.PlateY != 0, "徽标起跑没有实际偏移");
                    await SaveProbeScreenAsync("preview-heading-start-screen.png");
                    // 官方Storyboard.Pause/SeekAlignedToLastTick同步定位同一生产时钟；冻结取样，不是FPS测量。
                    board!.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(160));
                    await LayoutAsync(target);
                    var middle = ReadHeading(title, plate);
                    Require(middle.TitleOpacity is > 0 and < 1 && middle.PlateOpacity is > 0 and < 1,
                        "标题或徽标没有真实中间透明度");
                    Require(Math.Abs(middle.TitleX) + Math.Abs(middle.TitleY) < Math.Abs(initial.TitleX) + Math.Abs(initial.TitleY)
                        && Math.Abs(middle.PlateX) + Math.Abs(middle.PlateY) < Math.Abs(initial.PlateX) + Math.Abs(initial.PlateY),
                        "标题或徽标没有向目标落点前进");
                    await SaveProbeScreenAsync("preview-heading-middle-screen.png");
                    board.Resume();
                    await UntilAsync(() => Get<object?>(target, "_previewHeadingFlight") is null);
                    RequireHeadingSettled(title, plate);
                    await SaveProbeScreenAsync("preview-heading-completed-screen.png");
                    samples.Add(new { phase = "completed", capture!.Title, capture.Plate, capture.TitleFontSize,
                        initial, middle, completed = true, freezeSampling = true });
                }
                else if (round == 1)
                {
                    HomeMotion.ApplyPreference(false);
                    Require(Get<object?>(target, "_previewHeadingFlight") is null, "关闭动画没有停止当前标题飞行");
                    RequireHeadingSettled(title, plate);
                    samples.Add(new { phase = "motion-off", restored = true });
                }
                if (round == 2)
                {
                    frame.GoBack(new SuppressNavigationTransitionInfo());
                    Require(Get<object?>(target, "_previewHeadingFlight") is null, "离页没有清理标题飞行");
                    RequireHeadingSettled(title, plate);
                    samples.Add(new { phase = "leave", restored = true });
                }
                else frame.GoBack(new SuppressNavigationTransitionInfo());
                await UntilAsync(() => ReferenceEquals(frame.Content, library) && library.IsLoaded && !library.ViewModel.Busy);
                await LayoutAsync(library);
            }
            Require(fixture.Actions.Plays == 0, "标题交接探针请求了播放");
            await WritePerformanceEvidenceAsync("preview-heading-motion.json", new
            { samples, RealNetwork = false, MediaLoaded = false, FrameNavigated = true, SourceCapture = "actual CardPreview" });
        }
        finally
        {
            fixture.Actions.CaptureOpen = null;
            target?.StopPreviewHeadingMotion();
            if (source is not null) Call(source, "SuppressHoverDwell");
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            HomeMotion.ApplyPreference(savedMotion);
        }

        void FreezeLoaded(object sender, RoutedEventArgs args)
        {
            var page = (DetailPage)sender;
            page.Loaded -= FreezeLoaded;
            Freeze(page);
        }
        void Freeze(DetailPage page)
        {
            var flight = Get<object?>(page, "_previewHeadingFlight");
            if (flight is null) return;
            board = Get<Storyboard>(flight, "_board");
            board.Pause();
            board.SeekAlignedToLastTick(TimeSpan.Zero);
        }
    }

    private static PreviewHeadingPose ReadHeading(TextBlock title, Image plate)
    {
        Require(title.RenderTransform is CompositeTransform && plate.RenderTransform is CompositeTransform,
            "真实标题或徽标没有生产CompositeTransform");
        var text = (CompositeTransform)title.RenderTransform;
        var logo = (CompositeTransform)plate.RenderTransform;
        return new(text.TranslateX, text.TranslateY, text.ScaleX, title.Opacity,
            logo.TranslateX, logo.TranslateY, logo.ScaleX, plate.Opacity);
    }

    private static void RequireHeadingSettled(TextBlock title, Image plate) =>
        Require(title.RenderTransform is null && plate.RenderTransform is null && title.Opacity == 1 && plate.Opacity == 1,
            "标题徽标完成/关动画/离页后没有恢复静止终值");

    private sealed record PreviewHeadingPose(double TitleX, double TitleY, double TitleScale, double TitleOpacity,
        double PlateX, double PlateY, double PlateScale, double PlateOpacity);
}
