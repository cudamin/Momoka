using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Shell.Views;
using Windows.UI.ViewManagement;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Test("仅可见当前视频海报准备，音乐缺图与过期身份走普通导航", () =>
        {
            foreach (var state in new[] { "music", "missing", "stale", "hidden", "outside" })
            {
                using var fixture = new Fixture();
                if (state == "music") fixture.Item.Type = "Audio";
                if (state == "missing") fixture.Source.ConnectedPoster.Source = null;
                if (state == "stale") fixture.Item.Source!.IsCurrent = false;
                if (state == "hidden") fixture.Source.Visibility = Visibility.Collapsed;
                if (state == "outside") fixture.Source.ConnectedPoster.X = 2000;
                Assert.False(CardDetailTransition.Prepare(fixture.Frame, fixture.Source));
            }
        });
        Test("实际身份与目标就绪后启动一次，并取消图片淡入", () =>
        {
            using var fixture = new Fixture();
            Assert.True(fixture.Prepare());
            var animation = ConnectedAnimationService.Created.Last();
            fixture.Target.ConnectedPoster.Source = null;
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            Assert.Equal(0, animation.StartCalls);
            fixture.Target.ConnectedPoster.Source = new BitmapSource();
            fixture.Target.Layout();
            fixture.Target.Layout();
            Assert.Equal(1, animation.StartCalls);
            Assert.Equal(1, fixture.Target.StoppedArrivals);
            Assert.True(BrowsePresentation.Results.Any(result => result.Started));
            animation.Complete();
            Assert.Null(animation.Handlers);
            Assert.Null(HomeMotion.Handlers);
        });
        Test("原生TryStart拒绝或抛错恢复普通呈现", () =>
        {
            foreach (var throws in new[] { false, true })
            {
                using var fixture = new Fixture();
                fixture.Prepare();
                var animation = ConnectedAnimationService.Created.Last();
                animation.Starts = false;
                animation.ThrowOnStart = throws;
                CardDetailTransition.Attach(fixture.Target, fixture.Item);
                Assert.True(BrowsePresentation.Results.Any(result => !result.Started));
                Assert.False(CardDetailTransition.IsPrepared(fixture.Frame, new DetailRequest { Item = fixture.Item }));
                Assert.Null(animation.Handlers);
            }
        });
        Test("错账号同ID和目标身份更正不能复用旧准备", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            var other = new EmbyItem { IdentityKey = fixture.Item.IdentityKey };
            Assert.False(CardDetailTransition.IsPrepared(fixture.Frame, new DetailRequest { Item = other }));
            CardDetailTransition.Attach(fixture.Target, other);
            Assert.Equal(0, ConnectedAnimationService.Created.Last().StartCalls);
        });
        Test("来源过期和准备超时解除目标等待", () =>
        {
            foreach (var stale in new[] { false, true })
            {
                using var fixture = new Fixture();
                fixture.Prepare();
                fixture.Target.ConnectedPoster.Source = null;
                CardDetailTransition.Attach(fixture.Target, fixture.Item);
                if (stale)
                {
                    fixture.Item.Source!.IsCurrent = false;
                    fixture.Target.Layout();
                }
                else DispatcherTimer.Created.Last().Fire();
                fixture.Target.ConnectedPoster.Source = new BitmapSource();
                fixture.Target.Layout();
                Assert.Equal(0, ConnectedAnimationService.Created.Last().StartCalls);
                Assert.False(DispatcherTimer.Created.Last().IsRunning);
            }
        });
        Test("关应用或系统动画立即取消，后台系统事件回UI队列", () =>
        {
            foreach (var system in new[] { false, true })
            {
                using var fixture = new Fixture();
                fixture.Prepare();
                if (system)
                {
                    fixture.Frame.DispatcherQueue.HasThreadAccess = false;
                    UISettings.Change(false);
                    fixture.Frame.DispatcherQueue.Drain();
                }
                else HomeMotion.Change(false);
                Assert.False(CardDetailTransition.IsPrepared(fixture.Frame, new DetailRequest { Item = fixture.Item }));
                Assert.Null(HomeMotion.Handlers);
            }
        });
        Test("导航取消与Frame离树拒绝晚到Loaded和旧完成", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            var stale = ConnectedAnimationService.Created.Last().LateComplete();
            fixture.Frame.Unload();
            fixture.Target.Load();
            stale();
            Assert.Null(HomeMotion.Handlers);
            Assert.False(CardDetailTransition.IsPrepared(fixture.Frame, new DetailRequest { Item = fixture.Item }));
        });
        Test("返回只用原已存在可见源，成功后完成再释放", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            ConnectedAnimationService.Created.Last().Complete();
            Assert.True(CardDetailTransition.BeginReturn(fixture.Target));
            fixture.Frame.Content = fixture.Source;
            CardDetailTransition.CompleteReturn(fixture.Frame);
            fixture.Frame.DispatcherQueue.Drain();
            var returning = ConnectedAnimationService.Created.Last();
            Assert.Equal(1, returning.StartCalls);
            Assert.True(CardDetailTransition.Returning(fixture.Frame));
            returning.Complete();
            Assert.False(CardDetailTransition.Returning(fixture.Frame));
        });
        Test("返回源回收或队列拒绝正常回退，不强制创建源", () =>
        {
            foreach (var rejected in new[] { false, true })
            {
                using var fixture = new Fixture();
                fixture.Prepare();
                CardDetailTransition.Attach(fixture.Target, fixture.Item);
                ConnectedAnimationService.Created.Last().Complete();
                Assert.True(CardDetailTransition.BeginReturn(fixture.Target));
                fixture.Frame.Content = fixture.Source;
                fixture.Frame.DispatcherQueue.RejectEnqueue = rejected;
                fixture.Source.ConnectedPoster.Source = null;
                CardDetailTransition.CompleteReturn(fixture.Frame);
                fixture.Frame.DispatcherQueue.Drain();
                Assert.False(CardDetailTransition.Returning(fixture.Frame));
                Assert.Equal(0, ConnectedAnimationService.Created.Last().StartCalls);
            }
        });
        Test("原生Prepare失败和Cancel失败不阻止普通导航", () =>
        {
            using var fixture = new Fixture();
            ConnectedAnimationService.ThrowOnPrepare = true;
            Assert.False(fixture.Prepare());
            ConnectedAnimationService.ThrowOnPrepare = false;
            fixture.Prepare();
            ConnectedAnimationService.Created.Last().ThrowOnCancel = true;
            CardDetailTransition.Cancel(fixture.Frame);
            Assert.False(CardDetailTransition.IsPrepared(fixture.Frame, new DetailRequest { Item = fixture.Item }));
        });
        Test("进入不相关页面取消旧票据；已使用准备不能借相同Item重复", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            fixture.Frame.Navigate(new DetailRequest { Item = fixture.Item });
            Assert.True(CardDetailTransition.IsPrepared(fixture.Frame, new DetailRequest { Item = fixture.Item }));
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            Assert.False(CardDetailTransition.IsPrepared(fixture.Frame, new DetailRequest { Item = fixture.Item }));
            fixture.Frame.Navigate(new object());
            Assert.Null(HomeMotion.Handlers);
            Assert.False(CardDetailTransition.Started(fixture.Frame));
        });
        Test("快速第二次准备拒绝旧Completed且仅新目标生效", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            var old = ConnectedAnimationService.Created.Last();
            var stale = old.LateComplete();
            fixture.Prepare();
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            var current = ConnectedAnimationService.Created.Last();
            stale();
            Assert.Equal(1, current.StartCalls);
            Assert.NotNull(current.Handlers);
            current.Complete();
            Assert.Null(current.Handlers);
        });
        Test("窄窗隐藏目标等待超时仍静态回退，返回错误页面不启动", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            fixture.Target.Visibility = Visibility.Collapsed;
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            DispatcherTimer.Created.Last().Fire();
            Assert.False(CardDetailTransition.Started(fixture.Frame));
            fixture.Target.Visibility = Visibility.Visible;
            fixture.Prepare();
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            ConnectedAnimationService.Created.Last().Complete();
            Assert.True(CardDetailTransition.BeginReturn(fixture.Target));
            fixture.Frame.Content = new object();
            CardDetailTransition.CompleteReturn(fixture.Frame);
            fixture.Frame.DispatcherQueue.Drain();
            Assert.Equal(0, ConnectedAnimationService.Created.Last().StartCalls);
        });
        foreach (var add in new[] { UISettings.EventFailure.Before, UISettings.EventFailure.After })
            foreach (var remove in new[] { UISettings.EventFailure.Before, UISettings.EventFailure.After })
                Test($"系统动画订阅{add}与退订{remove}失败仍清票据并回普通呈现", () =>
                {
                    using var fixture = new Fixture();
                    UISettings.AddFailure = add;
                    UISettings.RemoveFailure = remove;
                    Assert.False(fixture.Prepare());
                    Assert.False(CardDetailTransition.IsPrepared(fixture.Frame, new DetailRequest { Item = fixture.Item }));
                    Assert.False(DispatcherTimer.Created.Last().IsRunning);
                    Assert.Null(HomeMotion.Handlers);
                    Assert.True(BrowsePresentation.Results.Any(result => !result.Started));
                    var failedSettings = UISettings.Created.Last();
                    var stale = failedSettings.CapturePostedChange();
                    if (add == UISettings.EventFailure.After && remove == UISettings.EventFailure.Before)
                        Assert.NotNull(failedSettings.Handlers); // 退订失败不伪称native已释放。
                    UISettings.AddFailure = UISettings.EventFailure.None;
                    UISettings.RemoveFailure = UISettings.EventFailure.None;
                    fixture.Prepare();
                    UISettings.Enabled = false; // 不向新订阅发通知，仅投递已经失效的旧委托。
                    stale();
                    Assert.True(CardDetailTransition.IsPrepared(fixture.Frame, new DetailRequest { Item = fixture.Item }));
                    UISettings.Enabled = true;
                    failedSettings.Handlers = null; // 只释放测试故障端点，不改变生产取消路径。
                });

        foreach (var remove in new[] { UISettings.EventFailure.Before, UISettings.EventFailure.After })
            Test("完成退订" + remove + "失败旧系统事件不能取消新返回", () =>
            {
                using var fixture = new Fixture();
                fixture.Prepare();
                CardDetailTransition.Attach(fixture.Target, fixture.Item);
                var failedSettings = UISettings.Created.Last();
                var stale = failedSettings.CapturePostedChange();
                UISettings.RemoveFailure = remove;
                ConnectedAnimationService.Created.Last().Complete();
                Assert.Null(HomeMotion.Handlers);
                UISettings.RemoveFailure = UISettings.EventFailure.None;
                Assert.True(CardDetailTransition.BeginReturn(fixture.Target));
                UISettings.Enabled = false; // 旧观察期不能读取这次偏好后取消新返回票据。
                stale();
                Assert.True(CardDetailTransition.Returning(fixture.Frame));
                UISettings.Enabled = true;
                CardDetailTransition.Cancel(fixture.Frame);
                Assert.False(CardDetailTransition.Returning(fixture.Frame));
                failedSettings.Handlers = null;
            });

        Test("返回重新订阅失败不会向导航抛错，清票据与回退继续", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            ConnectedAnimationService.Created.Last().Complete();
            UISettings.AddFailure = UISettings.EventFailure.After;
            UISettings.RemoveFailure = UISettings.EventFailure.Before;
            Assert.False(CardDetailTransition.BeginReturn(fixture.Target));
            Assert.False(CardDetailTransition.Returning(fixture.Frame));
            Assert.True(BrowsePresentation.Results.Any(result => !result.Started));
            UISettings.Created.Last().Handlers = null;
        });

        Test("导航Stopped或Failed立即取消匹配准备而不等待700ms", () =>
        {
            foreach (var failure in new[] { false, true })
            {
                using var fixture = new Fixture();
                fixture.Prepare();
                fixture.Frame.Navigate(new DetailRequest { Item = fixture.Item });
                var timer = DispatcherTimer.Created.Last();
                if (failure) fixture.Frame.FailNavigation();
                else fixture.Frame.StopNavigation();
                Assert.False(timer.IsRunning);
                Assert.False(CardDetailTransition.IsPrepared(fixture.Frame, new DetailRequest { Item = fixture.Item }));
                Assert.True(BrowsePresentation.Results.Any(result => !result.Started));
                fixture.Target.Layout();
                Assert.Equal(0, ConnectedAnimationService.Created.Last().StartCalls);
            }
        });
        Test("退出再登录同ID不能借原owner返回，源卡被复用也不借新项", () =>
        {
            foreach (var rebind in new[] { false, true })
            {
                using var fixture = new Fixture();
                fixture.Prepare();
                CardDetailTransition.Attach(fixture.Target, fixture.Item);
                ConnectedAnimationService.Created.Last().Complete();
                if (rebind) fixture.Source.Card!.Item = new EmbyItem { IdentityKey = "source:replacement", Source = fixture.Item.Source };
                else
                {
                    fixture.Item.Source!.IsCurrent = false;
                    fixture.Target.ViewModel.CurrentItem = new EmbyItem { IdentityKey = fixture.Item.IdentityKey };
                }
                Assert.False(CardDetailTransition.BeginReturn(fixture.Target));
            }
        });
        Test("货架内已实现但被滚动视口裁掉的海报不准备关联动画", () =>
        {
            using var fixture = new Fixture();
            var viewport = new ScrollView { ActualWidth = 300, ActualHeight = 400, X = 40, Y = 40 };
            fixture.Source.Parent = viewport;
            fixture.Source.ConnectedPoster.X = 400; // root内但横带视口外。
            fixture.Source.ConnectedPoster.Y = 80;
            Assert.False(fixture.Prepare());
            fixture.Source.ConnectedPoster.X = 80;
            Assert.True(fixture.Prepare());
            CardDetailTransition.Cancel(fixture.Frame);
            var outer = new ScrollViewer { ActualWidth = 600, ActualHeight = 200, X = 20, Y = 20 };
            viewport.Parent = outer;
            fixture.Source.ConnectedPoster.Y = 250; // 内层可见但被外页纵向视口裁掉。
            Assert.False(fixture.Prepare());
        });
        Test("原生前进缺完成事件仍释放活动观察，返回缺完成彻底取消", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            var timer = DispatcherTimer.Created.Last();
            Assert.True(timer.IsRunning);
            timer.Fire();
            Assert.Null(HomeMotion.Handlers);
            Assert.Null(ConnectedAnimationService.Created.Last().Handlers);
            Assert.True(CardDetailTransition.BeginReturn(fixture.Target));
            fixture.Frame.Content = fixture.Source;
            CardDetailTransition.CompleteReturn(fixture.Frame);
            fixture.Frame.DispatcherQueue.Drain();
            timer.Fire();
            Assert.False(CardDetailTransition.Returning(fixture.Frame));
            Assert.Null(HomeMotion.Handlers);
            Assert.Null(ConnectedAnimationService.Created.Last().Handlers);
        });
        Test("返回尚未投递时超时释放票据，旧Timer通知不能取消新准备", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            ConnectedAnimationService.Created.Last().Complete();
            Assert.True(CardDetailTransition.BeginReturn(fixture.Target));
            var timer = DispatcherTimer.Created.Last();
            var stale = timer.CapturePostedTick();
            timer.Fire();
            Assert.False(CardDetailTransition.Returning(fixture.Frame));
            fixture.Prepare();
            stale();
            Assert.True(CardDetailTransition.IsPrepared(fixture.Frame, new DetailRequest { Item = fixture.Item }));
        });
        Test("重复返回完成请求只排一次并启动一次原生动画", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            ConnectedAnimationService.Created.Last().Complete();
            Assert.True(CardDetailTransition.BeginReturn(fixture.Target));
            fixture.Frame.Content = fixture.Source;
            CardDetailTransition.CompleteReturn(fixture.Frame);
            CardDetailTransition.CompleteReturn(fixture.Frame);
            fixture.Frame.DispatcherQueue.Drain();
            var animation = ConnectedAnimationService.Created.Last();
            Assert.Equal(1, animation.StartCalls);
            animation.Complete();
        });
        return Run();
    }

    private sealed class Fixture : IDisposable
    {
        public Frame Frame { get; } = new();
        public EmbyItem Item { get; } = new();
        public PosterCard Source { get; } = new();
        public DetailPage Target { get; } = new();
        public Fixture()
        {
            HomeMotion.Preference = true;
            UISettings.Enabled = true;
            UISettings.AddFailure = UISettings.EventFailure.None;
            UISettings.RemoveFailure = UISettings.EventFailure.None;
            ConnectedAnimationService.ThrowOnPrepare = false;
            BrowsePresentation.Results.Clear();
            var root = new FrameworkElement { ActualWidth = 1280, ActualHeight = 900 };
            var xaml = new XamlRoot { Content = root };
            Frame.XamlRoot = xaml;
            Frame.Content = Source;
            Source.XamlRoot = xaml;
            Source.Card!.Item = Item;
            Source.ConnectedPoster.Parent = Source;
            Source.ConnectedPoster.XamlRoot = xaml;
            Source.ConnectedPoster.Source = new BitmapSource();
            Target.Frame = Frame;
            Target.ViewModel.CurrentItem = Item;
            Target.XamlRoot = xaml;
            Target.ConnectedPoster.Parent = Target;
            Target.ConnectedPoster.XamlRoot = xaml;
            Target.ConnectedPoster.Source = new BitmapSource();
        }
        public bool Prepare() => CardDetailTransition.Prepare(Frame, Source);
        public void Dispose()
        {
            CardDetailTransition.Cancel(Frame);
            Frame.DispatcherQueue.Drain();
            Assert.Null(HomeMotion.Handlers);
            Assert.True(UISettings.Created.All(settings => settings.Handlers is null));
            UISettings.AddFailure = UISettings.EventFailure.None;
            UISettings.RemoveFailure = UISettings.EventFailure.None;
        }
    }
}
