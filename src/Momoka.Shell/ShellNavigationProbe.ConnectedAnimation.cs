using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
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
        var frame = new Frame();
        var sourcePage = new Page();
        var source = new PosterCard { Card = new CardItem(item, fixture.Images, 200) };
        var sourcePanel = new StackPanel { Margin = new Thickness(40), HorizontalAlignment = HorizontalAlignment.Left };
        sourcePanel.Children.Add(source);
        sourcePage.Content = sourcePanel;
        frame.Content = sourcePage;
        _root.Children.Add(frame);
        var motion = HomeMotion.PreferenceEnabled;
        var samples = new List<object>();
        DetailPage? target = null;
        try
        {
            ResizeProbeClient(1280, 850);
            HomeMotion.ApplyPreference(true);
            await LayoutAsync(source);
            await source.Card.EnsurePosterAsync();
            await UntilAsync(() => source.ConnectedPoster.Source is BitmapSource { PixelWidth: > 0 } && !source.PosterArrivalRunning);
            Require(CardDetailTransition.Prepare(frame, source), "XAML岛不能准备当前可见视频海报关联动画");
            Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item), new SuppressNavigationTransitionInfo()), "匿名详情导航失败");
            target = (DetailPage)frame.Content;
            await UntilAsync(() => target.IsReady && CardDetailTransition.Started(frame));
            samples.Add(new { State = "forward", Started = true, Identity = target.ViewModel.CurrentItem?.IdentityKey, NoPlayback = fixture.Actions.Plays == 0 });
            await Task.Delay(400);
            await CompositionPlaybackProbe.SaveScreenAsync(_window!.Handle, Path.Combine(_options.Paths.LogDirectory, "card-detail-connected-forward-screen.png"));

            // 原始源已离树：仅尝试返回，不能为了动画强制恢复或创建源容器。
            var returnPrepared = CardDetailTransition.BeginReturn(target);
            target.Release();
            frame.Content = sourcePage;
            await LayoutAsync(source);
            if (returnPrepared) CardDetailTransition.CompleteReturn(frame);
            await Task.Delay(400);
            samples.Add(new { State = "return", Prepared = returnPrepared, FrameRestored = ReferenceEquals(frame.Content, sourcePage) });
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

            Require(CardDetailTransition.Prepare(frame, source), "取消测试不能准备");
            CardDetailTransition.Cancel(frame);
            Require(!CardDetailTransition.IsPrepared(frame, DetailRequest.For(fixture.Services, item)), "离页取消留下旧准备");
            samples.Add(new { State = "cancelled", Prepared = false });
            Require(fixture.Actions.Plays == 0 && fixture.Transport.Requests.All(request => request.StartsWith("GET ", StringComparison.Ordinal)),
                "关联动画研究产生播放或服务器写入");
            await WritePerformanceEvidenceAsync("card-detail-connected.json", new { samples, RealNetwork = false, MediaLoaded = false });
        }
        finally
        {
            CardDetailTransition.Cancel(frame);
            target?.Release();
            source.Card?.AbandonPoster();
            frame.Content = null;
            _root.Children.Remove(frame);
            HomeMotion.ApplyPreference(motion);
        }
    }
}
