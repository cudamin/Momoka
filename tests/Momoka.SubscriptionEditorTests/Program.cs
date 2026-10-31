using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.SubscriptionFixture;

namespace Momoka.Tests;

internal static class Program
{
    private const string Snapshot = """{"id":12,"name":"测试剧","type":"电视剧","media_source":"themoviedb","media_id":"42","season":1,"total_episode":45,"start_episode":0,"episode_group":"old-group","quality":"custom quality","sites":[8,99],"filter_groups":["旧规则"],"downloader":"旧下载器","save_path":"/old/sub","search_interval":36,"best_version":1,"best_version_full":1,"search_imdbid":1,"custom_words":"old words","media_category":"动画","state":"R","unknown":{"keep":true}}""";
    private static readonly List<(string, Func<Task>)> Cases = [];
    private static async Task<int> Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Add("原订阅草稿往返不发送任何未改字段", () =>
        {
            var original = Subscription(); var vm = Create(original);
            Equal(1, vm.Read().Changes(original).Count); Equal("old-group", vm.Read().EpisodeGroup);
            Equal("custom quality", vm.Quality!.Value); Equal("/old/sub", vm.SavePath);
            return Task.CompletedTask;
        });
        Add("周期由自定义切系统显式发送 null，其余字段省略", () =>
        {
            var original = Subscription(); var vm = Create(original); vm.IntervalMode = 0;
            var changes = vm.Read().Changes(original); Equal(2, changes.Count); True(changes["search_interval"] is null);
            Equal(36d, vm.SearchInterval); vm.IntervalMode = 1; Equal(1, vm.Read().Changes(original).Count);
            return Task.CompletedTask;
        });
        foreach (var value in new[] { double.NaN, 0, -1, 8761, 2.5 }) Add($"拒绝无效周期 {value}", () =>
        {
            var vm = Create(Subscription()); vm.SearchInterval = value; Throws<MoviePilotException>(() => vm.Read()); return Task.CompletedTask;
        });
        Add("质量预设提交真实正则，保留当前自定义规则", () =>
        {
            var original = Subscription(); var vm = Create(original);
            vm.Quality = vm.Qualities.Single(item => item.Label == "WEB-DL");
            vm.Resolution = vm.Resolutions.Single(item => item.Label == "4K");
            Equal("WEB-?DL|WEB-?RIP", vm.Read().Changes(original)["quality"]);
            Equal("4K|2160p|x2160", vm.Read().Changes(original)["resolution"]);
            return Task.CompletedTask;
        });
        Add("洗版关闭隐藏全集选项而保留偏好，请求关闭两项", () =>
        {
            var original = Subscription(); var vm = Create(original); vm.BestVersion = false;
            Equal(Visibility.Collapsed, vm.FullUpgradeVisibility); True(vm.BestVersionFull);
            var changes = vm.Read().Changes(original); Equal(0, changes["best_version"]); Equal(0, changes["best_version_full"]);
            vm.BestVersion = true; Equal(Visibility.Visible, vm.FullUpgradeVisibility); Equal(1, vm.Read().Changes(original).Count);
            return Task.CompletedTask;
        });
        Add("电影隐藏季集和剧集组且不改其存量值", () =>
        {
            var original = MoviePilotSubscription.Parse(Json(Snapshot.Replace("电视剧", "电影")))!;
            var vm = Create(original); Equal(Visibility.Collapsed, vm.SeriesVisibility); Equal(Visibility.Collapsed, vm.GroupVisibility);
            vm.TotalEpisodes = double.NaN; vm.Season = -1; Equal(1, vm.Read().Changes(original).Count); return Task.CompletedTask;
        });
        Add("非 TMDB 剧不显示剧集组选项", () =>
        {
            var vm = Create(MoviePilotSubscription.Parse(Json(Snapshot.Replace("themoviedb", "douban")))!);
            Equal(Visibility.Collapsed, vm.GroupVisibility); Equal("old-group", vm.Read().EpisodeGroup); return Task.CompletedTask;
        });
        Add("站点和规则清空提交空数组，路径清空提交 null", () =>
        {
            var original = Subscription(); var vm = Create(original);
            foreach (var row in vm.Sites) row.Selected = false;
            foreach (var row in vm.FilterGroups) row.Selected = false;
            vm.SavePath = "";
            var changes = vm.Read().Changes(original); Equal(0, ((int[])changes["sites"]!).Length); Equal(0, ((string[])changes["filter_groups"]!).Length); True(changes["save_path"] is null);
            Equal("跟随系统", vm.SitesSummary); return Task.CompletedTask;
        });
        Add("目录取 download_path，按路径去重且展示合并别名", () =>
        {
            var choices = MoviePilotSubscriptionPresets.DownloadPaths(Json("""[{"name":"剧集","download_path":"/tv","save_path":"remote:/tv"},{"name":"动画","download_path":"/tv"},{"name":"电影","download_path":"/movie"}]"""));
            Equal(2, choices.Count); Equal("/tv", choices[0].Value); Equal("剧集 / 动画 (/tv)", choices[0].Label); Equal("/tv", choices[0].ToString()); return Task.CompletedTask;
        });
        Add("读取所有目录不丢失存量未知选项与用户已输入草稿", async () =>
        {
            using var fixture = await Connected(); var original = await fixture.ExistingAsync(12); var vm = Create(original, fixture.Service);
            vm.Keyword = "新关键词"; vm.SavePath = "/typed/path"; await vm.ReloadAsync();
            Equal("新关键词", vm.Keyword); Equal("/typed/path", vm.SavePath); Equal("旧下载器", vm.Downloader!.Value); Equal("old-group", vm.EpisodeGroup!.Value);
            True(vm.Sites.Any(row => row.Value == "99" && row.Selected)); True(vm.FilterGroups.Any(row => row.Value == "旧规则" && row.Selected));
            True(vm.Sites.All(row => row.Value != "2")); Equal("/downloads", vm.Paths[0].Value); Equal(0, fixture.Http.Writes);
            True(fixture.Http.Requests.Any(request => request.Path == "rule/groups" && request.Query == "?include_usage=false"));
        });
        Add("部分目录失败显示重试且保留旧选择，其他目录仍加载", async () =>
        {
            using var fixture = await Connected("site/rss"); var vm = Create(await fixture.ExistingAsync(12), fixture.Service); await vm.ReloadAsync();
            True(vm.NoticeOpen); True(vm.NoticeMessage!.Contains("订阅站点", StringComparison.Ordinal)); True(vm.Sites.Any(row => row.Selected && row.Value == "99"));
            True(vm.Downloaders.Any(item => item.Value == "PT")); Equal(0, fixture.Http.Writes);
        });
        Add("选项读取迟到不能复活已关闭编辑器", async () =>
        {
            using var fixture = await Connected(); var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var previous = fixture.Http.Before;
            fixture.Http.Before = async (request, token) =>
            {
                if (request.Path == "site/rss") { read.SetResult(); await release.Task; }
                return previous is null ? null : await previous(request, token);
            };
            var vm = Create(await fixture.ExistingAsync(12), fixture.Service); var task = vm.ReloadAsync(); await read.Task;
            vm.Cancel(); release.SetResult(); await task; Equal(0, vm.Paths.Count); True(vm.Sites.All(row => row.Label != "站点甲"));
        });
        Add("编辑实际 PUT 只发送本次变化，未知服务器字段保留", async () =>
        {
            using var fixture = await Connected(); var original = await fixture.ExistingAsync(12); var vm = Create(original, fixture.Service);
            vm.Keyword = "changed"; vm.SearchImdbId = false; vm.MediaCategory = "";
            await fixture.Service.UpdateSubscriptionAsync(original, vm.Read(), default);
            var sent = fixture.Http.Requests.Single(request => request.Method == "PUT"); Equal(4, sent.Body.EnumerateObject().Count());
            Equal(0, sent.Body.GetProperty("search_imdbid").GetInt32()); Equal(JsonValueKind.Null, sent.Body.GetProperty("media_category").ValueKind);
            True(fixture.Http.Subscriptions.Single().GetProperty("unknown").GetProperty("keep").GetBoolean());
        });
        Add("失败后草稿保持并且未知写不重复发送", async () =>
        {
            using var fixture = await Connected(); var original = await fixture.ExistingAsync(12); var vm = Create(original, fixture.Service); vm.Keyword = "保留草稿";
            var previous = fixture.Http.Before;
            fixture.Http.Before = (request, token) => request.Method == "PUT" ? throw new HttpRequestException("fixture lost response") : previous!(request, token);
            await ThrowsAsync<Exception>(() => fixture.Service.UpdateSubscriptionAsync(original, vm.Read(), default));
            Equal("保留草稿", vm.Keyword);
            await ThrowsAsync<MoviePilotOperationBlockedException>(() => fixture.Service.UpdateSubscriptionAsync(original, vm.Read(), default));
            Equal(1, fixture.Http.Requests.Count(request => request.Method == "PUT"));
        });
        Add("XAML 基础进阶与全字段接线完整且保存中关闭受保护", () =>
        {
            var document = XDocument.Parse(Resource("Editor.xaml")); XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var names = document.Descendants().Select(element => (string?)element.Attribute(x + "Name")).Where(name => name is not null).ToHashSet();
            foreach (var name in new[] { "EditorTabs", "BasicFields", "AdvancedFields", "SubscriptionSitesInput", "SearchIntervalMode", "DownloaderInput", "SavePathInput", "BestVersionInput", "BestVersionFullInput", "SearchImdbInput", "FilterGroupsInput", "SeasonInput", "EpisodeGroupInput", "CustomWordsInput", "ConfirmRemoveSubscription" }) True(names.Contains(name), name);
            var source = Resource("Editor.cs"); True(source.Contains("dialog.Closing +=", StringComparison.Ordinal)); True(source.Contains("if (editor.ViewModel.Working) args.Cancel = true", StringComparison.Ordinal));
            True(source.Contains("title.Width = editor.Width", StringComparison.Ordinal));
            True(source.Contains("root.Changed -= OnRootChanged", StringComparison.Ordinal));
            True(source.Contains("editor.SavePathInput.Text = editor.ViewModel.SavePath", StringComparison.Ordinal));
            True(source.Contains("ToggleSwitchFillOn", StringComparison.Ordinal));
            True(source.IndexOf("await save(draft)", StringComparison.Ordinal) < source.IndexOf("editor._saved = true", StringComparison.Ordinal)); return Task.CompletedTask;
        });
        var failed = 0;
        foreach (var (name, test) in Cases)
        {
            try { await test(); Console.WriteLine("通过 " + name); }
            catch (Exception error) { failed++; Console.WriteLine("失败 " + name + "\n" + error); }
        }
        Console.WriteLine($"共 {Cases.Count} 项，通过 {Cases.Count - failed}，失败 {failed}，跳过 0");
        return failed == 0 ? 0 : 1;
    }
    private static void Add(string name, Func<Task> test) => Cases.Add((name, test));
    private static MoviePilotSubscription Subscription() => MoviePilotSubscription.Parse(Json(Snapshot))!;
    private static MoviePilotSubscriptionEditorViewModel Create(MoviePilotSubscription subscription, MoviePilotService? service = null)
    {
        var vm = new MoviePilotSubscriptionEditorViewModel(); vm.Attach(subscription, service); return vm;
    }
    private static async Task<SubscriptionFixture> Connected(string? fail = null)
    {
        var fixture = new SubscriptionFixture(); fixture.Seed(Json(Snapshot));
        fixture.Http.Before = (request, _) => Task.FromResult<HttpResponseMessage?>(request.Path == fail ? FakeMoviePilot.Reply(new { detail = "fixture unavailable" }, HttpStatusCode.ServiceUnavailable) : request.Path switch
        {
            "site/rss" => FakeMoviePilot.Reply(Json("""[{"id":8,"name":"站点甲","is_active":true},{"id":2,"name":"停用站点","is_active":false}]""")),
            "download/clients" => FakeMoviePilot.Reply(Json("""[{"name":"PT"}]""")),
            "download/paths" => FakeMoviePilot.Reply(Json("""[{"name":"剧集","download_path":"/downloads","save_path":"remote:/downloads"}]""")),
            "rule/groups" => FakeMoviePilot.Reply(Json("""{"rule_groups":[{"name":"剧集优先"}]}""")),
            _ => null
        });
        await fixture.ConnectAsync(); return fixture;
    }
    private static string Resource(string name) { using var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!); return reader.ReadToEnd(); }
    private static void True(bool value, string message = "") { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new InvalidOperationException($"期望 {expected}，实际 {actual}"); }
    private static void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new InvalidOperationException("期望 " + typeof(T).Name); }
    private static async Task ThrowsAsync<T>(Func<Task> run) where T : Exception { try { await run(); } catch (T) { return; } throw new InvalidOperationException("期望 " + typeof(T).Name); }
}
