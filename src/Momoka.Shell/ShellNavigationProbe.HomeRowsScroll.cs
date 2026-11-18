using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 聚合主页有很多排（每个在线账号每个库一排）时，滚到底最后几排「最近添加」必须真的排出来。
    /// <para>
    /// 2026-11-18 用户报「聚合模式下主页的最近添加显示不全」：数据全到了（<see cref="HomePage.HomeRows"/>
    /// 里每一排都有卡片），但竖向货架用 <c>StackLayout</c> 虚拟化时只实现视口里的行、其余行按已实现行的
    /// 平均高度估总高；主页的宽卡排比海报排矮一截，十来排一起排时总高被估短，最后几排滚不到也长不出来。
    /// 这一条把窗口停在三个账号各五个库（十五排库货架 + 媒体库 + 继续观看），滚到 <c>ScrollableHeight</c>
    /// 之后要求最后那一排真的在屏上。
    /// </para>
    /// </summary>
    private async Task AggregateHomeRowsScrollAsync()
    {
        using var fixture = CreateFixture();
        var poster = await FixtureImageAsync(400, 600);
        fixture.Transport.Reply = (request, _) =>
        {
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) });
            if (path.EndsWith("/Views", StringComparison.Ordinal))
                return Task.FromResult(Json(new
                {
                    Items = new (string Id, string Name, string? Type)[]
                        {
                            ("movies", "电影", "movies"),
                            ("series", "电视节目", "tvshows"),
                            ("mixed", "混合内容", null),
                            ("music", "音乐", "music"),
                            ("homevideos", "家庭视频", "homevideos")
                        }
                        .Select(library => new EmbyItem
                        {
                            Id = uri.Host + "-" + library.Id,
                            Name = library.Name,
                            Type = EmbyItemType.CollectionFolder,
                            CollectionType = library.Type,
                            ImageTags = new() { ["Primary"] = "poster" }
                        }).ToArray(),
                    TotalRecordCount = 5
                }));
            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal))
                return Task.FromResult(Json(Enumerable.Range(0, 6).Select(index => new EmbyItem
                {
                    Id = uri.Host + "-latest-" + index,
                    Name = "最近入库 " + index,
                    Type = EmbyItemType.Movie,
                    ImageTags = new() { ["Primary"] = "poster" }
                }).ToArray()));
            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal)) return Task.FromResult(Items(1, 1, "resume-"));
            if (path.EndsWith("/Shows/NextUp", StringComparison.Ordinal)) return Task.FromResult(Items(0, 0));
            if (path.EndsWith("/Items", StringComparison.Ordinal)) return Task.FromResult(Items(6, 6));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        await fixture.SelectAsync("a.invalid", "账号甲");
        await fixture.SelectAsync("b.invalid", "账号乙");
        await fixture.SelectAsync("c.invalid", "账号丙");
        fixture.Settings.AggregateMode = true;
        // 关掉轮播：媒体库那一排留在横排最前面（比后面的海报排矮），正是估高最容易短的一档。
        fixture.Settings.Ui.ShowHomeBanner = false;
        var libraries = await SourceLibraries.LoadAsync(fixture.Session, CancellationToken.None);
        Require(libraries.Items.Count == 15, "夹具没有交出三个账号各五个库");

        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            ResizeProbeClient(1280, 800);
            frame.Navigate(typeof(HomePage), new HomeRequest(fixture.Services, libraries.Items, null));
            var home = (HomePage)frame.Content;
            await UntilAsync(() => home.IsReady && home.HomeRows.Count == 17);
            await LayoutAsync(home);
            await Task.Delay(700); // 首次进场淡入走完，量到的才是最终版面。

            var scroller = Get<ScrollView>(home, "Scroller");
            var repeater = Get<ItemsRepeater>(home, "ShelfRepeater");
            Require(scroller.ScrollableHeight > 0, "主页内容没有超出窗口，夹具不足以验证滚到底");
            Require(Enumerable.Range(0, home.HomeRows.Count).All(index => repeater.TryGetElement(index) is not null),
                "主页竖向货架的排没有全部实现，后面的「最近添加」滚不到也长不出来");

            // 用户那条路：把滚动条拖到底。估短了的话，这里到的「底」其实在最后一排之前。
            scroller.ScrollTo(0, scroller.ScrollableHeight, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
            await LayoutAsync(home);
            await Task.Delay(400);

            var last = home.HomeRows.Count - 1;
            var element = repeater.TryGetElement(last) as FrameworkElement;
            Require(element is not null, "滚到底后最后一排货架没有实现，主页的最近添加显示不全");
            Require(VisibleArea(element!, home) > 0, "最后一排货架滚到底后仍不在屏上");
            await SaveProbeScreenAsync("aggregate-home-rows-bottom-screen.png");
            Require(fixture.Actions.Plays == 0, "货架滚动探针意外请求播放");
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
    }
}
