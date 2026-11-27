using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class SeriesResourceViewTests
{
    internal static void Register(string sourceRoot)
    {
        Test("剧清单VM：首次加载保留接口顺序、全部状态、列表模式且零选择", () =>
        {
            using var fixture = new SeriesViewFixture();
            Assert.Equal("tmdbid=283938", fixture.ViewModel.Query);
            Assert.Equal("3,1,2", Ids(fixture.ViewModel));
            Assert.Equal(0, fixture.ViewModel.SelectedCount);
            Assert.False(fixture.ViewModel.CanContinue);
            Assert.False(fixture.ViewModel.Grouped);
            Assert.Equal(Visibility.Collapsed, fixture.ViewModel.Groups[0].HeaderVisibility);
            Assert.Equal(1, fixture.Transport.HistoryReads);
        });
        Test("剧清单VM：筛选只显示当前匹配，隐藏选择被剔除且清除不重选", () =>
        {
            using var fixture = new SeriesViewFixture();
            var vm = fixture.ViewModel;
            var rows = vm.Rows;
            var retained = vm.Rows.Single(row => row.History.Id == 2);
            vm.SelectAll();
            Assert.Equal(3, vm.SelectedCount);
            vm.SearchText = "s01e02";
            Assert.Equal("2", Ids(vm));
            Assert.Equal(1, vm.SelectedCount);
            Assert.Contains("1 / 3", vm.CountLabel);
            Assert.True(ReferenceEquals(rows, vm.Rows));
            Assert.True(ReferenceEquals(retained, vm.Rows[0]));
            Assert.Equal("2", string.Join(",", vm.Select().Select(history => history.Id)));
            vm.SearchText = "";
            Assert.Equal("3,1,2", Ids(vm));
            Assert.Equal(1, vm.SelectedCount);
            Assert.True(vm.Rows.Single(row => row.History.Id == 2).Selected);
            Assert.True(vm.Rows.Where(row => row.History.Id != 2).All(row => !row.Selected));
            Assert.Equal(1, fixture.Transport.HistoryReads);
        });
        Test("剧清单VM：失败状态的全选不选择隐藏成功记录", () =>
        {
            using var fixture = new SeriesViewFixture();
            var vm = fixture.ViewModel;
            vm.StatusFilter = MoviePilotTransferStatusFilter.Failed;
            vm.SelectAll();
            Assert.Equal("1", Ids(vm));
            Assert.Equal(1, vm.SelectedCount);
            Assert.Equal(1L, vm.Select().Single().Id);
            vm.StatusFilter = MoviePilotTransferStatusFilter.Success;
            Assert.Equal(0, vm.SelectedCount);
            Assert.Equal("3,2", Ids(vm));
            Assert.False(vm.CanContinue);
            vm.StatusFilter = MoviePilotTransferStatusFilter.All;
            Assert.Equal(0, vm.SelectedCount);
        });
        Test("剧清单VM：同一个按钮全选当前结果，再清空已选项", () =>
        {
            using var fixture = new SeriesViewFixture();
            var vm = fixture.ViewModel;
            vm.SetSelected([vm.Rows[0]], true);
            Assert.Equal("全选", vm.SelectionActionLabel);
            vm.ToggleSelection();
            Assert.Equal(3, vm.SelectedCount);
            Assert.Equal("清空选择", vm.SelectionActionLabel);
            vm.ToggleSelection();
            Assert.Equal(0, vm.SelectedCount);
            Assert.Equal("全选", vm.SelectionActionLabel);

            vm.SearchText = "s01e02";
            vm.ToggleSelection();
            Assert.Equal(2L, vm.Select().Single().Id);
            Assert.Equal("清空选择", vm.SelectionActionLabel);
            vm.SearchText = "没有匹配";
            vm.ToggleSelection();
            Assert.Equal(0, vm.SelectedCount);
            Assert.Equal("全选", vm.SelectionActionLabel);
        });
        Test("剧清单VM：排序、方向和分组不发请求且保留当前选择", () =>
        {
            using var fixture = new SeriesViewFixture();
            var vm = fixture.ViewModel;
            var groups = vm.Groups;
            vm.SetSelected([vm.Rows.Single(row => row.History.Id == 2)], true);
            vm.Sort = MoviePilotTransferSort.Title;
            Assert.Equal("1,2,3", Ids(vm));
            vm.Descending = true;
            Assert.Equal("3,2,1", Ids(vm));
            vm.Sort = MoviePilotTransferSort.Size;
            Assert.True(vm.Descending);
            Assert.Equal("3,2,1", Ids(vm));
            Assert.Equal(1, vm.SelectedCount);
            vm.Grouped = true;
            Assert.Equal(1, vm.Groups.Count);
            Assert.Equal(Visibility.Visible, vm.Groups[0].HeaderVisibility);
            Assert.Equal(3, vm.Groups[0].Members.Count);
            Assert.True(ReferenceEquals(groups, vm.Groups));
            vm.Sort = MoviePilotTransferSort.Default;
            Assert.Equal("3,1,2", Ids(vm));
            Assert.False(vm.Descending);
            Assert.Equal(Visibility.Collapsed, vm.DirectionVisibility);
            Assert.Equal(1, fixture.Transport.HistoryReads);
        });
        Test("剧清单VM：精确标题分组、折叠保留勾选且支持折叠全选本组", () =>
        {
            using var fixture = new SeriesViewFixture(alternateTitle: true);
            var vm = fixture.ViewModel;
            vm.Grouped = true;
            Assert.Equal(2, vm.Groups.Count);
            var group = vm.Groups.Single(group => group.Title == "测试剧");
            vm.ToggleGroup(group);
            Assert.Equal(0, group.Count);
            Assert.Equal(2, group.Members.Count);
            vm.SetSelected(group.Members, true);
            Assert.Equal(2, vm.SelectedCount);
            Assert.Equal("1,2", string.Join(",", vm.Select().Select(history => history.Id)));
            vm.ToggleGroup(group);
            Assert.Equal(2, group.Count);
            Assert.True(group.All(row => row.Selected));
            vm.Grouped = false;
            Assert.Equal(2, vm.SelectedCount);
            vm.ClearSelection();
            Assert.Equal(0, vm.SelectedCount);
        });
        Test("剧清单VM：空筛选、恢复与失败状态文案相互独立", () =>
        {
            using var fixture = new SeriesViewFixture();
            var vm = fixture.ViewModel;
            vm.SelectAll();
            vm.SearchText = "不存在的资源";
            Assert.Equal(Visibility.Visible, vm.EmptyVisibility);
            Assert.Contains("搜索", vm.EmptyTitle);
            Assert.Equal(0, vm.SelectedCount);
            Assert.False(vm.CanContinue);
            Assert.Throws<MoviePilotException>(() => vm.Select());
            vm.SearchText = "";
            Assert.Equal(Visibility.Collapsed, vm.EmptyVisibility);
            Assert.Equal(3, vm.Rows.Count);
            Assert.Equal(0, vm.SelectedCount);
            fixture.Transport.EmptyHistory = true;
            ResourceFixture.Complete(vm.ReloadAsync());
            Assert.Contains("当前剧", vm.EmptyTitle);
            Assert.Contains("重新打开", vm.EmptyMessage);
            Assert.Equal(Visibility.Visible, vm.EmptyVisibility);
            fixture.Transport.FailHistory = true;
            ResourceFixture.Complete(vm.ReloadAsync());
            Assert.True(vm.NoticeOpen);
            Assert.Contains("重新打开", vm.NoticeTitle);
            Assert.Equal(Visibility.Collapsed, vm.EmptyVisibility);
        });
        Test("剧清单VM：刷新保留筛选偏好，但撤销旧资源选择", () =>
        {
            using var fixture = new SeriesViewFixture();
            var vm = fixture.ViewModel;
            vm.SearchText = "s01e02";
            vm.SelectAll();
            vm.Sort = MoviePilotTransferSort.Title;
            vm.Descending = true;
            vm.Grouped = true;
            ResourceFixture.Complete(vm.ReloadAsync());
            Assert.Equal("s01e02", vm.SearchText);
            Assert.Equal(MoviePilotTransferSort.Title, vm.Sort);
            Assert.True(vm.Descending && vm.Grouped);
            Assert.Equal("2", Ids(vm));
            Assert.Equal(0, vm.SelectedCount);
        });
        Test("剧清单VM：行勾选通知实际计数和按钮状态，清空一次通知同步原生列表", () =>
        {
            using var fixture = new SeriesViewFixture();
            var vm = fixture.ViewModel;
            var changes = new List<string>();
            var selections = 0;
            vm.PropertyChanged += (_, args) => changes.Add(args.PropertyName ?? "");
            vm.ResourceSelectionChanged += () => selections++;
            vm.Rows[0].Selected = true;
            Assert.Equal(1, vm.SelectedCount);
            Assert.True(vm.CanContinue);
            Assert.True(changes.Contains(nameof(vm.SelectedCount)) && changes.Contains(nameof(vm.CanContinue)));
            vm.ClearSelection();
            Assert.Equal(0, vm.SelectedCount);
            Assert.Equal(2, selections);
        });
        Test("剧清单VM：关闭后迟到响应不恢复资源，连接改变拒绝继续", () =>
        {
            using var fixture = new SeriesViewFixture();
            var vm = fixture.ViewModel;
            vm.SelectAll();
            fixture.Settings.MoviePilot.Url = "https://changed.invalid";
            Assert.False(vm.CanContinue);
            Assert.Throws<MoviePilotException>(() => vm.Select());
            vm.Dispose();
            vm.SearchText = "测试";
            Assert.Equal(0, vm.Rows.Count);
            Assert.Equal(0, vm.Groups.Count);
            Assert.False(vm.CanContinue);
        });
        Test("剧清单VM：在途加载输入采用最新筛选，取消后晚回零可执行记录", () =>
        {
            using var fixture = new SeriesViewFixture();
            fixture.Transport.PendingHistory = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var vm = fixture.ViewModel;
            var loading = vm.ReloadAsync();
            Assert.True(vm.Busy);
            vm.SearchText = "S01E02";
            fixture.Transport.PendingHistory.SetResult(true);
            SeriesViewFixture.CompletePending(loading);
            Assert.Equal("2", Ids(vm));
            fixture.Transport.PendingHistory = new(TaskCreationOptions.RunContinuationsAsynchronously);
            loading = vm.ReloadAsync();
            vm.Dispose();
            fixture.Transport.PendingHistory.SetResult(true);
            SeriesViewFixture.CompletePending(loading);
            Assert.Equal(0, vm.Rows.Count);
            Assert.False(vm.CanContinue);
        });
        Test("剧清单封面：列表里的小封面采用小图圆角", () =>
        {
            var document = XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/MoviePilotSeriesReorganizeDialog.xaml"));
            var thumbnail = document.Descendants().Single(element => element.Name.LocalName == "Border"
                && (string?)element.Attribute("Width") == "48" && (string?)element.Attribute("Height") == "72");
            Assert.Equal("{StaticResource EgSmallArtworkCornerRadius}", (string?)thumbnail.Attribute("CornerRadius"));
            Assert.Equal("{StaticResource EgFrameStyle}", (string?)thumbnail.Attribute("Style"));
        });
        Test("剧清单模板：原生搜索、筛选排序菜单、真实分组与单集边界保留", () =>
        {
            var document = XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/MoviePilotSeriesReorganizeDialog.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            XElement Named(string name) => document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == name);
            const string toolbarRadius = "{StaticResource EgSearchCornerRadius}";
            Assert.False(document.Descendants().Any(element => (string?)element.Attribute(x + "Name") == "ReloadResources"), "工具栏不再显示刷新按钮");
            foreach (var name in new[] { "StatusFilterButton", "ResourceSortButton", "ResourceViewButton", "ToggleResourceSelection" })
                Assert.Equal(toolbarRadius, (string?)Named(name).Attribute("CornerRadius"), name);
            var toolbarStyle = document.Descendants().Single(element => (string?)element.Attribute(x + "Key") == "SeriesResourceToolbarButtonStyle");
            const string toolbarHeight = "{StaticResource SeriesResourceToolbarHeight}";
            Assert.Equal(toolbarHeight, (string?)Named("ResourceSearch").Attribute("Height"));
            Assert.Equal(toolbarHeight, (string?)Named("ResourceViewButton").Attribute("Height"));
            Assert.Equal("{StaticResource EgSecondaryButtonStyle}", (string?)toolbarStyle.Attribute("BasedOn"));
            foreach (var property in new[] { "Height", "MinHeight" })
                Assert.Equal(toolbarHeight, (string?)toolbarStyle.Elements().Single(element => (string?)element.Attribute("Property") == property).Attribute("Value"));
            foreach (var name in new[] { "StatusFilterButton", "ResourceSortButton" })
                Assert.Equal("{StaticResource SeriesResourceToolbarButtonStyle}", (string?)Named(name).Attribute("Style"), name);
            var menuStyle = document.Descendants().Single(element => (string?)element.Attribute(x + "Key") == "SeriesResourceMenuStyle");
            Assert.Equal(toolbarRadius, (string?)menuStyle.Elements().SingleOrDefault(element =>
                (string?)element.Attribute("Property") == "CornerRadius")?.Attribute("Value"));
            Assert.Equal("AutoSuggestBox", Named("ResourceSearch").Name.LocalName);
            Assert.Equal("OnSearchTextChanged", (string?)Named("ResourceSearch").Attribute("TextChanged"));
            Assert.Equal("OnSearchSubmitted", (string?)Named("ResourceSearch").Attribute("QuerySubmitted"));
            Assert.Equal("{x:Bind ViewModel.SearchText, Mode=TwoWay}", (string?)Named("ResourceSearch").Attribute("Text"));
            Assert.Equal("True", (string?)Named("SeriesGroupsSource").Attribute("IsSourceGrouped"));
            Assert.Equal("None", (string?)Named("SeriesResources").Attribute("SelectionMode"));
            Assert.Equal("True", (string?)Named("SeriesResources").Attribute("IsItemClickEnabled"));
            Assert.Equal("OnResourceClicked", (string?)Named("SeriesResources").Attribute("ItemClick"));
            Assert.Equal("{x:Bind Selected, Mode=TwoWay}", (string?)Named("SelectSeriesResource").Attribute("IsChecked"));
            Assert.False(Named("SelectSeriesResource").Attributes().Any(attribute => attribute.Name.LocalName is "Checked" or "Unchecked"));
            var code = File.ReadAllText(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/MoviePilotSeriesReorganizeDialog.xaml.cs"));
            Assert.Contains("ViewModel.Grouped ? (object)SeriesGroupsSource.View : ViewModel.Rows", code);
            Assert.DoesNotContain("SeriesResources.SelectedItems", code);
            foreach (var name in new[] { "StatusAll", "StatusSuccess", "StatusFailed", "SortDefault", "SortTitle", "SortPath", "SortMode", "SortSize", "SortTime", "SortStatus", "ViewList", "ViewGrouped" })
                Assert.Equal("RadioMenuFlyoutItem", Named(name).Name.LocalName);
            Assert.Equal("{x:Bind ViewModel.DirectionVisibility, Mode=OneWay}", (string?)Named("SortDirection").Attribute("Visibility"));
            Assert.Equal("OnGroupToggle", (string?)Named("ToggleSeriesResourceGroup").Attribute("Click"));
            Assert.Equal("OnSelectGroup", (string?)Named("SelectSeriesResourceGroup").Attribute("Click"));
            foreach (var menu in new[] { "StatusFilterMenu", "ResourceSortMenu", "ResourceViewMenu" })
            {
                Assert.Equal("Bottom", (string?)Named(menu).Attribute("Placement"));
                Assert.Equal("True", (string?)Named(menu).Attribute("ShouldConstrainToRootBounds"));
                Assert.Equal("{StaticResource SeriesResourceMenuStyle}", (string?)Named(menu).Attribute("MenuFlyoutPresenterStyle"));
                foreach (var option in Named(menu).Elements().Where(element => element.Name.LocalName == "RadioMenuFlyoutItem"))
                    Assert.Equal("{StaticResource SeriesResourceRadioItemStyle}", (string?)option.Attribute("Style"), "菜单单选项共用紧凑样式");
            }
            Assert.Equal("{StaticResource SeriesResourceMenuItemStyle}", (string?)Named("SortDirection").Attribute("Style"));
            var toolbar = Named("ResourceToolbar");
            Assert.Equal("Horizontal", (string?)toolbar.Attribute("Orientation"));
            foreach (var name in new[] { "ToggleResourceSelection", "StatusFilterButton", "ResourceSortButton", "ResourceViewButton" })
                Assert.Equal(toolbar, Named(name).Parent, name + " 与工具栏操作保持同一行");
            Assert.Equal("{x:Bind ViewModel.SelectionActionLabel, Mode=OneWay}", (string?)Named("ToggleResourceSelection").Attribute("Content"));
            Assert.Equal("OnToggleSelection", (string?)Named("ToggleResourceSelection").Attribute("Click"));
            foreach (var (key, baseStyle) in new[] { ("SeriesResourceRadioItemStyle", "DefaultRadioMenuFlyoutItemStyle"),
                ("SeriesResourceMenuItemStyle", "DefaultMenuFlyoutItemStyle") })
            {
                var style = document.Descendants().Single(element => (string?)element.Attribute(x + "Key") == key);
                Assert.Equal("{StaticResource " + baseStyle + "}", (string?)style.Attribute("BasedOn"));
                Assert.Equal("{StaticResource EgListRowCornerRadius}", (string?)style.Elements().Single(element =>
                    (string?)element.Attribute("Property") == "CornerRadius").Attribute("Value"));
                Assert.Equal("{StaticResource SeriesResourceMenuItemPadding}", (string?)style.Elements().Single(element =>
                    (string?)element.Attribute("Property") == "Padding").Attribute("Value"));
            }
        });
    }

    private static string Ids(MoviePilotSeriesReorganizeViewModel vm) => string.Join(",", vm.Rows.Select(row => row.History.Id));
}

internal sealed class SeriesViewFixture : IDisposable
{
    internal AppSettings Settings { get; } = new();
    internal SeriesViewTransport Transport { get; }
    internal MoviePilotService Service { get; }
    internal MoviePilotSeriesReorganizeViewModel ViewModel { get; } = new();
    private readonly MoviePilotClient _client;

    internal SeriesViewFixture(bool alternateTitle = false)
    {
        Settings.MoviePilot.Enabled = true;
        Settings.MoviePilot.Url = "https://series-fixture.invalid";
        Settings.MoviePilot.Username = "anonymous-fixture";
        Settings.MoviePilot.ProtectedPassword = "anonymous-password";
        Transport = new(alternateTitle);
        _client = new(Transport);
        Service = new(_client, new MoviePilotCredentials(PassthroughSecretProtector.Instance), Settings);
        ResourceFixture.Complete(Service.RefreshAvailabilityAsync());
        ViewModel.Attach(Service, new("测试剧", MoviePilotTransferRequest.TypeSeries, "283938", null, "", []) { BrowseSeriesHistory = true });
        ResourceFixture.Complete(ViewModel.ReloadAsync());
    }

    internal static void CompletePending(Task task)
    {
        using var finished = new ManualResetEventSlim();
        _ = task.ContinueWith(_ => finished.Set(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Assert.True(finished.Wait(TimeSpan.FromSeconds(5)), "挂起的匿名清单任务没有完成");
        ResourceFixture.Complete(task);
    }

    public void Dispose()
    {
        ViewModel.Dispose();
        Assert.Equal(0, Transport.Writes);
        Service.Dispose();
        _client.Dispose();
    }
}

internal sealed class SeriesViewTransport(bool alternateTitle) : HttpMessageHandler
{
    internal int HistoryReads { get; private set; }
    internal int Writes { get; private set; }
    internal bool FailHistory { get; set; }
    internal bool EmptyHistory { get; set; }
    internal TaskCompletionSource<bool>? PendingHistory { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal("series-fixture.invalid", request.RequestUri!.Host);
        var path = request.RequestUri.AbsolutePath;
        if (path.EndsWith("login/access-token", StringComparison.Ordinal)) return Json(new { access_token = "anonymous-token" });
        if (path.EndsWith("dashboard/system", StringComparison.Ordinal)) return Json(new { version = "fixture" });
        if (path.EndsWith("history/transfer", StringComparison.Ordinal))
        {
            HistoryReads++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("title=tmdbid%3D283938", request.RequestUri.Query);
            if (PendingHistory is { } pending) await pending.Task;
            if (FailHistory) return new(HttpStatusCode.BadRequest) { Content = new StringContent("fixture failure") };
            var ids = EmptyHistory ? [] : new[] { 3, 1, 2 };
            var records = ids.Select(id => new
            {
                id,
                title = alternateTitle && id == 3 ? "测试剧 " : "测试剧",
                type = "电视剧",
                media_source = "themoviedb",
                media_id = "283938",
                tmdbid = "283938",
                seasons = "S01",
                episodes = id == 3 ? "E10" : "E0" + id,
                mode = "softlink",
                status = id != 1,
                date = "2026-10-28 12:00:00",
                src = "/downloads/source-" + id + ".mkv",
                dest = "/library/episode-" + id + ".mkv",
                src_fileitem = new { storage = "local", type = "file", path = "/downloads/source-" + id + ".mkv", size = id * 100 },
                dest_fileitem = new { storage = "local", type = "file", path = "/library/episode-" + id + ".mkv" }
            });
            return Json(new { success = true, data = new { total = ids.Length, list = records } });
        }
        if (request.Method != HttpMethod.Get) Writes++;
        throw new InvalidOperationException("匿名清单夹具拒绝未登记的接口：" + path);
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json") };
}
