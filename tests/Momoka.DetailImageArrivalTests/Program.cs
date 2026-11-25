using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Windows.UI.ViewManagement;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Test("图片父页Cancel保留预告终端重置调用", () =>
        {
            var vm = new DetailArtworkFixture();
            vm.Cancel();
            Assert.Equal(1, vm.TrailerResets);
        });
        Test("首帧门控前文字已可读且缓存命中的图片也不解码", () =>
        {
            var release = Gate();
            var vm = new DetailArtworkFixture();
            UseGate(vm, release.Task);
            var load = vm.Start(Item());
            Assert.Equal("文字先显示", vm.Title);
            Assert.Equal(0, vm.ShowRequests);
            Assert.Equal(0, vm.DecodeRequests);
            release.SetResult();
            Await(load);
            Assert.True(vm.DecodeRequests > 0);
            Assert.NotNull(vm.StillImage);
            // 2026-11-17 封面取色：同一趟门控之后才起，像素交回视图模型做艺术图那一档的环境底。
            Assert.True(vm.StillColorRequests > 0);
            Assert.NotNull(vm.StillColorPixels);
        });
        Test("门控前快速离页取消不启动图片请求", () =>
        {
            var release = Gate();
            var vm = new DetailArtworkFixture();
            UseGate(vm, release.Task);
            var load = vm.Start(Item());
            vm.Cancel();
            Await(load);
            release.SetResult();
            Assert.Equal(0, vm.ShowRequests);
            Assert.Equal(0, vm.DecodeRequests);
            Assert.Null(vm.StillImage);
            Assert.Equal(0, vm.StillColorRequests);
            Assert.Null(vm.StillColorPixels);
        });
        Test("门控放行时旧图片批次身份已变不发起请求", () =>
        {
            var release = Gate();
            var vm = new DetailArtworkFixture();
            UseGate(vm, release.Task);
            var load = vm.Start(Item());
            vm.ReplaceOwner();
            release.SetResult();
            Await(load);
            Assert.Equal(0, vm.ShowRequests);
            Assert.Equal(0, vm.DecodeRequests);
            Assert.Equal(0, vm.StillColorRequests);
            Assert.Null(vm.StillColorPixels);
        });
        Test("首帧门控前取消也把封面空框收掉", () =>
        {
            var release = Gate();
            var vm = new DetailArtworkFixture();
            UseGate(vm, release.Task);
            var load = vm.Start(Item());
            Assert.True(vm.StillPending, "空框没有在第一帧前站住");
            vm.Cancel();
            Await(load);
            Assert.False(vm.StillPending, "门控前取消后仍留着等待空框");
            release.SetResult();
        });
        Test("离树后迟到的图片收尾也把封面空框收掉", () =>
        {
            var vm = new DetailArtworkFixture { HoldDecode = true };
            var load = vm.Start(Item());
            Assert.True(vm.StillPending, "空框没有在第一帧前站住");
            vm.DetachWithoutCancellingTransport();
            vm.DecodeRelease.SetResult(new());
            Await(load);
            Assert.False(vm.StillPending, "离树后的收尾没有把空框收掉");
        });
        Test("旧批次收尾不按掉新一代的封面空框", () =>
        {
            var vm = new DetailArtworkFixture { HoldDecode = true };
            var first = vm.Start(Item());
            Assert.True(vm.StillPending, "第一代没有站住空框");
            var secondGate = Gate();
            UseGate(vm, secondGate.Task);
            var second = vm.Start(Item());
            vm.DecodeRelease.SetResult(new());
            Await(first);
            Assert.True(vm.StillPending, "旧批次收尾把新一代的空框按掉了");
            secondGate.SetResult();
            Await(second);
            Assert.False(vm.StillPending, "新一代收尾后空框还亮着");
        });
        Test("详情已捕获门控不借新环境或下一次门控提前放行", () =>
        {
            BrowsePresentation.Gate = Task.CompletedTask;
            var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var vm = new DetailArtworkFixture();
            UseGate(vm, first.Task);
            var load = vm.Start(Item());
            UseGate(vm, Task.CompletedTask);
            Assert.Equal("文字先显示", vm.Title);
            Assert.Equal(0, vm.DecodeRequests);
            first.SetResult();
            Await(load);
            Assert.True(vm.DecodeRequests > 0);
        });
        Test("没有显式页面门控的详情不受其它页面环境门控阻塞", () =>
        {
            BrowsePresentation.Gate = new TaskCompletionSource().Task;
            var vm = new DetailArtworkFixture();
            Await(vm.Start(Item()));
            Assert.True(vm.DecodeRequests > 0);
        });
        Test("实际Cancel清上一页门控，重新Attach后的默认加载不等旧Task", () =>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var vm = new DetailArtworkFixture();
            UseGate(vm, gate.Task);
            var load = vm.Start(Item());
            vm.Cancel();
            Await(load);
            vm.Reactivate();
            BrowsePresentation.Gate = new TaskCompletionSource().Task;
            Await(vm.Start(Item()));
            Assert.True(vm.DecodeRequests > 0);
            Assert.False(gate.Task.IsCompleted);
        });
        Test("详情导航接线先Attach重置后捕获当前页面门控再Reload", () =>
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var page = File.ReadAllText(Path.Combine(directory!.FullName, "src", "Momoka.Shell", "Views", "DetailPage.xaml.cs"));
            var navigation = page[page.IndexOf("protected override void OnNavigatedTo", StringComparison.Ordinal)..];
            var attach = navigation.IndexOf("ViewModel.Attach(", StringComparison.Ordinal);
            var gate = navigation.IndexOf("ViewModel.UseImagePresentation(BrowsePresentation.WaitForImagesAsync(this));", StringComparison.Ordinal);
            var reload = navigation.IndexOf("ViewModel.ReloadAsync()", StringComparison.Ordinal);
            Assert.True(attach >= 0 && gate > attach && reload > gate);
            var vm = File.ReadAllText(Path.Combine(directory.FullName, "src", "Momoka.Shell", "ViewModels", "DetailViewModel.cs"));
            var start = vm.IndexOf("    internal void Attach(", StringComparison.Ordinal);
            var end = vm.IndexOf("    private new bool IsCurrent(", start, StringComparison.Ordinal);
            var body = vm[start..end];
            Assert.True(body.Contains("Cancel();", StringComparison.Ordinal));
            Assert.False(body.Contains("LoadArtworkAsync(", StringComparison.Ordinal), "Attach不能在门控设置前发起图片批次");
        });
        Test("慢图片晚于首屏但取消后的响应不能回填", () =>
        {
            BrowsePresentation.Gate = Task.CompletedTask;
            var vm = new DetailArtworkFixture { HoldDecode = true };
            var load = vm.Start(Item());
            Assert.Equal("文字先显示", vm.Title);
            Assert.True(vm.DecodeRequests > 0);
            Assert.Null(vm.StillImage);
            vm.Cancel();
            vm.DecodeRelease.SetResult(new());
            Await(load);
            Assert.Null(vm.StillImage);
            Assert.Null(vm.PlateImage);
        });
        Test("图片响应到达时页面已脱离不能回填", () =>
        {
            BrowsePresentation.Gate = Task.CompletedTask;
            var vm = new DetailArtworkFixture { HoldDecode = true };
            var load = vm.Start(Item());
            vm.DetachWithoutCancellingTransport();
            vm.DecodeRelease.SetResult(new());
            Await(load);
            Assert.Null(vm.StillImage);
            Assert.Null(vm.PlateImage);
        });
        Test("已在树的Source绑定更新立即220ms淡入，不等待ImageOpened", () =>
        {
            ResetUi();
            var image = new Image();
            image.Load();
            using var fade = Fade(image);
            image.Source = new BitmapImage();
            Assert.Equal(0d, image.Opacity);
            var board = Storyboard.Created.Single();
            Assert.True(board.Running);
            Assert.Equal(1, board.Children.Count);
            Assert.Equal("Opacity", board.Children[0].Property);
            Assert.Equal(TimeSpan.FromMilliseconds(220), board.Children[0].Duration.TimeSpan);
            board.Finish();
            Assert.Equal(1d, image.Opacity);
            Assert.Equal(0, board.CompletionSubscriptions);
        });
        Test("同图重复绑定和Loaded不重新闪空", () =>
        {
            ResetUi();
            var picture = new BitmapImage();
            var image = new Image { Source = picture };
            using var fade = Fade(image);
            image.Load();
            image.Source = picture;
            Assert.Equal(1d, image.Opacity);
            Assert.Equal(0, Storyboard.Created.Count);
            image.Source = new BitmapImage();
            Storyboard.Created.Single().Finish();
            var source = image.Source;
            image.Source = source;
            image.Unload();
            image.Load();
            Assert.Equal(1, Storyboard.Created.Count);
            Assert.Equal(1d, image.Opacity);
        });
        Test("同一卡片换封面尺寸保留可见且停止首次未完成淡入", () =>
        {
            ResetUi();
            var identity = new object();
            var image = new Image();
            image.Load();
            using var fade = new ImageArrivalFade(image, () => identity);
            image.Source = new BitmapImage();
            var first = Storyboard.Created.Single();
            Assert.Equal(0d, image.Opacity);
            image.Source = new BitmapImage();
            Assert.Equal(1d, image.Opacity);
            Assert.False(first.Running);
            Assert.Equal(0, first.CompletionSubscriptions);
            Assert.Equal(0, HomeMotion.Subscriptions);
            Assert.Equal(0, UISettings.Created.Sum(settings => settings.Subscriptions));
            image.Source = new BitmapImage();
            Assert.Equal(1, Storyboard.Created.Count);
            Assert.Equal(1d, image.Opacity);
        });
        Test("已有缓存图的卡片更新清晰度不重播淡入", () =>
        {
            ResetUi();
            var identity = new object();
            var image = new Image { Source = new BitmapImage() };
            image.Load();
            using var fade = new ImageArrivalFade(image, () => identity);
            image.Source = new BitmapImage();
            Assert.Equal(0, Storyboard.Created.Count);
            Assert.Equal(1d, image.Opacity);
        });
        Test("卡片换项和真正清空后的首次图片仍淡入", () =>
        {
            ResetUi();
            object? identity = new object();
            var image = new Image { Source = new BitmapImage() };
            image.Load();
            using var fade = new ImageArrivalFade(image, () => identity);
            identity = new object();
            image.Source = new BitmapImage();
            Assert.Equal(0d, image.Opacity);
            Assert.Equal(1, Storyboard.Created.Count);
            image.Source = null;
            Assert.Equal(1d, image.Opacity);
            image.Source = new BitmapImage();
            Assert.Equal(0d, image.Opacity);
            Assert.Equal(2, Storyboard.Created.Count);
            fade.Stop();
            identity = null;
            image.Source = new BitmapImage();
            Assert.Equal(0d, image.Opacity);
            Assert.Equal(3, Storyboard.Created.Count);
        });
        Test("主海报及八个拼图表面都按Card身份区分清晰度替换与换卡", () =>
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var source = File.ReadAllText(Path.Combine(directory!.FullName, "src", "Momoka.Shell", "Views", "PosterCard.xaml.cs"));
            Assert.True(source.Contains("new ImageArrivalFade(Poster, () => Card)", StringComparison.Ordinal));
            foreach (var name in new[] { "FnNasPosterBackground", "FolderPoster0", "FolderPoster1", "FolderPoster2", "FolderPoster3",
                "LibraryPoster0", "LibraryPoster1", "LibraryPoster2" })
                Assert.True(source.Contains($"new({name}, () => Card)", StringComparison.Ordinal), $"{name}缺少当前卡片身份");
        });
        Test("离树迟到Source直接visible，Loaded不闪空", () =>
        {
            ResetUi();
            var image = new Image();
            using var fade = Fade(image);
            image.Source = new BitmapImage();
            Assert.Equal(1d, image.Opacity);
            image.Load();
            Assert.Equal(0, Storyboard.Created.Count);
            Assert.Equal(1d, image.Opacity);
        });
        Test("空图和静止缓存图不持有全局或系统监听", () =>
        {
            ResetUi();
            var image = new Image();
            image.Load();
            using var fade = Fade(image);
            Assert.Equal(0, HomeMotion.Subscriptions);
            Assert.Equal(0, UISettings.Created.Sum(settings => settings.Subscriptions));
            image.Source = new BitmapImage();
            Assert.Equal(1, HomeMotion.Subscriptions);
            Storyboard.Created.Single().Finish();
            Assert.Equal(0, HomeMotion.Subscriptions);
            Assert.Equal(0, UISettings.Created.Sum(settings => settings.Subscriptions));
        });
        Test("空图取消和失败都settle并清完成订阅", () =>
        {
            ResetUi();
            var image = new Image();
            image.Load();
            using var fade = Fade(image);
            image.Source = new BitmapImage();
            var first = Storyboard.Created.Single();
            image.Source = null;
            Assert.Equal(1d, image.Opacity);
            Assert.False(first.Running);
            Assert.Equal(0, first.CompletionSubscriptions);
            image.Source = new BitmapImage();
            image.Fail();
            Assert.Equal(1d, image.Opacity);
            Assert.Equal(0, Storyboard.Created.Last().CompletionSubscriptions);
        });
        Test("关闭应用和系统动画即时落定不残留透明", () =>
        {
            ResetUi();
            var image = new Image();
            image.Load();
            using var fade = Fade(image);
            image.Source = new BitmapImage();
            HomeMotion.Change(false);
            Assert.Equal(1d, image.Opacity);
            Assert.Equal(0, Storyboard.Created.Last().CompletionSubscriptions);
            HomeMotion.Change(true);
            image.Source = new BitmapImage();
            UISettings.Created.Last().Change(false);
            Assert.Equal(1d, image.Opacity);
            image.Source = new BitmapImage();
            Assert.Equal(2, Storyboard.Created.Count);
        });
        Test("真离树清外部订阅，回收幻影不拆绑定，重入可继续淡入", () =>
        {
            ResetUi();
            var image = new Image();
            image.Load();
            using var fade = Fade(image);
            image.Source = new BitmapImage();
            image.Unload(phantom: true);
            Assert.Equal(1, image.PropertySubscriptions);
            Assert.Equal(1, HomeMotion.Subscriptions);
            image.Source = new BitmapImage();
            Assert.Equal(2, Storyboard.Created.Count);
            image.Unload();
            Assert.Equal(1d, image.Opacity);
            Assert.Equal(0, HomeMotion.Subscriptions);
            Assert.Equal(0, UISettings.Created.Last().Subscriptions);
            image.Load();
            image.Source = new BitmapImage();
            Assert.Equal(3, Storyboard.Created.Count);
            fade.Dispose();
            Assert.Equal(0, image.PropertySubscriptions);
            Assert.Equal(0, HomeMotion.Subscriptions);
            Assert.Equal(1d, image.Opacity);
        });
        Test("ImageBrush晚到只淡入专用图片宿主，保留画刷原有0.55淡底", () =>
        {
            ResetUi();
            var host = new FrameworkElement();
            host.Load();
            var brush = new ImageBrush { Opacity = 0.55 };
            using var fade = Fade(brush, host);
            brush.ImageSource = new BitmapImage();
            Assert.Equal(0.55, brush.Opacity);
            Assert.Equal(0d, host.Opacity);
            var board = Storyboard.Created.Single();
            Assert.True(ReferenceEquals(host, board.Children[0].Target));
            Assert.Equal(1d, board.Children[0].To);
            board.Finish();
            Assert.Equal(0.55, brush.Opacity);
            Assert.Equal(1d, host.Opacity);
            brush.ImageSource = new BitmapImage();
            brush.Fail();
            Assert.Equal(0.55, brush.Opacity);
            Assert.Equal(1d, host.Opacity);
        });
        Test("Storyboard启动失败也清理，不留下透明与完成订阅", () =>
        {
            ResetUi();
            var image = new Image();
            image.Load();
            using var fade = Fade(image);
            Storyboard.ThrowOnBegin = true;
            image.Source = new BitmapImage();
            Assert.Equal(1d, image.Opacity);
            Assert.Equal(0, Storyboard.Created.Single().CompletionSubscriptions);
        });
        Test("详情与商店背景六种图片共用实际接线，Release清理且重新Attach可再次淡入", () =>
        {
            ResetUi();
            var page = new DetailPage();
            var images = new[] { page.PosterImage, page.TitlePlate, page.CornerArt, page.FooterBanner };
            foreach (var image in images) image.Load();
            page.BackdropPicture.Load();
            page.StoreBackdropPicture.Load();
            page.Watch();
            page.Watch();
            foreach (var image in images) image.Source = new BitmapImage();
            page.BackdropImageBrush.ImageSource = new BitmapImage();
            page.StoreBackdropImageBrush.ImageSource = new BitmapImage();
            Assert.True(page.ArtworkArrivalRunning);
            Assert.Equal(6, Storyboard.Created.Count);
            Assert.Equal(6, HomeMotion.Subscriptions);
            page.Release();
            Assert.False(page.ArtworkArrivalRunning);
            Assert.Equal(0, HomeMotion.Subscriptions);
            Assert.Equal(0, page.BackdropImageBrush.PropertySubscriptions);
            Assert.Equal(0, page.StoreBackdropImageBrush.PropertySubscriptions);
            foreach (var image in images)
            {
                Assert.Equal(0, image.PropertySubscriptions);
                Assert.Equal(1d, image.Opacity);
            }
            page.Watch();
            Assert.False(page.ArtworkArrivalRunning, "已有缓存Source保持可见");
            page.PosterImage.Source = new BitmapImage();
            Assert.True(page.ArtworkArrivalRunning);
            page.Release();
            Assert.Equal(0, HomeMotion.Subscriptions);
        });
        Test("关联封面高清替换保持不透明，新页面停留恢复正常淡入", () =>
        {
            ResetUi();
            var page = new DetailPage();
            page.PosterImage.Load();
            page.Watch();
            page.PosterImage.Source = new BitmapImage();
            Assert.Equal(0d, page.PosterImage.Opacity);
            page.StopConnectedPosterArrival();
            Assert.Equal(1d, page.PosterImage.Opacity);
            var count = Storyboard.Created.Count;
            page.PosterImage.Source = new BitmapImage();
            Assert.Equal(count, Storyboard.Created.Count);
            Assert.Equal(1d, page.PosterImage.Opacity);
            page.Release();
            page.Watch();
            page.PosterImage.Source = new BitmapImage();
            Assert.Equal(0d, page.PosterImage.Opacity);
            page.Release();
        });
        Test("真实源封面保留方法在离树清图时保持可见，释放与回收换卡不借旧图", () =>
        {
            ResetUi();
            var poster = new PosterCard { Card = new CardItem() };
            poster.Poster.Load();
            var picture = new BitmapImage();
            poster.Card.Poster = picture;
            poster.Poster.Source = picture;
            var release = poster.RetainConnectedPoster();
            Assert.Equal(1d, poster.Poster.Opacity);
            poster.Card.Poster = null;
            poster.Poster.Source = null;
            Assert.True(ReferenceEquals(picture, poster.Poster.Source));
            Assert.Equal(1d, poster.Poster.Opacity);
            release();
            Assert.Null(poster.Poster.Source);
            poster.Card.Poster = picture;
            poster.Poster.Source = picture;
            release = poster.RetainConnectedPoster();
            poster.Card = new CardItem();
            poster.Poster.Source = null;
            Assert.Null(poster.Poster.Source);
            release();
            Assert.Null(poster.Poster.Source);
        });
        Test("真实详情封面交接在网络门控前显示并先更新尺寸，离页和错项不借图", () =>
        {
            var gate = Gate();
            var vm = new DetailArtworkFixture();
            UseGate(vm, gate.Task);
            var item = Item();
            var load = vm.Start(item);
            var picture = new BitmapSource { PixelWidth = 170, PixelHeight = 255 };
            Assert.True(vm.UseConnectedPoster(item, picture));
            Assert.True(ReferenceEquals(picture, vm.StillImage));
            Assert.Equal((170, 255), vm.RefittedPixels!.Value);
            Assert.Equal(0, vm.DecodeRequests);
            Assert.False(vm.UseConnectedPoster(new EmbyItem { Id = "another-item" }, new BitmapSource()));
            vm.Cancel();
            Assert.False(vm.UseConnectedPoster(item, new BitmapSource()));
            gate.SetResult();
            Await(load);
        });
        AnimatedPosterTests.Register();
        return Run();
    }
    private static EmbyItem Item() => new()
    {
        Id = "fixture",
        Name = "文字先显示",
        Type = EmbyItemType.Movie,
        ImageTags = new(StringComparer.Ordinal) { [EmbyImageStore.Logo] = "logo", [EmbyImageStore.Primary] = "cover" }
    };
    private static TaskCompletionSource Gate()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Native Frame callbacks do not inherit the caller's AsyncLocal context.
        BrowsePresentation.Gate = Task.CompletedTask;
        return release;
    }
    private static void UseGate(DetailArtworkFixture vm, Task gate)
    {
        var method = typeof(DetailArtworkFixture).GetMethod("UseImagePresentation", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new AssertionException("生产VM缺少显式页面门控入口");
        method.Invoke(vm, [gate]);
    }
    // The standalone offline runner deliberately waits at its console boundary, never in production UI.
#pragma warning disable RS0030
    private static void Await(Task task) => task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
#pragma warning restore RS0030
    private static IDisposable Fade(params object[] args)
    {
        var type = typeof(Program).Assembly.GetType("Momoka.Shell.Views.ImageArrivalFade")
            ?? throw new AssertionException("生产ImageArrivalFade不存在");
        return (IDisposable)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: args, culture: null)!;
    }
    private static void ResetUi()
    {
        Storyboard.Created.Clear();
        Storyboard.ThrowOnBegin = false;
        UISettings.Enabled = true;
        HomeMotion.PreferenceEnabled = true;
        Assert.Equal(0, HomeMotion.Subscriptions, "前一个控件必须已清理外部订阅");
    }
}
