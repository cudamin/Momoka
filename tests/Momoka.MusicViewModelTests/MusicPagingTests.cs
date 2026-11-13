using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.Music;
using Momoka.Shell.Views;

namespace Momoka.MusicViewModelTests;

internal static class MusicPagingTests
{
    internal static IEnumerable<(string Name, Func<Task> Run)> Cases()
    {
        yield return ("墙分页原生入口独立于曲目 Footer，绑定通知不冻结", WallEntryAsync);
        yield return ("曲目 Footer 分页入口保留并具备自动化名称", TrackEntryAsync);
        yield return ("墙分页按钮随悬浮播放栏避让并能恢复", ClearanceAsync);
        foreach (var section in new[] { MusicSection.Albums, MusicSection.Artists, MusicSection.Playlists })
            foreach (var total in new[] { 81, 100, 160 })
                yield return ($"{section} {total} 项能经正式页面入口取得第 81 项与末页", () => WallPagesAsync(section, total));
        yield return ("曲目与收藏经原入口取得第二页，集合原地追加", TrackPagesAsync);
        yield return ("读取第二页时重复点击不会并发或取消当前页", DuplicateMoreAsync);
        yield return ("第二页失败保留列表与分页入口，重试复用当前偏移", RetryMoreAsync);
        yield return ("墙模式切换拒绝前一分区迟到第二页", SectionSwitchAsync);
        yield return ("页面释放与重新附加身份拒绝旧第二页", IdentitySwitchAsync);
        yield return ("服务器空第二页结束分页且不清空已有列表", EmptyMoreAsync);
        foreach (var test in MusicPagingProbeTests.Cases()) yield return test;
    }

    private static Task WallEntryAsync()
    {
        var page = ReadPage();
        var wall = Named(page, "WallMoreButton");
        Require(wall.Name.LocalName == "Button" && (string?)wall.Attribute("Click") == "OnMore", "墙没有调用正式 OnMore 的原生 Button");
        Require((string?)wall.Attribute("AutomationProperties.AutomationId") == "MusicWallLoadMore"
            && !string.IsNullOrWhiteSpace((string?)wall.Attribute("AutomationProperties.Name")), "墙分页按钮缺少自动化定位或名称");
        Require((string?)wall.Attribute(Xaml + "Uid") == "MusicLibraryPage_LoadMore", "墙分页按钮没有接入现有 resw 本地化路径");
        var resources = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Strings", "zh-Hans", "Resources.resw"));
        foreach (var key in new[] { "MusicLibraryPage_LoadMore.Content", "MusicLibraryPage_LoadMore.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name" })
            Require(!string.IsNullOrWhiteSpace(resources.Descendants("data").Single(data => (string?)data.Attribute("name") == key).Element("value")?.Value),
                "分页本地化文案缺失：" + key);
        Require((string?)wall.Attribute("Visibility") == "{x:Bind local:MusicLibraryPage.Show(ViewModel.MoreAvailable), Mode=OneWay}",
            "墙分页显隐未跟随 MoreAvailable 的实时绑定");
        var host = Named(page, "WallHost");
        Require(ReferenceEquals(wall.Parent, host) && (string?)wall.Attribute("Grid.Row") == "1", "墙分页入口没有自己的独立行");
        Require((string?)host.Attribute("Visibility") == "{x:Bind ViewModel.WallVisibility, Mode=OneWay}", "曲目模式可能残留墙分页按钮");
        var rows = host.Element(Presentation + "Grid.RowDefinitions")?.Elements().ToArray();
        Require(rows is { Length: 2 } && (string?)rows[0].Attribute("Height") == "*"
            && (string?)rows[1].Attribute("Height") == "Auto", "墙与分页入口未分别分配剩余高度和自身高度");
        Require(ReferenceEquals(Named(page, "WallScroll").Parent, host), "墙滚动区没有被有限高度行承载");
        Require(Named(page, "WallScroll").Name.LocalName == "Grid"
            && Named(page, "MusicWall").Name.LocalName == "ItemsView"
            && !Named(page, "MusicWall").Ancestors().Any(element => element.Name.LocalName == "ScrollViewer"),
            "音乐墙仍由外层竖滚宿主无界量测");
        Require(!wall.Ancestors().Any(element => element.Name.LocalName is "ListView" or "ListView.Footer" or "ScrollViewer"),
            "墙分页按钮仍依赖曲目表或滚动页尾");
        Require(MusicLibraryPage.Show(true) == Visibility.Visible && MusicLibraryPage.Show(false) == Visibility.Collapsed,
            "正式页面显隐函数结果不正确");
        return Task.CompletedTask;
    }

    private static Task TrackEntryAsync()
    {
        var page = ReadPage();
        var button = Named(page, "MoreButton");
        Require(button.Name.LocalName == "Button" && (string?)button.Attribute("Click") == "OnMore", "曲目分页入口不再调用原处理器");
        Require((string?)button.Attribute("AutomationProperties.AutomationId") == "MusicTrackLoadMore"
            && !string.IsNullOrWhiteSpace((string?)button.Attribute("AutomationProperties.Name")), "曲目分页入口缺少定位或名称");
        Require(button.Parent?.Name.LocalName == "ListView.Footer"
            && ReferenceEquals(button.Parent.Parent, Named(page, "MusicRows")), "曲目分页入口移出了现有 Footer");
        Require((string?)button.Attribute("Visibility") == "{x:Bind local:MusicLibraryPage.Show(ViewModel.MoreAvailable), Mode=OneWay}",
            "曲目分页显隐绑定不正确");
        return Task.CompletedTask;
    }

    private static async Task ClearanceAsync()
    {
        await using var fixture = new Fixture();
        using var library = fixture.Library();
        var page = new MusicLibraryPage(library);
        page.SetPlayerClearance(88);
        Require(page.WallMoreButton.Margin.Bottom == 88 && page.MusicWall.Padding.Bottom == 120
            && page.MusicRows.Padding.Bottom == 88, "分页入口或原有滚动区未让开悬浮播放栏");
        page.SetPlayerClearance(0);
        Require(page.WallMoreButton.Margin.Bottom == 0 && page.WallMoreButton.Margin.Top == 16
            && page.MusicWall.Padding.Bottom == 32 && page.MusicRows.Padding.Bottom == 0,
            "播放栏退出后没有恢复原有页尾空隙");
    }

    private static async Task WallPagesAsync(MusicSection section, int total)
    {
        await using var fixture = new Fixture();
        fixture.Transport.Reply = (request, _) => Task.FromResult(Paged(fixture.Transport, request, total));
        using var library = fixture.Library();
        var rows = library.Rows;
        await library.SelectAsync(section);
        Require(rows.Count == 80 && library.MoreAvailable && library.WallVisibility == Visibility.Visible
            && library.TrackViewVisibility == Visibility.Collapsed, "墙首屏未保留正确分区和第二页入口");
        var original = rows[0];
        var notifications = new List<NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, args) => notifications.Add(args.Action);
        var availabilityChanges = new List<string?>();
        library.PropertyChanged += (_, args) => availabilityChanges.Add(args.PropertyName);
        var page = new MusicLibraryPage(library);
        page.InvokeMore();
        await UntilAsync(() => !library.Busy);
        Require(ReferenceEquals(rows, library.Rows) && ReferenceEquals(original, rows[0]), "分页重建了集合或首项");
        Require(rows.Count == total && rows[80].Item.Id == "t80" && rows[^1].Item.Id == "t" + (total - 1), "第 81 项或末项不可达/顺序错误");
        Require(notifications.Count == total - 80 && notifications.All(action => action == NotifyCollectionChangedAction.Add),
            "分页不是追加，选择和滚动锚点会失去稳定对象");
        Require(!library.MoreAvailable && availabilityChanges.Contains(nameof(library.MoreAvailable)), "末页没有实时收起分页入口");
        Require(library.CountLabel == MusicCatalog.CountLabel(section, total), "分页改变了总数或量词");
        Require(fixture.Transport.Requests.Count(request => request.Query.Contains("StartIndex=80", StringComparison.Ordinal)) == 1,
            "实际入口没有且仅有一条第二页请求");
        var before = fixture.Transport.Requests.Count;
        page.InvokeMore();
        Require(fixture.Transport.Requests.Count == before, "末页以后仍发请求");
    }

    private static async Task TrackPagesAsync()
    {
        await using var fixture = new Fixture();
        fixture.Transport.Reply = (request, _) => Task.FromResult(Paged(fixture.Transport, request, 100));
        using var library = fixture.Library();
        var page = new MusicLibraryPage(library);
        foreach (var section in new[] { MusicSection.Tracks, MusicSection.Favorites })
        {
            await library.SelectAsync(section);
            var first = library.Rows[0];
            Require(library.TrackViewVisibility == Visibility.Visible && library.WallVisibility == Visibility.Collapsed, "曲目/收藏不再走表格");
            page.InvokeMore();
            await UntilAsync(() => !library.Busy);
            Require(library.Rows.Count == 100 && ReferenceEquals(first, library.Rows[0]) && !library.MoreAvailable, "曲目第二页退化");
        }
    }

    private static async Task DuplicateMoreAsync()
    {
        await using var fixture = new Fixture();
        var entered = Signal();
        var release = Signal();
        fixture.Transport.Reply = async (request, _) =>
        {
            if (Offset(request.Query) == 80) { entered.TrySetResult(); await release.Task; }
            return Paged(fixture.Transport, request, 160);
        };
        using var library = fixture.Library();
        await library.SelectAsync(MusicSection.Albums);
        var page = new MusicLibraryPage(library);
        page.InvokeMore();
        await entered.Task;
        Require(library.Busy && library.MoreAvailable && library.Rows.Count == 80, "读取期间清空了旧列表或分页入口");
        page.InvokeMore();
        Require(fixture.Transport.Requests.Count(request => Offset(request.Query) == 80) == 1, "重复点击发出了并发余页请求");
        release.TrySetResult();
        await UntilAsync(() => !library.Busy);
        Require(library.Rows.Count == 160 && !library.MoreAvailable, "重复点击取消了当前页或重复追加");
    }

    private static async Task RetryMoreAsync()
    {
        await using var fixture = new Fixture();
        var attempts = 0;
        fixture.Transport.Reply = (request, _) => Task.FromResult(Offset(request.Query) == 80 && ++attempts == 1
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("anonymous page failure") }
            : Paged(fixture.Transport, request, 100));
        using var library = fixture.Library();
        await library.SelectAsync(MusicSection.Playlists);
        var original = library.Rows[0];
        var page = new MusicLibraryPage(library);
        page.InvokeMore();
        await UntilAsync(() => !library.Busy);
        Require(library.Rows.Count == 80 && ReferenceEquals(original, library.Rows[0]) && library.MoreAvailable
            && library.NoticeOpen && library.NoticeTitle == "读取音乐失败", "失败没有保留旧数据、错误原因和恢复入口");
        page.InvokeMore();
        await UntilAsync(() => !library.Busy);
        Require(library.Rows.Count == 100 && !library.MoreAvailable && !library.NoticeOpen && attempts == 2,
            "失败重试没有复用偏移或没有恢复");
    }

    private static async Task SectionSwitchAsync()
    {
        await using var fixture = new Fixture();
        var entered = Signal();
        var release = Signal();
        CancellationToken received = default;
        fixture.Transport.Reply = async (request, token) =>
        {
            if (request.Query.Contains("IncludeItemTypes=MusicAlbum", StringComparison.Ordinal) && Offset(request.Query) == 80)
            { received = token; entered.TrySetResult(); await release.Task; }
            return Paged(fixture.Transport, request, 100);
        };
        using var library = fixture.Library();
        await library.SelectAsync(MusicSection.Albums);
        var oldMore = library.LoadMoreAsync();
        await entered.Task;
        await library.SelectAsync(MusicSection.Artists);
        Require(received.IsCancellationRequested && library.Rows.Count == 80 && library.Section == MusicSection.Artists, "分区切换没有取消旧余页");
        release.TrySetResult();
        await oldMore;
        Require(library.Rows.Count == 80 && library.Rows.All(row => row.Item.Type == EmbyItemType.MusicArtist)
            && library.CountLabel == "100 位" && library.MoreAvailable, "专辑迟到响应污染艺术家分区");
        var page = new MusicLibraryPage(library);
        page.InvokeMore();
        await UntilAsync(() => !library.Busy);
        Require(library.Rows.Count == 100 && library.Rows.All(row => row.Item.Type == EmbyItemType.MusicArtist), "切换后新余页不能正常读取");
    }

    private static async Task IdentitySwitchAsync()
    {
        await using var oldFixture = new Fixture();
        await using var nextFixture = new Fixture();
        var entered = Signal();
        var release = Signal();
        CancellationToken received = default;
        oldFixture.Transport.Reply = async (request, token) =>
        {
            if (Offset(request.Query) == 80) { received = token; entered.TrySetResult(); await release.Task; }
            return Paged(oldFixture.Transport, request, 160);
        };
        nextFixture.Transport.Reply = (request, _) => Task.FromResult(Paged(nextFixture.Transport, request, 81));
        using var library = oldFixture.Library();
        await library.SelectAsync(MusicSection.Albums);
        var oldMore = library.LoadMoreAsync();
        await entered.Task;
        library.Release();
        Require(received.IsCancellationRequested, "页面释放没有取消实际第二页 HTTP");
        library.Attach(new LibraryRequest
        { Services = NullServices.Instance, Title = "new identity", Source = nextFixture.Scope, ParentId = "music", MusicRootId = "music" },
            nextFixture.Session, nextFixture.Images);
        await library.SelectAsync(MusicSection.Playlists);
        release.TrySetResult();
        await oldMore;
        Require(library.Rows.Count == 80 && library.Rows.All(row => ReferenceEquals(row.Item.Source, nextFixture.Scope))
            && library.Title == "new identity" && library.Section == MusicSection.Playlists,
            "释放/换身份后旧第二页写回新列表");
        new MusicLibraryPage(library).InvokeMore();
        await UntilAsync(() => !library.Busy);
        Require(library.Rows.Count == 81 && !library.MoreAvailable && library.Rows.All(row => ReferenceEquals(row.Item.Source, nextFixture.Scope)),
            "新身份第81项不能正常读回");
    }

    private static async Task EmptyMoreAsync()
    {
        await using var fixture = new Fixture();
        fixture.Transport.Reply = (request, _) => Task.FromResult(Offset(request.Query) == 80
            ? Transport.Json(new ItemsResult { Items = [], TotalRecordCount = 160 })
            : Paged(fixture.Transport, request, 160));
        using var library = fixture.Library();
        await library.SelectAsync(MusicSection.Artists);
        var first = library.Rows[0];
        new MusicLibraryPage(library).InvokeMore();
        await UntilAsync(() => !library.Busy);
        Require(library.Rows.Count == 80 && ReferenceEquals(first, library.Rows[0]) && !library.MoreAvailable
            && library.EmptyVisibility == Visibility.Collapsed, "空余页清掉已有数据或保留无限分页入口");
    }

    private static HttpResponseMessage Paged(Transport transport, Sent request, int total)
    {
        var query = request.Query;
        var offset = Offset(query);
        var type = query.Contains("IncludeItemTypes=MusicAlbum", StringComparison.Ordinal) ? EmbyItemType.MusicAlbum
            : request.Path.EndsWith("/Artists", StringComparison.Ordinal) ? EmbyItemType.MusicArtist
            : query.Contains("IncludeItemTypes=Playlist", StringComparison.Ordinal) ? EmbyItemType.Playlist : EmbyItemType.Audio;
        var items = Enumerable.Range(offset, Math.Clamp(total - offset, 0, MusicCatalog.PageSize)).Select(index =>
        {
            var item = transport.Track("t" + index);
            item.Type = type;
            return item;
        }).ToList();
        return Transport.Json(new ItemsResult { Items = items, TotalRecordCount = total });
    }

    private static int Offset(string query)
    {
        var value = query.TrimStart('?').Split('&').FirstOrDefault(part => part.StartsWith("StartIndex=", StringComparison.Ordinal));
        return value is null ? 0 : int.Parse(value["StartIndex=".Length..], CultureInfo.InvariantCulture);
    }

    private static XDocument ReadPage() => XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "MusicLibraryPage.xaml"));
    private static XElement Named(XDocument page, string name) => page.Descendants().Single(element => (string?)element.Attribute(Xaml + "Name") == name);
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("当前测试不在 Momoka 工作树内");
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        { if (condition()) return; await Task.Delay(5); }
        throw new TimeoutException("未收到预期的实际分页事件");
    }
    private sealed class NullServices : IServiceProvider
    {
        internal static NullServices Instance { get; } = new();
        public object? GetService(Type serviceType) => throw new InvalidOperationException("分页夹具不允许解析外部服务");
    }
}
