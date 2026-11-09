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
        Test("首帧门控前文字已可读且缓存命中的图片也不解码", () =>
        {
            var release = Gate();
            var vm = new DetailArtworkFixture();
            var load = vm.Start(Item());
            Assert.Equal("文字先显示", vm.Title);
            Assert.Equal(0, vm.ShowRequests);
            Assert.Equal(0, vm.DecodeRequests);
            release.SetResult();
            Await(load);
            Assert.True(vm.DecodeRequests > 0);
            Assert.NotNull(vm.StillImage);
        });
        Test("门控前快速离页取消不启动图片请求", () =>
        {
            var release = Gate();
            var vm = new DetailArtworkFixture();
            var load = vm.Start(Item());
            vm.Cancel();
            Await(load);
            release.SetResult();
            Assert.Equal(0, vm.ShowRequests);
            Assert.Equal(0, vm.DecodeRequests);
            Assert.Null(vm.StillImage);
        });
        Test("门控放行时旧图片批次身份已变不发起请求", () =>
        {
            var release = Gate();
            var vm = new DetailArtworkFixture();
            var load = vm.Start(Item());
            vm.ReplaceOwner();
            release.SetResult();
            Await(load);
            Assert.Equal(0, vm.ShowRequests);
            Assert.Equal(0, vm.DecodeRequests);
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
        Test("详情五种图片共用实际接线，Release清理且重新Attach可再次淡入", () =>
        {
            ResetUi();
            var page = new DetailPage();
            var images = new[] { page.PosterImage, page.TitlePlate, page.CornerArt, page.FooterBanner };
            foreach (var image in images) image.Load();
            page.BackdropPicture.Load();
            page.Watch();
            page.Watch();
            foreach (var image in images) image.Source = new BitmapImage();
            page.BackdropImageBrush.ImageSource = new BitmapImage();
            Assert.True(page.ArtworkArrivalRunning);
            Assert.Equal(5, Storyboard.Created.Count);
            Assert.Equal(5, HomeMotion.Subscriptions);
            page.Release();
            Assert.False(page.ArtworkArrivalRunning);
            Assert.Equal(0, HomeMotion.Subscriptions);
            Assert.Equal(0, page.BackdropImageBrush.PropertySubscriptions);
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
        return Run();
    }
    private static EmbyItem Item() => new()
    {
        Id = "fixture", Name = "文字先显示", Type = EmbyItemType.Movie,
        ImageTags = new(StringComparer.Ordinal) { [EmbyImageStore.Logo] = "logo", [EmbyImageStore.Primary] = "cover" }
    };
    private static TaskCompletionSource Gate()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BrowsePresentation.Gate = release.Task;
        return release;
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
