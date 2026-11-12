using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>真实XAML岛、原生ConnectedAnimation、当前详情页面；仅匿名读请求，无播放。</summary>
    private async Task CardDetailConnectedAnimationAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var item = new EmbyItem { Id = "connected-movie", Name = "匿名关联动画", Type = EmbyItemType.Movie,
            ImageTags = new() { ["Primary"] = "poster" }, BackdropImageTags = ["backdrop"] };
        fixture.Session.Capture().Bind(item);
        var bytes = await FixtureImageAsync(260, 375);
        fixture.Transport.Reply = (request, _) =>
        {
            Require(request.Method == HttpMethod.Get, "关联动画探针只允许匿名读取");
            if (request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            if (request.RequestUri.AbsolutePath.EndsWith("/Items/" + item.Id, StringComparison.Ordinal))
                return Task.FromResult(Json(item));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var frame = new Frame { CacheSize = 1 };
        var sourcePage = new CardDetailConnectedSourcePage();
        var source = new PosterCard { Card = new CardItem(item, fixture.Images, 200) };
        var sourcePanel = new StackPanel { Margin = new Thickness(40), HorizontalAlignment = HorizontalAlignment.Left };
        sourcePanel.Children.Add(source);
        _root.Children.Add(frame);
        var motion = HomeMotion.PreferenceEnabled;
        var samples = new List<object>();
        var receipts = new List<CardDetailTransitionReceipt>();
        var observations = new List<Action>();
        DetailPage? target = null;
        try
        {
            ResizeProbeClient(1280, 850);
            HomeMotion.ApplyPreference(true);
            Require(frame.Navigate(typeof(CardDetailConnectedSourcePage), sourcePanel, new SuppressNavigationTransitionInfo()), "关联源Page正式导航失败");
            sourcePage = (CardDetailConnectedSourcePage)frame.Content;
            await LayoutAsync(source);
            await source.Card.EnsurePosterAsync();
            await UntilAsync(() => source.ConnectedPoster.Source is BitmapSource { PixelWidth: > 0 } && !source.PosterArrivalRunning);
            Require(CardDetailTransition.Prepare(frame, source), "XAML岛不能准备当前可见视频海报关联动画");
            if (CardDetailTransition.Observe(frame, receipts.Add) is { } detach) observations.Add(detach);
            Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item), new SuppressNavigationTransitionInfo()), "匿名详情导航失败");
            target = (DetailPage)frame.Content;
            await UntilAsync(() => target.IsReady && CardDetailTransition.Started(frame));
            samples.Add(new { State = "forward", Started = true, Identity = target.ViewModel.CurrentItem?.IdentityKey, NoPlayback = fixture.Actions.Plays == 0 });
            await UntilAsync(() => receipts.Any(receipt => receipt.Direction == "forward" && receipt.Completed));
            await CompositionPlaybackProbe.SaveScreenAsync(_window!.Handle, Path.Combine(_options.Paths.LogDirectory, "card-detail-connected-forward-screen.png"));

            // 原始源已离树：仅尝试返回，不能为了动画强制恢复或创建源容器。
            var returnPrepared = CardDetailTransition.BeginReturn(target);
            Require(returnPrepared && frame.CanGoBack, "同图关联动画不能准备正式返回");
            frame.GoBack(new SuppressNavigationTransitionInfo());
            await LayoutAsync(source);
            if (returnPrepared) CardDetailTransition.CompleteReturn(frame);
            await UntilAsync(() => receipts.Any(receipt => receipt.Direction == "return" && (receipt.Completed || receipt.TimedOut || !receipt.Started)));
            Require(ReferenceEquals(frame.Content, sourcePage), "Frame缓存返回没有恢复原源Page");
            Require(receipts.Any(receipt => receipt.Direction == "return" && receipt.Started && receipt.Completed), "正式GoBack没有完成原生返回关联动画");
            samples.Add(new { State = "return", Prepared = returnPrepared, FrameRestored = true, FrameGoBack = true,
                TryStart = receipts.Any(receipt => receipt.Direction == "return" && receipt.Started),
                Completed = receipts.Any(receipt => receipt.Direction == "return" && receipt.Completed),
                TimeoutFallback = receipts.Any(receipt => receipt.Direction == "return" && receipt.TimedOut) });
            CardDetailTransition.Cancel(frame);

            HomeMotion.ApplyPreference(false);
            Require(!CardDetailTransition.Prepare(frame, source), "关动画仍准备关联动画");
            samples.Add(new { State = "motion-off", Prepared = false });
            HomeMotion.ApplyPreference(true);
            var image = source.ConnectedPoster.Source;
            source.ConnectedPoster.Source = null;
            Require(!CardDetailTransition.Prepare(frame, source), "缺源图片仍准备关联动画");
            source.ConnectedPoster.Source = image;
            samples.Add(new { State = "missing-source", Prepared = false });

            // 原生前进可选动效失败：仍正式导航到详情，700ms普通回退，随后正常GoBack。
            Require(CardDetailTransition.Prepare(frame, source), "前进失败回退测试不能准备");
            if (CardDetailTransition.Observe(frame, receipts.Add) is { } detachForwardFailure) observations.Add(detachForwardFailure);
            item.ImageTags["Primary"] = "replacement"; // 同项主体图版本已改变，旧Primary票据必须拒绝。
            var beforeFailure = receipts.Count;
            Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item), new SuppressNavigationTransitionInfo()), "旧图票据阻止普通详情导航");
            target = (DetailPage)frame.Content;
            await UntilAsync(() => target.IsReady && receipts.Skip(beforeFailure).Any(receipt => receipt.Direction == "forward" && !receipt.Started));
            Require(!CardDetailTransition.Started(frame), "旧图前进仍报告原生成功");
            frame.GoBack(new SuppressNavigationTransitionInfo());
            await LayoutAsync(source);
            Require(ReferenceEquals(frame.Content, sourcePage), "前进动效回退破坏正常返回");
            item.ImageTags["Primary"] = "poster";
            samples.Add(new { State = "forward-artwork-changed", FrameNavigated = true, TryStart = false, Fallback = true, FrameGoBack = true });

            Require(CardDetailTransition.Prepare(frame, source), "取消测试不能准备");
            CardDetailTransition.Cancel(frame);
            Require(!CardDetailTransition.IsPrepared(frame, DetailRequest.For(fixture.Services, item)), "离页取消留下旧准备");
            samples.Add(new { State = "cancelled", Prepared = false });

            // 第二次真实详情往返让已存在的源图失效：正常GoBack仍完成，关联动画必须给出普通回退回执。
            Require(CardDetailTransition.Prepare(frame, source), "失效源返回测试不能准备");
            if (CardDetailTransition.Observe(frame, receipts.Add) is { } detachFallback) observations.Add(detachFallback);
            Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item), new SuppressNavigationTransitionInfo()), "失效源测试详情导航失败");
            target = (DetailPage)frame.Content;
            await UntilAsync(() => target.IsReady && CardDetailTransition.Started(frame));
            Require(CardDetailTransition.BeginReturn(target), "失效源回退测试不能准备返回");
            source.ConnectedPoster.Source = null;
            var previousReceipts = receipts.Count;
            frame.GoBack(new SuppressNavigationTransitionInfo());
            await LayoutAsync(source);
            source.ConnectedPoster.Source = null; // Loaded缓存恢复之后显式模拟源图片已不可用。
            CardDetailTransition.CompleteReturn(frame);
            await UntilAsync(() => receipts.Skip(previousReceipts).Any(receipt => receipt.Direction == "return" && !receipt.Started));
            Require(ReferenceEquals(frame.Content, sourcePage), "源图片不可用阻止了正式返回");
            samples.Add(new { State = "missing-return-source", FrameGoBack = true, FrameRestored = true, TryStart = false, Fallback = true });
            Require(fixture.Actions.Plays == 0 && fixture.Transport.Requests.All(request => request.StartsWith("GET ", StringComparison.Ordinal)),
                "关联动画研究产生播放或服务器写入");
            await WritePerformanceEvidenceAsync("card-detail-connected.json", new { samples, receipts, RealNetwork = false, MediaLoaded = false });
        }
        finally
        {
            CardDetailTransition.Cancel(frame);
            foreach (var detach in observations) detach();
            target?.Release();
            source.Card?.AbandonPoster();
            frame.Content = null;
            _root.Children.Remove(frame);
            HomeMotion.ApplyPreference(motion);
        }
    }
}

/// <summary>只供匿名关联动画测试，使用真实Frame缓存与OnNavigatedTo，原源元素不被重新创建。</summary>
public sealed class CardDetailConnectedSourcePage : Page
{
    public CardDetailConnectedSourcePage() => NavigationCacheMode = NavigationCacheMode.Required;
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (Content is null && args.Parameter is UIElement content) Content = content;
    }
}
