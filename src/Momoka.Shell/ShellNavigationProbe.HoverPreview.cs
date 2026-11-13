using System.Collections.Concurrent;
using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Momoka.Emby;
using Momoka.Shell.Views;
using Momoka.Theming;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    // 匿名数据、真实控件与 Popup.Closed；不移动指针，不启动媒体播放。
    private async Task HoverPreviewAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync("hover-preview.invalid");
        var portrait = await FixtureImageAsync(400, 600);
        var landscape = await FixtureImageAsync(800, 400);
        var originalTheme = ThemeHost.Current;
        var originalMotion = HomeMotion.PreferenceEnabled;
        var poster = new PosterCard { CardWidth = 170, PosterHeight = 255 };
        var stage = new Grid { Padding = new Thickness(24), MinHeight = 650 };
        poster.HorizontalAlignment = HorizontalAlignment.Center;
        poster.VerticalAlignment = VerticalAlignment.Top;
        poster.Margin = new Thickness(0, 90, 0, 0);
        stage.Children.Add(poster);
        var scroll = new ScrollViewer { Content = stage };
        _root.Children.Add(scroll);
        var readings = new List<string>();
        var actions = 0;
        poster.ActionRequested += (_, args) =>
        {
            Require(args.Action == CardAction.Play, "预览按钮产生了非播放动作");
            actions++;
        };
        try
        {
            foreach (var width in new[] { 1280, 760, 360 })
            {
                ResizeProbeClient(width, 700);
                await LayoutAsync(scroll);
                Require(Math.Abs(_root.ActualWidth - width) < 2, "窗口实际宽度未到达请求值");
                foreach (var scenario in new[] { "resume", "empty", "virtual" })
                {
                    var pending = new ConcurrentQueue<TaskCompletionSource<HttpResponseMessage>>();
                    fixture.Transport.Reply = (request, token) =>
                    {
                        Require(request.Method == HttpMethod.Get, "悬停不能写入服务器");
                        if (!request.RequestUri!.AbsolutePath.EndsWith("/Images/Thumb", StringComparison.Ordinal))
                            return Task.FromResult(FakeTransport.Standard(request));
                        var response = Pending<HttpResponseMessage>();
                        pending.Enqueue(response);
                        return response.Task.WaitAsync(token);
                    };
                    var item = new EmbyItem
                    {
                        Id = $"hover-{width}-{scenario}",
                        Name = scenario == "resume" ? "青之箱：冬季赛场上的约定与新的开始"
                            : scenario == "empty" ? "字段为空的影片" : "尚未播出的虚拟单集",
                        Type = scenario == "empty" ? EmbyItemType.Movie : EmbyItemType.Episode,
                        ImageTags = new() { [EmbyImageStore.Thumb] = "fixture-thumb" }
                    };
                    if (scenario == "resume")
                    {
                        item.SeriesName = item.Name;
                        item.ParentIndexNumber = 2;
                        item.IndexNumber = 5;
                        item.RunTimeTicks = TimeSpan.FromMinutes(24).Ticks;
                        item.CommunityRating = 8.6f;
                        item.Genres = ["Animation", "Comedy", "Romance"];
                        item.Overview = "终于迎来了冬季杯的首场比赛。在队友的鼓励下，带着共同的约定踏上赛场，迎接属于自己的挑战。";
                        item.UserData = new() { PlaybackPositionTicks = TimeSpan.FromMinutes(6).Ticks };
                    }
                    if (scenario == "virtual") item.LocationType = "Virtual";
                    var card = new CardItem(fixture.Session.Capture().Bind(item), fixture.Images, 170,
                        upcoming: scenario == "virtual", fetchPoster: _ => Task.FromResult(portrait));
                    poster.Card = card;
                    await card.EnsurePosterAsync();
                    await LayoutAsync(poster);
                    Require(poster.ShowPreviewForShot() == card.Title, "正式卡片未打开悬停预览");
                    var popup = Get<Popup>(poster, "_previewHost");
                    var preview = Get<CardPreview>(poster, "_preview");
                    await LayoutAsync(preview);
                    await UntilAsync(() => pending.Count >= 1);
                    var old = pending.ToArray()[0];
                    popup.IsOpen = false;
                    await UntilAsync(() => !Get<bool>(poster, "_previewOpen")
                        && Get<CancellationTokenSource?>(poster, "_previewArtwork") is null);
                    Require(Get<List<ScrollViewer>>(poster, "_previewScrolls").Count == 0, "关闭后仍保留滚动监听");
                    Require(poster.ShowPreviewForShot() == card.Title, "关闭后无法重新打开");
                    await LayoutAsync(preview);
                    await UntilAsync(() => pending.Count >= 2);
                    var image = Get<Image>(preview, "PreviewImage");
                    var media = Get<Border>(preview, "Media");
                    var fallback = image.Source;
                    Require(fallback is not null, "宽图未到时没有海报");
                    var before = new Size(preview.ActualWidth, preview.ActualHeight);
                    var mediaBefore = new Size(media.ActualWidth, media.ActualHeight);
                    old.TrySetResult(Picture(landscape));
                    await Task.Delay(100);
                    Require(ReferenceEquals(image.Source, fallback), "旧请求覆盖重新打开的预览");
                    pending.ToArray()[1].TrySetResult(Picture(landscape));
                    await UntilAsync(() => image.Source is not null && !ReferenceEquals(image.Source, fallback));
                    await LayoutAsync(preview);
                    Require(Math.Abs(preview.ActualHeight - before.Height) < 1
                        && Math.Abs(media.ActualHeight - mediaBefore.Height) < 1, "宽图到达后浮层跳高");
                    Require(Math.Abs(image.Width - media.ActualWidth) < 1
                        && Math.Abs(image.Height - media.ActualHeight) < 1, "图片未匹配媒体视口");
                    var play = Get<Button>(preview, "PlayButton");
                    Require(play.Visibility == card.PlayVisibility, "播放入口未沿用卡片能力");
                    var bounds = Bounds(preview, _root);
                    Require(bounds.Left >= 10 && bounds.Top >= 10 && bounds.Right <= _root.ActualWidth - 10
                        && bounds.Bottom <= _root.ActualHeight - 10, $"预览越界：{bounds} / {_root.ActualWidth}x{_root.ActualHeight}");
                    foreach (var name in new[] { "TitleText", "MetaText", "GenresText", "OverviewText", "ResumeText" })
                    {
                        var text = Get<TextBlock>(preview, name);
                        if (text.Visibility != Visibility.Visible || text.ActualWidth <= 0) continue;
                        var box = Bounds(text, preview);
                        Require(box.Left >= 0 && box.Right <= preview.ActualWidth + 1
                            && box.Bottom <= preview.ActualHeight + 1, "文本越界：" + name);
                    }
                    if (scenario == "empty")
                        foreach (var name in new[] { "FactsBlock", "OverviewText", "ResumeBlock" })
                            Require(Get<FrameworkElement>(preview, name).Visibility == Visibility.Collapsed, "空字段未收起：" + name);
                    if (scenario == "resume")
                    {
                        Require(Get<TextBlock>(preview, "PlayText").Text == "继续播放", "续播文案错误");
                        Require(!Get<TextBlock>(preview, "MetaText").Text.Contains(item.SeriesName!, StringComparison.Ordinal), "重复剧名");
                        var progress = Get<ProgressBar>(preview, "ResumeProgress");
                        var peer = new ProgressBarAutomationPeer(progress);
                        Require(peer.GetAutomationId() == "CardPreviewProgress", "进度条无法定位");
                        var value = (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue);
                        Require(value.Minimum == 0 && value.Maximum == 100 && Math.Abs(value.Value - 25) < 0.1, "进度条UIA读数错误");
                        var buttonPeer = new ButtonAutomationPeer(play);
                        Require(buttonPeer.GetAutomationId() == "CardPreviewPlay" && buttonPeer.GetName() == card.PlayActionTip,
                            "播放UIA名称或定位错误");
                        Require(play.ActualHeight >= 40, "播放按钮命中区域不足");
                        if (width == 1280)
                        {
                            foreach (var theme in UiThemes.All)
                            {
                                ThemeHost.Apply(theme);
                                await LayoutAsync(preview);
                                await SaveProbeScreenAsync($"hover-preview-{theme.Id}-screen.png");
                            }
                            ThemeHost.Apply(originalTheme);
                            HomeMotion.ApplyPreference(true);
                            if (HomeMotion.AnimationsEnabled)
                            {
                                var completed = 0;
                                preview.PlayEnter();
                                preview.PlayExit(() => completed++);
                                preview.PlayEnter();
                                await Task.Delay(300);
                                Require(completed == 0 && Get<Border>(preview, "Panel").Opacity > 0.99, "反转执行旧关闭回调");
                                preview.PlayExit(() => completed++);
                                HomeMotion.ApplyPreference(false);
                                Require(completed == 1 && Get<Border>(preview, "Panel").Opacity == 0, "关动画未立即落定");
                            }
                            else readings.Add("系统动画关闭，未覆盖有帧反转");
                            HomeMotion.ApplyPreference(originalMotion);
                            preview.PlayEnter();
                        }
                        await SaveProbeScreenAsync($"hover-preview-resume-{width}-screen.png");
                        var expected = actions + 1;
                        ((IInvokeProvider)buttonPeer.GetPattern(PatternInterface.Invoke)).Invoke();
                        await UntilAsync(() => actions == expected && !popup.IsOpen);
                    }
                    else await SaveProbeScreenAsync($"hover-preview-{scenario}-{width}-screen.png");
                    readings.Add($"{width}/{scenario}: panel={before}; media={mediaBefore}; Closed后重开通过");
                    Call(poster, "ClosePreview", true);
                    await UntilAsync(() => Get<CancellationTokenSource?>(poster, "_previewArtwork") is null);
                    card.AbandonPoster();
                }
            }
            Require(actions == 3 && fixture.Actions.Plays == 0, "匿名动作次数错误或进入播放服务");
        }
        finally
        {
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "hover-preview.txt"), readings);
            Call(poster, "ClosePreview", true);
            poster.Card?.AbandonPoster();
            _root.Children.Remove(scroll);
            ThemeHost.Apply(originalTheme);
            HomeMotion.ApplyPreference(originalMotion);
            ResizeInspect(1420, 980);
        }
        static HttpResponseMessage Picture(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        static Rect Bounds(FrameworkElement element, UIElement target) => element.TransformToVisual(target)
            .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
    }
}
