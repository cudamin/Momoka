using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Shell.Views;
using Windows.Foundation;
using Windows.UI.ViewManagement;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class PreviewHeadingTransitionTests
{
    internal static void Register()
    {
        Test("标题票据：新页未挂树不消耗票，生产Loaded先于后订阅者起跑一次", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            Assert.True(fixture.Attach());
            Assert.Equal(0, fixture.StartCount);
            Assert.True(fixture.Target.HasLoadedHandlers && fixture.Target.HasLayoutHandlers);
            RoutedEventHandler observer = (_, _) => Assert.True(fixture.Target.HeadingRunning);
            fixture.Target.Loaded += observer;
            fixture.Mount();
            fixture.Target.Load();
            fixture.Target.Loaded -= observer;
            fixture.Target.Layout();
            fixture.Target.Load();
            Assert.Equal(1, fixture.StartCount);
            Assert.False(PreviewHeadingTransition.Attach(fixture.Target, fixture.Item));
            fixture.RequireWaitingDetached();
        });
        Test("标题票据：Loaded后首次布局仍可接住，同页重复Attach不重复成功起跑", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            Assert.True(fixture.Attach());
            Assert.True(fixture.Attach());
            fixture.Target.TitleText.ActualWidth = 0;
            fixture.Mount();
            fixture.Target.Load();
            Assert.Equal(0, fixture.StartCount);
            Assert.True(fixture.Target.HasLayoutHandlers);
            fixture.Target.TitleText.ActualWidth = 240;
            fixture.Target.Layout();
            fixture.Target.Layout();
            Assert.Equal(1, fixture.StartCount);
            Assert.True(fixture.Target.HeadingRunning);
            fixture.RequireWaitingDetached();
        });
        Test("标题票据：已Loaded且挂树的当前目标直接起跑并释放全部等待", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            fixture.Target.IsLoaded = true;
            fixture.Mount();
            Assert.True(fixture.Attach());
            Assert.Equal(1, fixture.StartCount);
            fixture.RequireWaitingDetached();
        });
        Test("标题票据：替换票后旧Loaded和已投递超时不能取消或启动新票", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            fixture.Attach();
            var oldLoaded = fixture.Target.CapturePostedLoaded();
            var oldTimeout = fixture.Timer.CapturePostedTick();
            var replacement = fixture.Prepare(titleX: 80);
            fixture.Attach();
            oldLoaded();
            oldTimeout();
            Assert.Equal(0, fixture.StartCount);
            fixture.Mount();
            fixture.Target.Load();
            Assert.Equal(replacement.Title.Left - fixture.Target.TitleText.X, ((Microsoft.UI.Xaml.Media.CompositeTransform)fixture.Target.TitleText.RenderTransform!).TranslateX);
            Assert.Equal(1, fixture.StartCount);
            fixture.RequireWaitingDetached();
        });
        Test("标题票据：离页统一Stop取消pending，缓存重入的迟到Loaded不能起旧票", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            fixture.Attach();
            var oldLoaded = fixture.Target.CapturePostedLoaded();
            fixture.Target.StopPreviewHeadingMotion();
            fixture.RequireDetached();
            fixture.Mount();
            fixture.Target.Load();
            oldLoaded();
            Assert.Equal(0, fixture.StartCount);
        });
        Test("标题票据：显式取消、导航停止、失败及真实离树都解绑并拒绝晚到回调", () =>
        {
            foreach (var cause in new[] { "cancel", "stopped", "failed", "frame-left", "page-left", "unrelated", "same-item-navigation" })
            {
                using var fixture = new Fixture();
                fixture.Prepare();
                fixture.Attach();
                var oldLoaded = fixture.Target.CapturePostedLoaded();
                switch (cause)
                {
                    case "cancel": PreviewHeadingTransition.Cancel(fixture.Frame); break;
                    case "stopped": fixture.Frame.StopNavigation(); break;
                    case "failed": fixture.Frame.FailNavigation(); break;
                    case "frame-left": fixture.Frame.XamlRoot = null; fixture.Frame.Unload(); break;
                    case "page-left": fixture.Target.Unload(); break;
                    case "unrelated": fixture.Frame.Navigate(new object()); break;
                    case "same-item-navigation": fixture.Frame.Navigate(new DetailRequest { Item = fixture.Item }); break;
                }
                fixture.RequireDetached();
                fixture.Target.IsLoaded = true;
                fixture.Target.XamlRoot = new() { Content = new FrameworkElement() };
                oldLoaded();
                fixture.Target.Layout();
                Assert.Equal(0, fixture.StartCount, cause);
            }
        });
        Test("标题票据：目标尚未接入时只有匹配导航能保留原票", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            fixture.Frame.Navigate(new DetailRequest { Item = fixture.Item });
            Assert.True(fixture.Attach());
            fixture.Mount();
            fixture.Target.Load();
            Assert.Equal(1, fixture.StartCount);
        });
        Test("标题票据：来源过期、目标换项、账号owner或原DTO身份变化均不复用", () =>
        {
            foreach (var cause in new[] { "stale", "target-item", "target-owner", "source-mutation" })
            {
                using var fixture = new Fixture();
                fixture.Prepare();
                fixture.Attach();
                switch (cause)
                {
                    case "stale": fixture.Item.Source!.IsCurrent = false; break;
                    case "target-item": fixture.Target.ViewModel.CurrentItem = new() { IdentityKey = "other", Source = fixture.Item.Source }; break;
                    case "target-owner": fixture.Target.ViewModel.CurrentItem = new() { IdentityKey = fixture.Item.IdentityKey }; break;
                    case "source-mutation": fixture.Item.IdentityKey = "mutated-after-capture"; break;
                }
                fixture.Mount();
                fixture.Target.Load();
                Assert.Equal(0, fixture.StartCount, cause);
                fixture.RequireDetached();
            }
        });
        Test("标题票据：相同身份的新DTO仍能接，缺失身份的Attach拒绝并解绑", () =>
        {
            using (var fixture = new Fixture())
            {
                fixture.Prepare();
                fixture.Target.ViewModel.CurrentItem = new() { IdentityKey = fixture.Item.IdentityKey, Source = fixture.Item.Source };
                fixture.Attach();
                fixture.Mount();
                fixture.Target.Load();
                Assert.Equal(1, fixture.StartCount);
            }
            using (var fixture = new Fixture())
            {
                fixture.Prepare();
                Assert.False(PreviewHeadingTransition.Attach(fixture.Target, new() { IdentityKey = fixture.Item.IdentityKey }));
                fixture.RequireDetached();
            }
        });
        Test("标题票据：当前Frame内容已换页或目标移到另一Frame不会启动", () =>
        {
            foreach (var movedFrame in new[] { false, true })
            {
                using var fixture = new Fixture();
                fixture.Prepare();
                fixture.Attach();
                if (movedFrame) fixture.Target.Frame = new() { Content = fixture.Target };
                else fixture.Frame.Content = new Page();
                fixture.Mount();
                fixture.Target.Load();
                Assert.Equal(0, fixture.StartCount);
                fixture.Timer.Fire();
                fixture.RequireDetached();
            }
        });
        Test("标题票据：700毫秒原等待截止取消，不延时也不残留Loaded监听", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            fixture.Attach();
            Assert.Equal(TimeSpan.FromMilliseconds(700), fixture.Timer.Interval);
            fixture.Timer.Fire();
            fixture.RequireDetached();
            fixture.Mount();
            fixture.Target.Load();
            Assert.Equal(0, fixture.StartCount);
        });
        Test("标题票据：等待时应用和系统关动画立即取消，后台系统通知回本Frame队列", () =>
        {
            foreach (var cause in new[] { "app", "system", "system-posted" })
            {
                using var fixture = new Fixture();
                fixture.Prepare();
                fixture.Attach();
                if (cause == "app") HomeMotion.Change(false);
                else
                {
                    fixture.Frame.DispatcherQueue.HasThreadAccess = cause != "system-posted";
                    UISettings.Change(false);
                    fixture.Frame.DispatcherQueue.Drain();
                }
                fixture.RequireDetached();
                fixture.Mount();
                fixture.Target.Load();
                Assert.Equal(0, fixture.StartCount, cause);
            }
        });
        Test("标题票据：旧系统投递回调不能借恢复后的动画偏好清掉新票", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            fixture.Attach();
            fixture.Frame.DispatcherQueue.HasThreadAccess = false;
            UISettings.Change(false);
            UISettings.Enabled = true;
            var replacement = fixture.Prepare(titleX: 90);
            fixture.Attach();
            fixture.Frame.DispatcherQueue.Drain();
            fixture.Mount();
            fixture.Target.Load();
            Assert.Equal(replacement.Title.Left - fixture.Target.TitleText.X, ((Microsoft.UI.Xaml.Media.CompositeTransform)fixture.Target.TitleText.RenderTransform!).TranslateX);
            Assert.Equal(1, fixture.StartCount);
        });
        Test("标题票据：普通点击没有capture或动画关闭不建等待，上一张票先收尾", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            fixture.Attach();
            PreviewHeadingTransition.Prepare(fixture.Frame, fixture.Source);
            fixture.RequireDetached();
            var count = DispatcherQueueTimer.Created.Count;
            HomeMotion.Preference = false;
            fixture.Prepare();
            Assert.Equal(count, DispatcherQueueTimer.Created.Count);
            Assert.False(fixture.Attach());
        });
        Test("标题票据：来源详情Release不取消刚为另一目标准备的票", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            var sourceDetail = new DetailPage { Frame = fixture.Frame };
            sourceDetail.StopPreviewHeadingMotion();
            fixture.Attach();
            fixture.Mount();
            fixture.Target.Load();
            Assert.Equal(1, fixture.StartCount);
        });
        Test("真实标题飞行：同步接同版徽标，两元素共用320毫秒Storyboard，完成恢复不透明终值", () =>
        {
            using var fixture = new Fixture();
            var capture = fixture.Prepare();
            fixture.Target.TitlePlate.Opacity = 0.4;
            fixture.Attach();
            fixture.Mount();
            fixture.Target.Load();
            Assert.True(ReferenceEquals(capture.PlatePicture, fixture.Target.TitlePlate.Source));
            Assert.Equal(1, fixture.Target.StoppedPlateArrivals);
            var board = fixture.Board;
            Assert.Equal(10, board.Children.Count);
            Assert.True(board.Children.All(animation => animation.Duration.TimeSpan == TimeSpan.FromMilliseconds(320)));
            Assert.Equal(0d, fixture.Target.TitleText.Opacity);
            Assert.Equal(0d, fixture.Target.TitlePlate.Opacity);
            Assert.True(fixture.Target.TitleText.RenderTransform is CompositeTransform);
            Assert.True(fixture.Target.TitlePlate.RenderTransform is CompositeTransform);
            board.Complete();
            Assert.False(fixture.Target.HeadingRunning);
            Assert.Null(fixture.Target.TitleText.RenderTransform);
            Assert.Null(fixture.Target.TitlePlate.RenderTransform);
            Assert.Equal(1d, fixture.Target.TitleText.Opacity);
            Assert.Equal(1d, fixture.Target.TitlePlate.Opacity);
            fixture.RequireDetached();
        });
        Test("真实标题飞行：已有高清图不被低清源覆盖，旧版徽标不参与同版标题时钟", () =>
        {
            foreach (var changedArtwork in new[] { false, true })
            {
                using var fixture = new Fixture();
                fixture.Prepare();
                var high = new BitmapImage();
                fixture.Target.ViewModel.PlateImage = high;
                fixture.Target.TitlePlate.Source = high;
                if (changedArtwork) fixture.Item.PlateArtwork = new("item", "Logo", "new-version");
                fixture.Attach();
                fixture.Mount();
                fixture.Target.Load();
                Assert.True(ReferenceEquals(high, fixture.Target.TitlePlate.Source));
                Assert.Equal(changedArtwork ? 5 : 10, fixture.Board.Children.Count);
                Assert.Equal(changedArtwork ? 0 : 1, fixture.Target.StoppedPlateArrivals);
                fixture.Board.Complete();
                fixture.RequireDetached();
            }
        });
        Test("真实标题飞行：关动画、离页和完成超时均恢复同一标题徽标并解除所有监听", () =>
        {
            foreach (var cause in new[] { "app", "system", "release", "timeout" })
            {
                using var fixture = new Fixture();
                fixture.Prepare();
                fixture.Attach();
                fixture.Mount();
                fixture.Target.Load();
                var board = fixture.Board;
                switch (cause)
                {
                    case "app": HomeMotion.Change(false); break;
                    case "system": UISettings.Change(false); break;
                    case "release": fixture.Target.StopPreviewHeadingMotion(); break;
                    case "timeout": fixture.Timer.Fire(); break;
                }
                Assert.False(fixture.Target.HeadingRunning, cause);
                Assert.Equal(1, board.StopCalls);
                Assert.Null(fixture.Target.TitleText.RenderTransform);
                Assert.Null(fixture.Target.TitlePlate.RenderTransform);
                Assert.Equal(1d, fixture.Target.TitleText.Opacity);
                Assert.Equal(1d, fixture.Target.TitlePlate.Opacity);
                fixture.RequireDetached();
            }
        });
        Test("真实标题飞行：抢排期间离页或换身份不启动，待票清理不能恢复成旧代次", () =>
        {
            foreach (var release in new[] { false, true })
            {
                using var fixture = new Fixture();
                fixture.Prepare();
                fixture.Attach();
                fixture.Target.DuringLayout = release ? fixture.Target.StopPreviewHeadingMotion
                    : () => fixture.Target.ViewModel.CurrentItem = new() { IdentityKey = "replacement", Source = fixture.Item.Source };
                fixture.Mount();
                fixture.Target.Load();
                Assert.Equal(0, fixture.StartCount);
                Assert.False(fixture.Target.HeadingRunning);
                fixture.RequireDetached();
            }
        });
    }

    private sealed class Fixture : IDisposable
    {
        private readonly int _timerStart = DispatcherQueueTimer.Created.Count;
        private readonly int _boardStart = Storyboard.Created.Count;
        internal Frame Frame { get; } = new();
        internal EmbyItem Item { get; } = new();
        internal PosterCard Source { get; } = new();
        internal DetailPage Target { get; } = new() { IsLoaded = false };
        internal DispatcherQueueTimer Timer => DispatcherQueueTimer.Created.Last();
        internal Storyboard Board => Storyboard.Created.Last();
        internal int StartCount => Storyboard.Created.Skip(_boardStart).Sum(board => board.BeginCalls);

        internal Fixture()
        {
            HomeMotion.Preference = true;
            UISettings.Enabled = true;
            UISettings.AddFailure = UISettings.EventFailure.None;
            UISettings.RemoveFailure = UISettings.EventFailure.None;
            Frame.XamlRoot = new() { Content = new FrameworkElement() };
            Frame.Content = Source;
            Source.Card!.Item = Item;
            Target.Frame = Frame;
            Target.ViewModel.CurrentItem = Item;
            Target.TitleText.Parent = Target;
            Target.TitleText.X = 300;
            Target.TitleText.Y = 160;
            Target.TitleText.ActualWidth = 240;
            Target.TitleText.ActualHeight = 44;
            Target.TitlePlate.Parent = Target;
            Target.TitlePlate.X = 300;
            Target.TitlePlate.Y = 80;
            Target.TitlePlate.ActualWidth = 260;
            Target.TitlePlate.ActualHeight = 55;
        }

        internal PreviewHeadingCapture Prepare(double titleX = 20)
        {
            var capture = new PreviewHeadingCapture(Item, new Rect(titleX, 30, 240, 40), 24,
                new Rect(30, 10, 200, 50), new ArtworkRef("item", "Logo", "logo"), new BitmapImage());
            Source.PreviewHeading = capture;
            PreviewHeadingTransition.Prepare(Frame, Source);
            return capture;
        }

        internal bool Attach()
        {
            Frame.Content = Target;
            return PreviewHeadingTransition.Attach(Target, Item);
        }

        internal void Mount()
        {
            Target.XamlRoot = Frame.XamlRoot;
            Target.TitleText.XamlRoot = Frame.XamlRoot;
            Target.TitlePlate.XamlRoot = Frame.XamlRoot;
        }

        internal void RequireWaitingDetached()
        {
            Assert.False(Frame.HasNavigationHandlers || Frame.HasUnloadedHandlers);
            Assert.False(Target.HasLoadedHandlers || Target.HasLayoutHandlers);
            Assert.False(Target.ViewModel.HasPropertyHandlers);
            Assert.False(DispatcherQueueTimer.Created[_timerStart].IsRunning || DispatcherQueueTimer.Created[_timerStart].HasHandlers);
        }

        internal void RequireDetached()
        {
            Assert.False(Frame.HasNavigationHandlers || Frame.HasUnloadedHandlers);
            Assert.False(Target.HasLoadedHandlers || Target.HasLayoutHandlers || Target.HasUnloadedHandlers);
            Assert.False(Target.ViewModel.HasPropertyHandlers);
            Assert.Null(HomeMotion.Handlers);
            Assert.True(UISettings.Created.All(settings => settings.Handlers is null));
            foreach (var timer in DispatcherQueueTimer.Created.Skip(_timerStart))
                Assert.False(timer.IsRunning || timer.HasHandlers);
            Assert.True(Storyboard.Created.Skip(_boardStart).All(board => !board.HasHandlers));
        }

        public void Dispose()
        {
            PreviewHeadingTransition.Cancel(Frame);
            Target.StopPreviewHeadingMotion();
            Frame.DispatcherQueue.Drain();
            RequireDetached();
            HomeMotion.Preference = true;
            UISettings.Enabled = true;
        }
    }
}
