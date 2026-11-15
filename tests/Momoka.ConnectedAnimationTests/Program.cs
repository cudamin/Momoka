using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Diagnostics;
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
            // 目标还没排过版（宽高为 0）：抢跑这一趟什么都不做，等第一次布局。
            fixture.Target.ConnectedPoster.Source = null;
            fixture.Target.ConnectedPoster.ActualWidth = 0;
            fixture.Target.ConnectedPoster.ActualHeight = 0;
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            Assert.Equal(0, animation.StartCalls);
            Assert.True(ReferenceEquals(fixture.Source.ConnectedPoster.Source, fixture.Target.ConnectedPoster.Source));
            Assert.Equal(1, fixture.Target.SeededWidths);
            Assert.Equal(1, fixture.Target.ForcedLayouts);
            fixture.Target.ConnectedPoster.ActualWidth = 200;
            fixture.Target.ConnectedPoster.ActualHeight = 300;
            fixture.Target.ConnectedPoster.IsLoaded = true;
            fixture.Target.Layout();
            fixture.Target.Layout();
            Assert.Equal(1, animation.StartCalls);
            Assert.Equal(2, fixture.Target.StoppedArrivals);
            Assert.True(BrowsePresentation.Results.Any(result => result.Started));
            animation.Complete();
            Assert.Null(animation.Handlers);
            Assert.Null(HomeMotion.Handlers);
        });
        Test("目标已排过版时在新页Loaded前立即起跑", () =>
        {
            using var fixture = new Fixture();
            Assert.True(fixture.Prepare());
            var animation = ConnectedAnimationService.Created.Last();
            fixture.Target.IsLoaded = false;
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            Assert.False(fixture.Target.IsLoaded);
            Assert.Equal(1, animation.StartCalls);
            Assert.True(CardDetailTransition.Started(fixture.Frame));
            Assert.True(BrowsePresentation.Results.Any(result => result.Started));
        });
        Test("新页Loaded前第一次布局落定即可起跑", () =>
        {
            using var fixture = new Fixture();
            Assert.True(fixture.Prepare());
            var animation = ConnectedAnimationService.Created.Last();
            fixture.Target.IsLoaded = false;
            fixture.Target.ConnectedPoster.ActualWidth = 0;
            fixture.Target.ConnectedPoster.ActualHeight = 0;
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            Assert.Equal(0, animation.StartCalls);
            fixture.Target.ConnectedPoster.ActualWidth = 200;
            fixture.Target.ConnectedPoster.ActualHeight = 300;
            fixture.Target.Layout();
            Assert.False(fixture.Target.IsLoaded);
            Assert.Equal(1, animation.StartCalls);
            Assert.True(BrowsePresentation.Results.Any(result => result.Started));
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
                fixture.Target.ConnectedPoster.IsLoaded = false;
                CardDetailTransition.Attach(fixture.Target, fixture.Item);
                if (stale)
                {
                    fixture.Item.Source!.IsCurrent = false;
                    fixture.Target.Layout();
                }
                else DispatcherTimer.Created.Last().Fire();
                fixture.Target.ConnectedPoster.Source = new BitmapSource();
                fixture.Target.ConnectedPoster.IsLoaded = true;
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
        Test("实际图引用收窄到自身Primary，集剧照与宽缩略图保持普通导航", () =>
        {
            foreach (var state in new[] { "episode", "thumb", "backdrop", "other-owner", "other-tag" })
            {
                using var fixture = new Fixture();
                if (state == "episode") fixture.Item.Type = EmbyItemType.Episode;
                else fixture.Source.Card!.LoadedPosterArtwork = state switch
                {
                    "thumb" => new("item", "Thumb", "poster"),
                    "backdrop" => new("item", "Backdrop", "poster"),
                    "other-owner" => new("series", "Primary", "poster"),
                    _ => new("item", "Primary", "other")
                };
                Assert.False(fixture.Prepare());
            }
        });
        Test("主体更正Primary图版本后旧海报不启动且返回重取不借旧图", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            fixture.Item.PrimaryTag = "new-poster";
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            Assert.Equal(0, ConnectedAnimationService.Created.Last().StartCalls);
            fixture.Item.PrimaryTag = "poster";
            fixture.Prepare();
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            ConnectedAnimationService.Created.Last().Complete();
            fixture.Source.Card!.LoadedPosterArtwork = new("item", "Thumb", "alternate");
            Assert.False(CardDetailTransition.BeginReturn(fixture.Target));
        });
        foreach (var add in new[] { ConnectedAnimation.EventFailure.Before, ConnectedAnimation.EventFailure.After })
            foreach (var remove in new[] { ConnectedAnimation.EventFailure.Before, ConnectedAnimation.EventFailure.After })
                Test($"原生Completed订阅{add}退订{remove}异常不阻断回退", () =>
                {
                    using var fixture = new Fixture();
                    fixture.Prepare();
                    var animation = ConnectedAnimationService.Created.Last();
                    animation.AddFailure = add;
                    animation.RemoveFailure = remove;
                    CardDetailTransition.Attach(fixture.Target, fixture.Item);
                    Assert.False(CardDetailTransition.Started(fixture.Frame));
                    Assert.True(BrowsePresentation.Results.Any(result => !result.Started));
                    Assert.True(animation.CancelCalls > 0);
                    var stale = animation.LateComplete();
                    fixture.Prepare();
                    CardDetailTransition.Attach(fixture.Target, fixture.Item);
                    stale();
                    Assert.True(CardDetailTransition.Started(fixture.Frame));
                    animation.Handlers = null; // 释放故障测试端点，生产只证明本地失效。
                });
        Test("原生回执分别报告前进返回启动完成与超时，观察异常不影响清理", () =>
        {
            using var fixture = new Fixture();
            var receipts = new List<CardDetailTransitionReceipt>();
            fixture.Prepare();
            var detach = CardDetailTransition.Observe(fixture.Frame, receipts.Add);
            var detachFault = CardDetailTransition.Observe(fixture.Frame, _ => throw new InvalidOperationException("faulting observer"));
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            ConnectedAnimationService.Created.Last().Complete();
            Assert.True(receipts.Any(receipt => receipt.Direction == "forward" && receipt.Started && receipt.Completed));
            Assert.True(CardDetailTransition.BeginReturn(fixture.Target));
            fixture.Frame.Content = fixture.Source;
            CardDetailTransition.CompleteReturn(fixture.Frame);
            fixture.Frame.DispatcherQueue.Drain();
            Assert.True(receipts.Any(receipt => receipt.Direction == "return" && receipt.Started && !receipt.Completed));
            ConnectedAnimationService.Created.Last().Complete();
            Assert.True(receipts.Any(receipt => receipt.Direction == "return" && receipt.Started && receipt.Completed));
            detach?.Invoke();
            detachFault?.Invoke();
        });
        Test("源离页正常释放海报仍可准备返回，重入只接受原实际Primary", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            ConnectedAnimationService.Created.Last().Complete();
            fixture.Source.Card!.LoadedPosterArtwork = null;
            fixture.Source.ConnectedPoster.Source = null;
            Assert.True(CardDetailTransition.BeginReturn(fixture.Target));
            fixture.Frame.Content = fixture.Source;
            fixture.Source.Card.LoadedPosterArtwork = new("item", "Primary", "poster");
            fixture.Source.ConnectedPoster.Source = new BitmapSource();
            CardDetailTransition.CompleteReturn(fixture.Frame);
            fixture.Frame.DispatcherQueue.Drain();
            Assert.Equal(1, ConnectedAnimationService.Created.Last().StartCalls);
            ConnectedAnimationService.Created.Last().Complete();
        });
        Test("源卡离树清图后仍把准备时的位图交接给详情，完成后释放保留", () =>
        {
            using var fixture = new Fixture();
            var picture = fixture.Source.ConnectedPoster.Source;
            fixture.Prepare();
            Assert.Equal(1, fixture.Source.Retentions);
            fixture.Source.ConnectedPoster.Source = null;
            fixture.Target.ConnectedPoster.Source = null;
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            Assert.True(ReferenceEquals(picture, fixture.Target.ConnectedPoster.Source));
            Assert.True(CardDetailTransition.Started(fixture.Frame));
            ConnectedAnimationService.Created.Last().Complete();
            Assert.Equal(0, fixture.Source.Retentions);
        });
        Test("详情页深入导航保留新准备，普通Release仍取消本页动画", () =>
        {
            using var fixture = new Fixture();
            fixture.Frame.Content = fixture.Target;
            fixture.Prepare();
            CardDetailTransition.Release(fixture.Target);
            Assert.True(CardDetailTransition.IsPrepared(fixture.Frame, new DetailRequest { Item = fixture.Item }));
            CardDetailTransition.Cancel(fixture.Frame);
            fixture.Frame.Content = fixture.Source;
            fixture.Prepare();
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            CardDetailTransition.Release(fixture.Target);
            Assert.False(CardDetailTransition.Started(fixture.Frame));
            Assert.Equal(0, fixture.Source.Retentions);
        });
        Test("卡片详情动作只准备真实导航，拒绝与异常及时释放源", () =>
        {
            using var fixture = new Fixture();
            var page = new Page { Frame = fixture.Frame };
            fixture.Frame.Content = page;
            CardDetailTransition.Navigate(page, fixture.Source, () => { });
            Assert.Equal(0, fixture.Source.Retentions);
            try { CardDetailTransition.Navigate(page, fixture.Source, () => throw new InvalidOperationException("navigation rejected")); }
            catch (InvalidOperationException) { }
            Assert.Equal(0, fixture.Source.Retentions);
            CardDetailTransition.Navigate(page, fixture.Source, () => fixture.Frame.Content = fixture.Target);
            Assert.True(CardDetailTransition.IsPrepared(fixture.Frame, new DetailRequest { Item = fixture.Item }));
        });
        Test("完整主体更正图片或来源容器换项时不交接旧图", () =>
        {
            foreach (var sourceChanged in new[] { false, true })
            {
                using var fixture = new Fixture();
                fixture.Prepare();
                var targetPicture = fixture.Target.ConnectedPoster.Source;
                if (sourceChanged) fixture.Source.Card!.Item = new EmbyItem { IdentityKey = "replacement", Source = fixture.Item.Source };
                else fixture.Target.ViewModel.CurrentItem = new EmbyItem
                {
                    IdentityKey = fixture.Item.IdentityKey,
                    Source = fixture.Item.Source,
                    PrimaryTag = "new-poster"
                };
                CardDetailTransition.Attach(fixture.Target, fixture.Item);
                Assert.False(CardDetailTransition.Started(fixture.Frame));
                if (!sourceChanged) Assert.True(ReferenceEquals(targetPicture, fixture.Target.ConnectedPoster.Source));
                Assert.Equal(0, fixture.Source.Retentions);
            }
        });
        Test("目标高清封面已经完成时不覆盖成来源小图", () =>
        {
            using var fixture = new Fixture();
            var highResolution = new BitmapSource();
            fixture.Target.ViewModel.StillImage = highResolution;
            fixture.Target.ConnectedPoster.Source = highResolution;
            fixture.Prepare();
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            Assert.True(ReferenceEquals(highResolution, fixture.Target.ConnectedPoster.Source));
            Assert.True(CardDetailTransition.Started(fixture.Frame));
        });
        Test("新页尚未Loaded时图片更正不打断Frame创建，Loaded后普通回退", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            fixture.Target.IsLoaded = false;
            fixture.Target.ViewModel.CurrentItem = new EmbyItem
            {
                IdentityKey = fixture.Item.IdentityKey,
                Source = fixture.Item.Source,
                PrimaryTag = "corrected"
            };
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            Assert.True(CardDetailTransition.IsPrepared(fixture.Frame, new DetailRequest { Item = fixture.Item }));
            fixture.Target.Load();
            Assert.False(CardDetailTransition.IsPrepared(fixture.Frame, new DetailRequest { Item = fixture.Item }));
            Assert.Equal(0, ConnectedAnimationService.Created.Last().StartCalls);
            Assert.Equal(0, fixture.Source.Retentions);
        });
        Test("人物关联动画只准备可见、当前账号且Primary图的人物卡", () =>
        {
            foreach (var state in new[] { "video", "missing", "stale", "hidden", "outside", "other-artwork" })
            {
                using var fixture = new PersonFixture();
                switch (state)
                {
                    case "video": fixture.Item.Type = "Movie"; break;
                    case "missing": fixture.Source.ConnectedPoster.Source = null; break;
                    case "stale": fixture.Item.Source!.IsCurrent = false; break;
                    case "hidden": fixture.Source.Visibility = Visibility.Collapsed; break;
                    case "outside": fixture.Source.ConnectedPoster.X = 2000; break;
                    case "other-artwork": fixture.Source.Card!.LoadedPosterArtwork = new("person", "Backdrop", "poster"); break;
                }
                Assert.False(fixture.Prepare(), state);
            }
        });
        Test("人物原图交接目标照片后只启动一次，完成释放保留", () =>
        {
            using var fixture = new PersonFixture();
            Assert.True(fixture.Prepare());
            var picture = fixture.Source.ConnectedPoster.Source;
            var animation = ConnectedAnimationService.Created.Last();
            var receipts = new List<PersonTransitionReceipt>();
            var detach = PersonTransition.Observe(fixture.Frame, receipts.Add);
            fixture.Frame.Content = fixture.TargetPage;
            var adopted = false;
            PersonTransition.Attach(fixture.TargetPage, fixture.Item, fixture.TargetImage, received =>
            {
                adopted = ReferenceEquals(received, picture);
                return true;
            });
            Assert.True(adopted);
            Assert.True(ReferenceEquals(picture, fixture.TargetImage.Source));
            Assert.Equal(1, animation.StartCalls);
            Assert.True(PersonTransition.Started(fixture.Frame));
            Assert.Equal(1, receipts.Count);
            Assert.True(receipts[0].Started && !receipts[0].Terminal);
            Assert.True(BrowsePresentation.Results.Any(result => result.Started));
            animation.Complete();
            Assert.Equal(2, receipts.Count);
            Assert.True(receipts[1].Started && receipts[1].Completed && receipts[1].Terminal);
            detach?.Invoke();
            Assert.Null(animation.Handlers);
            Assert.Null(HomeMotion.Handlers);
            Assert.Equal(0, fixture.Source.Retentions);
        });
        Test("人物目标未Loaded时等待，隐藏或身份不符时回退", () =>
        {
            using (var fixture = new PersonFixture())
            {
                fixture.Prepare();
                fixture.TargetPage.IsLoaded = false;
                fixture.Frame.Content = fixture.TargetPage;
                PersonTransition.Attach(fixture.TargetPage, fixture.Item, fixture.TargetImage);
                var animation = ConnectedAnimationService.Created.Last();
                Assert.Equal(0, animation.StartCalls);
                fixture.TargetPage.Load();
                Assert.Equal(1, animation.StartCalls);
                animation.Complete();
            }
            using (var fixture = new PersonFixture())
            {
                fixture.Prepare();
                fixture.TargetImage.Visibility = Visibility.Collapsed;
                fixture.Frame.Content = fixture.TargetPage;
                PersonTransition.Attach(fixture.TargetPage, fixture.Item, fixture.TargetImage);
                Assert.False(PersonTransition.Started(fixture.Frame));
                Assert.False(PersonTransition.IsPrepared(fixture.Frame, fixture.Item));
                Assert.Equal(0, fixture.Source.Retentions);
            }
            using (var fixture = new PersonFixture())
            {
                fixture.Prepare();
                var other = new EmbyItem { IdentityKey = "other", Type = "Person", Source = fixture.Item.Source };
                PersonTransition.Attach(fixture.TargetPage, other, fixture.TargetImage);
                Assert.Equal(0, ConnectedAnimationService.Created.Last().StartCalls);
                Assert.False(PersonTransition.IsPrepared(fixture.Frame, fixture.Item));
            }
        });
        Test("人物动画关闭或系统动画关闭立即取消并释放保留", () =>
        {
            foreach (var system in new[] { false, true })
            {
                using var fixture = new PersonFixture();
                fixture.Prepare();
                Assert.Equal(1, fixture.Source.Retentions);
                if (system)
                {
                    fixture.Frame.DispatcherQueue.HasThreadAccess = false;
                    UISettings.Change(false);
                    fixture.Frame.DispatcherQueue.Drain();
                }
                else HomeMotion.Change(false);
                Assert.False(PersonTransition.IsPrepared(fixture.Frame, fixture.Item));
                Assert.Null(HomeMotion.Handlers);
                Assert.Equal(0, fixture.Source.Retentions);
                UISettings.Enabled = true;
                HomeMotion.Preference = true;
            }
        });
        Test("人物卡片动作只准备真实导航，拒绝与异常及时释放来源", () =>
        {
            using var fixture = new PersonFixture();
            var page = new Page { Frame = fixture.Frame };
            fixture.Frame.Content = page;
            PersonTransition.Navigate(page, fixture.Source, () => { });
            Assert.Equal(0, fixture.Source.Retentions);
            try { PersonTransition.Navigate(page, fixture.Source, () => throw new InvalidOperationException("navigation rejected")); }
            catch (InvalidOperationException) { }
            Assert.Equal(0, fixture.Source.Retentions);
            PersonTransition.Navigate(page, fixture.Source, () => fixture.Frame.Content = fixture.TargetPage);
            Assert.True(PersonTransition.IsPrepared(fixture.Frame, fixture.Item));
        });
        Test("人物导航Stopped或Failed立即取消票据", () =>
        {
            foreach (var failure in new[] { false, true })
            {
                using var fixture = new PersonFixture();
                fixture.Prepare();
                if (failure) fixture.Frame.FailNavigation();
                else fixture.Frame.StopNavigation();
                Assert.False(PersonTransition.IsPrepared(fixture.Frame, fixture.Item));
                Assert.Null(HomeMotion.Handlers);
                Assert.Equal(0, fixture.Source.Retentions);
            }
        });
        Test("人物快速第二次准备拒绝旧完成且只让新目标启动", () =>
        {
            using var fixture = new PersonFixture();
            fixture.Prepare();
            var old = ConnectedAnimationService.Created.Last();
            var stale = old.LateComplete();
            fixture.Prepare();
            var current = ConnectedAnimationService.Created.Last();
            stale();
            fixture.Frame.Content = fixture.TargetPage;
            PersonTransition.Attach(fixture.TargetPage, fixture.Item, fixture.TargetImage);
            Assert.Equal(1, current.StartCalls);
            Assert.Equal(0, old.StartCalls);
            current.Complete();
        });
        Test("人物目标已有高清照片时不覆盖成来源小图", () =>
        {
            using var fixture = new PersonFixture();
            var highResolution = new BitmapSource();
            fixture.TargetImage.Source = highResolution;
            fixture.Prepare();
            fixture.Frame.Content = fixture.TargetPage;
            var adopted = false;
            PersonTransition.Attach(fixture.TargetPage, fixture.Item, fixture.TargetImage, _ =>
            {
                adopted = true;
                return true;
            });
            Assert.False(adopted);
            Assert.True(ReferenceEquals(highResolution, fixture.TargetImage.Source));
            Assert.Equal(1, ConnectedAnimationService.Created.Last().StartCalls);
            ConnectedAnimationService.Created.Last().Complete();
        });
        Test("关联回执的准备超时、启动拒绝和导航取消分别只结算一个终态", () =>
        {
            foreach (var state in new[] { "timeout", "rejected", "cancelled", "artwork" })
            {
                using var fixture = new Fixture();
                var receipts = new List<CardDetailTransitionReceipt>();
                fixture.Prepare();
                CardDetailTransition.Observe(fixture.Frame, receipts.Add);
                if (state == "timeout") DispatcherTimer.Created.Last().Fire();
                else if (state == "cancelled") fixture.Frame.StopNavigation();
                else
                {
                    if (state == "rejected") ConnectedAnimationService.Created.Last().Starts = false;
                    else fixture.Item.PrimaryTag = "replacement";
                    CardDetailTransition.Attach(fixture.Target, fixture.Item);
                }
                var expected = state switch
                {
                    "timeout" => ConnectedMotionReason.PrepareTimeout,
                    "rejected" => ConnectedMotionReason.StartRejected,
                    "artwork" => ConnectedMotionReason.ArtworkChanged,
                    _ => ConnectedMotionReason.NavigationCancelled
                };
                Assert.Equal(1, receipts.Count);
                Assert.Equal(expected, receipts[0].Reason);
                Assert.True(receipts[0].Terminal && !receipts[0].Started);
                Assert.Equal(state == "timeout", receipts[0].TimedOut);
                Assert.Equal(state == "rejected", receipts[0].PrepareToStartMs is not null);
                CardDetailTransition.Cancel(fixture.Frame);
                Assert.Equal(1, receipts.Count);
                Assert.Equal(0, fixture.Source.Retentions);
            }
        });
        Test("原生准备后的单调计时不包含Prepare调用，前进和返回独立", () =>
        {
            using var fixture = new Fixture();
            var clock = new MotionClock();
            var receipts = new List<CardDetailTransitionReceipt>();
            ConnectedAnimationService.Preparing = () => clock.Advance(350);
            Assert.True(CardDetailTransition.Prepare(fixture.Frame, fixture.Source, clock));
            ConnectedAnimationService.Preparing = null;
            CardDetailTransition.Observe(fixture.Frame, receipts.Add);
            clock.Advance(125);
            ConnectedAnimationService.Created.Last().Starting = () => clock.Advance(7);
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            clock.Advance(55);
            ConnectedAnimationService.Created.Last().Complete();
            Assert.Equal(132d, receipts[0].PrepareToStartMs!.Value);
            Assert.Equal(187d, receipts[1].ElapsedMs);
            Assert.True(receipts[1].Terminal && receipts[1].Completed);
            clock.Advance(4000);
            Assert.True(CardDetailTransition.BeginReturn(fixture.Target));
            fixture.Frame.Content = fixture.Source;
            clock.Advance(22);
            CardDetailTransition.CompleteReturn(fixture.Frame);
            fixture.Frame.DispatcherQueue.Drain();
            clock.Advance(12);
            ConnectedAnimationService.Created.Last().Complete();
            Assert.Equal(22d, receipts[2].PrepareToStartMs!.Value);
            Assert.Equal(34d, receipts[3].ElapsedMs);
            Assert.Equal(2, receipts.Count(receipt => receipt.Terminal));
        });
        Test("人物计时和完成超时分别报告Started与唯一终态，晚完成无效", () =>
        {
            using var fixture = new PersonFixture();
            var clock = new MotionClock();
            var receipts = new List<PersonTransitionReceipt>();
            Assert.True(PersonTransition.Prepare(fixture.Frame, fixture.Source, clock));
            PersonTransition.Observe(fixture.Frame, receipts.Add);
            clock.Advance(90);
            fixture.Frame.Content = fixture.TargetPage;
            PersonTransition.Attach(fixture.TargetPage, fixture.Item, fixture.TargetImage);
            var animation = ConnectedAnimationService.Created.Last();
            var lateComplete = animation.LateComplete();
            Assert.Equal(90d, receipts[0].PrepareToStartMs!.Value);
            clock.Advance(1200);
            DispatcherTimer.Created.Last().Fire();
            lateComplete();
            Assert.Equal(2, receipts.Count);
            Assert.True(receipts[1].Started && receipts[1].Terminal && receipts[1].TimedOut);
            Assert.Equal(ConnectedMotionReason.CompletionTimeout, receipts[1].Reason);
            Assert.Equal(1290d, receipts[1].ElapsedMs);
            Assert.True(BrowsePresentation.Results.All(result => result.Started));
            Assert.Equal(0, fixture.Source.Retentions);
        });
        Test("人物回执区分准备超时、目标不可见、图版本与动画关闭", () =>
        {
            foreach (var state in new[] { "timeout", "target", "artwork", "disabled" })
            {
                using var fixture = new PersonFixture();
                var receipts = new List<PersonTransitionReceipt>();
                fixture.Prepare();
                PersonTransition.Observe(fixture.Frame, receipts.Add);
                if (state == "timeout") DispatcherTimer.Created.Last().Fire();
                else if (state == "disabled") HomeMotion.Change(false);
                else
                {
                    if (state == "target") fixture.TargetImage.Visibility = Visibility.Collapsed;
                    else fixture.Item.PrimaryTag = "replacement";
                    fixture.Frame.Content = fixture.TargetPage;
                    PersonTransition.Attach(fixture.TargetPage, fixture.Item, fixture.TargetImage);
                }
                Assert.Equal(1, receipts.Count);
                Assert.True(receipts[0].Terminal && !receipts[0].Started);
                Assert.Equal(state switch
                {
                    "timeout" => ConnectedMotionReason.PrepareTimeout,
                    "target" => ConnectedMotionReason.TargetUnavailable,
                    "artwork" => ConnectedMotionReason.ArtworkChanged,
                    _ => ConnectedMotionReason.Disabled
                }, receipts[0].Reason);
                Assert.Null(receipts[0].PrepareToStartMs);
            }
        });
        Test("启动后关闭动画或导航取消只清理落定，不发失败页面回退", () =>
        {
            foreach (var disabled in new[] { false, true })
            {
                using var fixture = new Fixture();
                var receipts = new List<CardDetailTransitionReceipt>();
                fixture.Prepare();
                CardDetailTransition.Observe(fixture.Frame, receipts.Add);
                CardDetailTransition.Attach(fixture.Target, fixture.Item);
                var stale = ConnectedAnimationService.Created.Last().LateComplete();
                if (disabled) HomeMotion.Change(false);
                else fixture.Frame.StopNavigation();
                stale();
                Assert.Equal(2, receipts.Count);
                Assert.True(receipts[1].Started && receipts[1].Terminal && !receipts[1].Completed);
                Assert.Equal(disabled ? ConnectedMotionReason.Disabled : ConnectedMotionReason.NavigationCancelled, receipts[1].Reason);
                Assert.True(BrowsePresentation.Results.All(result => result.Started));
                HomeMotion.Preference = true;
            }
        });
        Test("准备前拒绝只记录有限匿名阶段，不创建票据或保留图片", () =>
        {
            var sink = new MotionSink();
            NavigationTiming.Start(new(sink));
            try
            {
                NavigationTiming.Click("motion-fixture");
                using var fixture = new Fixture();
                var timers = DispatcherTimer.Created.Count;
                HomeMotion.Preference = false;
                Assert.False(fixture.Prepare());
                HomeMotion.Preference = true;
                ConnectedAnimationService.ThrowOnPrepare = true;
                Assert.False(fixture.Prepare());
                Assert.Equal(timers, DispatcherTimer.Created.Count);
                Assert.Null(CardDetailTransition.Observe(fixture.Frame, _ => { }));
                Assert.Equal(0, fixture.Source.Retentions);
                Assert.True(sink.Entries.Any(entry => entry.Stage == "card-forward-disabled"));
                Assert.True(sink.Entries.Any(entry => entry.Stage == "card-forward-prepare-failed"));
                Assert.True(sink.Entries.All(entry => entry.Stage.Length <= 40 && entry.Stage != "invalid"));
                sink.Fail = true;
                Assert.False(fixture.Prepare()); // 诊断写入失败也不能中断普通导航。
            }
            finally { NavigationTiming.Stop(); }
        });
        Test("人物Completed观察者发起新导航时旧清理不能取消新票据", () =>
        {
            using var fixture = new PersonFixture();
            fixture.Prepare();
            PersonTransition.Observe(fixture.Frame, receipt =>
            {
                if (receipt.Completed) Assert.True(fixture.Prepare());
            });
            fixture.Frame.Content = fixture.TargetPage;
            PersonTransition.Attach(fixture.TargetPage, fixture.Item, fixture.TargetImage);
            ConnectedAnimationService.Created.Last().Complete();
            Assert.True(PersonTransition.IsPrepared(fixture.Frame, fixture.Item));
            Assert.Equal(1, fixture.Source.Retentions);
        });
        Test("已创建票据的Prepare订阅失败只记录一次准备失败阶段", () =>
        {
            var sink = new MotionSink();
            NavigationTiming.Start(new(sink));
            try
            {
                NavigationTiming.Click("motion-fixture");
                using var fixture = new Fixture();
                UISettings.AddFailure = UISettings.EventFailure.Before;
                Assert.False(fixture.Prepare());
                Assert.Equal(1, sink.Entries.Count(entry => entry.Stage == "card-forward-prepare-failed"));
                Assert.Equal(0, fixture.Source.Retentions);
            }
            finally { NavigationTiming.Stop(); }
        });
        Test("前进完成或超时观察者准备返回时旧尾声不能清掉返回动画", () =>
        {
            foreach (var timedOut in new[] { false, true })
            {
                using var fixture = new Fixture();
                fixture.Prepare();
                var receipts = new List<CardDetailTransitionReceipt>();
                CardDetailTransition.Observe(fixture.Frame, receipt =>
                {
                    receipts.Add(receipt);
                    if (receipt.Direction == "forward" && receipt.Terminal)
                        Assert.True(CardDetailTransition.BeginReturn(fixture.Target));
                });
                CardDetailTransition.Attach(fixture.Target, fixture.Item);
                var forward = ConnectedAnimationService.Created.Last();
                if (timedOut) DispatcherTimer.Created.Last().Fire();
                else forward.Complete();
                Assert.True(CardDetailTransition.Returning(fixture.Frame));
                Assert.True(DispatcherTimer.Created.Last().IsRunning);
                fixture.Frame.Content = fixture.Source;
                CardDetailTransition.CompleteReturn(fixture.Frame);
                fixture.Frame.DispatcherQueue.Drain();
                var returning = ConnectedAnimationService.Created.Last();
                Assert.Equal(1, returning.StartCalls);
                returning.Complete();
                Assert.True(receipts.Any(receipt => receipt.Direction == "return" && receipt.Completed));
            }
        });
        Test("返回完成超时只发已启动终态，旧回调不重复结算", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            var receipts = new List<CardDetailTransitionReceipt>();
            CardDetailTransition.Observe(fixture.Frame, receipts.Add);
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            DispatcherTimer.Created.Last().Fire();
            Assert.True(BrowsePresentation.Results.All(result => result.Started));
            Assert.True(CardDetailTransition.BeginReturn(fixture.Target));
            fixture.Frame.Content = fixture.Source;
            CardDetailTransition.CompleteReturn(fixture.Frame);
            fixture.Frame.DispatcherQueue.Drain();
            var late = ConnectedAnimationService.Created.Last().LateComplete();
            DispatcherTimer.Created.Last().Fire();
            late();
            Assert.Equal(1, receipts.Count(receipt => receipt.Direction == "return" && receipt.Terminal));
            Assert.True(receipts.Any(receipt => receipt.Direction == "return" && receipt.Started
                && receipt.Terminal && receipt.Reason == ConnectedMotionReason.CompletionTimeout));
            Assert.True(BrowsePresentation.Results.All(result => result.Started));
        });
        Test("人物图版本变化保留待决票据，由Attach发布正确图漂移回退", () =>
        {
            using var fixture = new PersonFixture();
            fixture.Prepare();
            var receipts = new List<PersonTransitionReceipt>();
            PersonTransition.Observe(fixture.Frame, receipts.Add);
            fixture.Item.PrimaryTag = "replacement";
            Assert.True(PersonTransition.IsPrepared(fixture.Frame, fixture.Item));
            fixture.Frame.Content = fixture.TargetPage;
            PersonTransition.Attach(fixture.TargetPage, fixture.Item, fixture.TargetImage);
            Assert.Equal(1, receipts.Count);
            Assert.Equal(ConnectedMotionReason.ArtworkChanged, receipts[0].Reason);
            Assert.True(receipts[0].Terminal && !receipts[0].Started);
        });
        Test("海报Prepare取消回执重入时外层不覆盖或清掉内层新准备", () =>
        {
            using var fixture = new Fixture();
            fixture.Prepare();
            var receipts = new List<CardDetailTransitionReceipt>();
            CardDetailTransition.Observe(fixture.Frame, receipt =>
            {
                receipts.Add(receipt);
                if (receipt.Reason == ConnectedMotionReason.Superseded) Assert.True(fixture.Prepare());
            });
            var created = ConnectedAnimationService.Created.Count;
            Assert.False(fixture.Prepare());
            Assert.Equal(created + 1, ConnectedAnimationService.Created.Count);
            Assert.Equal(1, fixture.Source.Retentions);
            Assert.True(CardDetailTransition.IsPrepared(fixture.Frame, new DetailRequest { Item = fixture.Item }));
            Assert.Equal(1, receipts.Count);
            CardDetailTransition.Attach(fixture.Target, fixture.Item);
            Assert.Equal(1, ConnectedAnimationService.Created.Last().StartCalls);
        });
        Test("人物Prepare取消回执重入时外层不覆盖或清掉内层新准备", () =>
        {
            using var fixture = new PersonFixture();
            fixture.Prepare();
            var receipts = new List<PersonTransitionReceipt>();
            PersonTransition.Observe(fixture.Frame, receipt =>
            {
                receipts.Add(receipt);
                if (receipt.Reason == ConnectedMotionReason.Superseded) Assert.True(fixture.Prepare());
            });
            var created = ConnectedAnimationService.Created.Count;
            Assert.False(fixture.Prepare());
            Assert.Equal(created + 1, ConnectedAnimationService.Created.Count);
            Assert.Equal(1, fixture.Source.Retentions);
            Assert.True(PersonTransition.IsPrepared(fixture.Frame, fixture.Item));
            Assert.Equal(1, receipts.Count);
            fixture.Frame.Content = fixture.TargetPage;
            PersonTransition.Attach(fixture.TargetPage, fixture.Item, fixture.TargetImage);
            Assert.Equal(1, ConnectedAnimationService.Created.Last().StartCalls);
        });
        return Run();
    }

    private sealed class MotionClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _timestamp;
        internal void Advance(int milliseconds) => _timestamp += milliseconds;
    }

    private sealed class MotionSink : INavigationTimingSink
    {
        internal List<NavigationTimingEvent> Entries { get; } = [];
        internal bool Fail { get; set; }
        public void Write(NavigationTimingEvent entry)
        {
            if (Fail) throw new IOException("fixture sink unavailable");
            Entries.Add(entry);
        }
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
            ConnectedAnimationService.Preparing = null;
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
            ConnectedAnimationService.Preparing = null;
        }
    }

    private sealed class PersonFixture : IDisposable
    {
        public Frame Frame { get; } = new();
        public EmbyItem Item { get; } = new() { Type = "Person" };
        public PosterCard Source { get; } = new();
        public Page TargetPage { get; } = new();
        public Image TargetImage { get; } = new();

        public PersonFixture()
        {
            HomeMotion.Preference = true;
            UISettings.Enabled = true;
            UISettings.AddFailure = UISettings.EventFailure.None;
            UISettings.RemoveFailure = UISettings.EventFailure.None;
            ConnectedAnimationService.ThrowOnPrepare = false;
            ConnectedAnimationService.Preparing = null;
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
            TargetPage.Frame = Frame;
            TargetPage.XamlRoot = xaml;
            TargetImage.Parent = TargetPage;
            TargetImage.XamlRoot = xaml;
            TargetImage.ActualWidth = 200;
            TargetImage.ActualHeight = 300;
        }

        public bool Prepare() => PersonTransition.Prepare(Frame, Source);

        public void Dispose()
        {
            PersonTransition.Cancel(Frame);
            Frame.DispatcherQueue.Drain();
            Assert.Null(HomeMotion.Handlers);
            Assert.True(UISettings.Created.All(settings => settings.Handlers is null));
            UISettings.AddFailure = UISettings.EventFailure.None;
            UISettings.RemoveFailure = UISettings.EventFailure.None;
            HomeMotion.Preference = true;
            UISettings.Enabled = true;
        }
    }
}
