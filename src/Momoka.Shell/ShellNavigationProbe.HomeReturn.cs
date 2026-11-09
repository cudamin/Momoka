using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task HomeNavigationReturnAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var libraries = Enumerable.Range(0, 2).Select(index => new EmbyItem
        {
            Id = "fold-library-" + index,
            Name = "匿名媒体库 " + (index + 1),
            Type = EmbyItemType.CollectionFolder,
            CollectionType = "movies",
            ImageTags = new() { ["Primary"] = "poster" }
        }).ToArray();
        foreach (var library in libraries) fixture.Session.Capture().Bind(library);
        fixture.Settings.Ui.ShowHomeBanner = true;
        fixture.Settings.Ui.HomeLibraryPlacement = HomeLibraryPlacement.BelowCarousel;
        fixture.Settings.Ui.HomeRows = HomeLayout.Save(HomeLayout.Plan(null, libraries));
        foreach (var row in fixture.Settings.Ui.HomeRows)
            row.Visible = row.Key == HomeLayout.Libraries || libraries.Any(library => row.Key == HomeLayout.LibraryKey(library));
        var picture = await FixtureImageAsync(1280, 720);
        var items = Enumerable.Range(0, 6).Select(index => new EmbyItem
        {
            Id = "fold-film-" + index,
            Name = "匿名影片 " + (index + 1),
            Type = EmbyItemType.Movie,
            ImageTags = new() { ["Primary"] = "poster" },
            BackdropImageTags = ["backdrop"]
        }).ToArray();
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(picture) });
            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal)) return Task.FromResult(Json(items));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var frame = new Frame { CacheSize = 2 };
        _root.Children.Add(frame);
        var samples = new List<object>();
        try
        {
            foreach (var width in new[] { 1100, 760 })
            {
                ResizeProbeClient(width, 1000);
                fixture.Settings.Ui.HomeLibraryPlacement = HomeLibraryPlacement.BelowCarousel;
                Require(frame.Navigate(typeof(HomePage), new HomeRequest(fixture.Services, libraries, null)), "主页导航失败");
                var home = (HomePage)frame.Content!;
                await UntilAsync(() => home.IsReady && home.HomeRows.Count == 3);
                await LayoutAsync(home);
                await Task.Delay(800);
                var scroller = Get<ScrollView>(home, "Scroller");
                var repeater = Get<ItemsRepeater>(home, "ShelfRepeater");
                var gap = ((StackLayout)repeater.Layout).Spacing;
                var line = ((ValueTuple<double, bool>)Call(home, "MeasureCoversLine", gap)!).Item1;
                ResizeProbeClient(width, (int)Math.Ceiling(_root.ActualHeight + line + 10 - scroller.ActualHeight));
                await LayoutAsync(home);
                fixture.Settings.Ui.HomeLibraryPlacement = HomeLibraryPlacement.Auto;
                Call(home, "UpdateLibraryOverlay");
                Require(!home.ViewModel.LibraryOnBanner && scroller.ActualHeight > line,
                    "默认档夹具没有落在媒体库下方的阈值边缘");

                // 主动施加真实入场动画作正对照，稳定布局与渲染位移必须给出同一条界线。
                HomeMotion.Enter(home, repeater, home.HomeRows.Count);
                var animated = ShelfTargets(repeater).Any(target => target.RenderTransform is TranslateTransform { Y: > 0 });
                Require(!HomeMotion.AnimationsEnabled || animated, "货架入场正对照没有开始");
                var movingLine = ((ValueTuple<double, bool>)Call(home, "MeasureCoversLine", gap)!).Item1;
                Require(Math.Abs(movingLine - line) <= 0.5, $"入场动画改变阈值：{line:0.0} → {movingLine:0.0}");
                Call(home, "UpdateLibraryOverlay");
                Require(!home.ViewModel.LibraryOnBanner, "普通入场误把媒体库压上轮播");
                await Task.Delay(800);

                var retainedPage = home;
                var retainedRows = home.HomeRows.ToArray();
                var retainedCards = home.ViewModel.Shelves.SelectMany(shelf => shelf.Cards).ToArray();
                var retainedSlides = home.ViewModel.Slides.ToArray();
                Require(frame.Navigate(typeof(LibraryPage), LibraryRequest.For(fixture.Services, libraries[0])), "离开主页失败");
                await UntilAsync(() => frame.Content is LibraryPage library && library.IsReady);
                frame.GoBack();
                home = (HomePage)frame.Content!;
                Require(ReferenceEquals(retainedPage, home), "有限 Frame 缓存没有复用成功主页实例");
                Require(retainedRows.SequenceEqual(home.HomeRows), "同配置主页返回重建了混排货架");
                Require(retainedCards.SequenceEqual(home.ViewModel.Shelves.SelectMany(shelf => shelf.Cards)),
                    "同配置主页返回重建了卡片");
                Require(retainedSlides.SequenceEqual(home.ViewModel.Slides), "同配置主页返回重建了轮播对象");
                Require(home.IsReady, "返回主页未使用暖缓存");
                await UntilAsync(() => home.IsLoaded);
                Require(Get<bool>(home, "_shelfEntranceSuppressed"), "历史返回主页仍允许货架重新入场");
                Require(!Get<bool>(home, "_entranceSuppressed"), "普通返回借用了播放返回与翻档抑制位");
                repeater = Get<ItemsRepeater>(home, "ShelfRepeater");
                for (var sample = 0; sample < 55; sample++)
                {
                    home.UpdateLayout();
                    AssertShelvesSettled(repeater);
                    Call(home, "UpdateLibraryOverlay");
                    Require(!home.ViewModel.LibraryOnBanner, "暖返回先把媒体库摆上轮播再落回横排");
                    Require(Get<HomeFoldMotion?>(home, "_fold") is null && Get<HomeFoldMotion?>(home, "_foldLate") is null,
                        "暖返回播放了多余媒体库翻档动画");
                    Require(Get<FrameworkElement>(home, "LibraryOverlay").Visibility == Visibility.Collapsed,
                        "暖返回出现轮播上的媒体库宿主");
                    await Task.Delay(16);
                }
                await SaveProbeScreenAsync($"home-navigation-return-{width}-screen.png");
                samples.Add(new { width, line, animated, frames = 55, libraryOnBanner = false, shelvesSettled = true });
                home.Release();
                frame.Content = null;
                frame.BackStack.Clear();
                frame.ForwardStack.Clear();
            }
            Require(fixture.Actions.Plays == 0 && fixture.Transport.Requests.All(value => value.StartsWith("GET ", StringComparison.Ordinal)),
                "主页返回匿名探针产生播放或写入");
            await WritePerformanceEvidenceAsync("home-navigation-return.json", new { samples, realNetwork = false, mediaLoaded = false });
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            ResizeProbeClient(1420, 980);
        }
    }
}
