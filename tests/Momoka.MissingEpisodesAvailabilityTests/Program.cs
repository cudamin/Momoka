using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Tests;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Run)> Cases = [];
    private static void Case(string name, Func<Task> run) => Cases.Add((name, run));
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var report = ParseReport(args);
        Console.WriteLine("R-041：原样提取真实 Shell 方法＋完整 source-linked Dialog/行；匿名 scope/假 HTTP；无 WinUI 或真实搜索");
        Console.WriteLine($"source revision={ProductionProvenance.Revision}; shell={ProductionProvenance.ShellSha256}; method={ProductionProvenance.MethodSha256}");
        Case("实际源码来源与 XAML 行/容器绑定边界", () =>
        {
            Provenance();
            XamlBoundary();
            return Task.CompletedTask;
        });
        foreach (var anime in new[] { false, true }) Register(anime);
        Case("父剧补齐动画资格仍按原 MP 连接授权", async () =>
        {
            await using var f = new MissingEpisodesFixture(season: true, parentAnime: true);
            f.Available(true);
            var opening = f.Open();
            var dialog = await f.NextDialog();
            Visible(dialog, anime: true);
            dialog.InvokeAniBt(f.Missing[0]);
            await f.Finish(opening, dialog);
            Check.Equal(1, f.Shell.AniBtQueries.Count, "AniBT handoff count");
            Check.Equal("Anonymous parent", f.Shell.AniBtQueries.Single().Title, "actual parent metadata handoff");
            Check.Equal(2, f.Transport.Requests.Count, "detail and parent only");
        });
        Case("正常季缺集 MP 词仍由父剧与季号补齐", async () =>
        {
            await using var f = new MissingEpisodesFixture(season: true);
            f.Available(true);
            var opening = f.Open();
            var dialog = await f.NextDialog();
            Visible(dialog, anime: false);
            dialog.InvokeMoviePilot(f.Missing[0]);
            await f.Finish(opening, dialog);
            Check.Equal("Anonymous parent S01 E02", f.Shell.MoviePilotQueries.Single(), "actual query factory");
        });
        Case("清单内容与本地筛选不因隐藏搜索而丢失", async () =>
        {
            await using var f = new MissingEpisodesFixture();
            var special = new EmbyItem { Id = "virtual-special", Type = EmbyItemType.Episode, Name = "special", ParentIndexNumber = 0, IndexNumber = 1 };
            var future = new EmbyItem
            {
                Id = "virtual-future",
                Type = EmbyItemType.Episode,
                Name = "future",
                ParentIndexNumber = 1,
                IndexNumber = 3,
                PremiereDate = new DateTimeOffset(2999, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };
            var opening = f.Open([f.Missing[0], special, future]);
            var dialog = await f.NextDialog();
            Hidden(dialog);
            var list = dialog.ListSource;
            dialog.SetFilters(specials: true, unaired: true);
            Check.Equal(3, dialog.Rows.Count, "actual MissingShown includes special/future");
            Check.True(ReferenceEquals(list, dialog.ListSource), "actual bound collection stays stable");
            Check.True(dialog.Rows.All(row => row.SearchVisibility == Visibility.Collapsed && row.AnimeSearchVisibility == Visibility.Collapsed), "every shown row stays hidden");
            dialog.SetFilters(specials: false, unaired: false);
            Check.Equal(1, dialog.Rows.Count, "actual filters restore");
            await f.Finish(opening, dialog);
        });
        Case("读取中已有清单操作不重复启动", async () =>
        {
            await using var f = new MissingEpisodesFixture();
            f.Available(true);
            f.Transport.Detail.Hold = true;
            var first = f.Open();
            await f.Transport.Detail.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            var second = f.Open();
            await second.WaitAsync(TimeSpan.FromSeconds(10));
            Check.Equal(1, f.Transport.Requests.Count, "showing guard rejects second read");
            f.Transport.Detail.Release();
            var dialog = await f.NextDialog();
            await f.Finish(first, dialog);
            Check.Equal(1, f.Dialogs.Dialogs.Count, "single actual dialog");
        });
        Case("无服务或无 XamlRoot 进入保护保持", async () =>
        {
            await using var f = new MissingEpisodesFixture();
            var detached = new ShellPage(null, f.Session);
            await detached.OpenMissing(f.Owner, f.Missing);
            f.Shell.Root.XamlRoot = null;
            await f.Open();
            Check.Equal(0, f.Transport.Requests.Count, "entry guards reject reads");
            Check.Equal(0, f.Dialogs.Dialogs.Count, "entry guards reject dialog");
        });
        Case("原 scope 仍在线时不得借用新的默认来源", async () =>
        {
            await using var f = new MissingEpisodesFixture();
            f.Available(true);
            f.Transport.Detail.Hold = true;
            var opening = f.Open();
            await f.Transport.Detail.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            f.InstallScope("new-default");
            Check.True(f.Scope.IsAvailable, "old source stays online");
            Check.False(f.Scope.IsCurrent, "default source changed");
            f.Transport.Detail.Release();
            var dialog = await f.NextDialog();
            Visible(dialog, anime: false);
            Check.True(ReferenceEquals(f.Scope, dialog.Rows.Single().Episode.Source), "actual original scope preserved");
            await f.Finish(opening, dialog);
        });
        Case("原 scope 撤销后取图委托不改用新默认来源", async () =>
        {
            await using var f = new MissingEpisodesFixture();
            f.Available(true);
            var opening = f.Open();
            var dialog = await f.NextDialog();
            f.InstallScope("new-default");
            f.RevokeOriginal();
            var before = f.Transport.Requests.Count;
            var bytes = await dialog.Fetch(new ArtworkRef("owner-series", "Thumb", "anonymous-tag"));
            Check.True(bytes is null, "unavailable captured image scope yields null");
            Check.Equal(before, f.Transport.Requests.Count, "no fallback image read");
            await f.Finish(opening, dialog);
        });

        var results = new List<CaseResult>();
        foreach (var (name, run) in Cases)
        {
            try
            {
                await run().WaitAsync(TimeSpan.FromSeconds(20));
                results.Add(new CaseResult(name, "passed", null));
                Console.WriteLine("  通过  " + name);
            }
            catch (Exception error)
            {
                results.Add(new CaseResult(name, "failed", error.ToString()));
                Console.WriteLine("  失败  " + name + "：" + error.Message);
            }
        }
        var failed = results.Count(result => result.Status == "failed");
        var observations = MissingEpisodesFixture.Observations.ToArray();
        var exit = failed == 0 ? 0 : 1;
        Console.WriteLine($"共 {results.Count} 项，通过 {results.Count - failed}，失败 {failed}，跳过 0，退出 {exit}");
        Console.WriteLine($"匿名读取 {observations.Sum(o => o.Requests.Length)}；写请求 {observations.Sum(o => o.WriteRequests)}；MP HTTP {observations.Sum(o => o.MoviePilotHttpAttempts)}；凭据/DPAPI {observations.Sum(o => o.SecretAttempts)}；实际网络 0");
        if (report is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new
            {
                record = "R-041",
                revision = ProductionProvenance.Revision,
                shellSource = ProductionProvenance.ShellFile,
                shellSha256 = ProductionProvenance.ShellSha256,
                methodSha256 = ProductionProvenance.MethodSha256,
                dialogSha256 = ProductionProvenance.DialogSha256,
                xamlSha256 = ProductionProvenance.XamlSha256,
                fixtureAssemblySha256 = FileHash(Assembly.GetExecutingAssembly().Location),
                coreAssemblySha256 = FileHash(typeof(EmbySession).Assembly.Location),
                total = results.Count,
                passed = results.Count - failed,
                failed,
                skipped = 0,
                exitCode = exit,
                anonymousReadRequests = observations.Sum(o => o.Requests.Length),
                writeRequests = observations.Sum(o => o.WriteRequests),
                moviePilotHttpAttempts = observations.Sum(o => o.MoviePilotHttpAttempts),
                forbiddenMediaRequests = observations.Sum(o => o.ForbiddenRequests),
                secretAttempts = observations.Sum(o => o.SecretAttempts),
                realNetworkRequests = 0,
                coverage = "verbatim Shell method + complete source-linked Dialog/row + actual Core scope/availability; UI values, generated controls and search/window endpoint are substitutes",
                runtimeGaps = new[] { "WinUI pixels/layout/focus/hit-testing", "actual user clicks", "real DispatcherQueue/ContentDialog timing", "resource window/client search", "real server/login/DPAPI/media/native" },
                cases = results
            }, new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
        }
        return exit;
    }

    private static void Register(bool anime)
    {
        var suffix = anime ? " [动画来源下拉]" : " [MP单入口]";
        Case("进入前仅启用而未连接隐藏" + suffix, async () =>
        {
            await using var f = new MissingEpisodesFixture(anime: anime);
            var opening = f.Open();
            var dialog = await f.NextDialog();
            Hidden(dialog);
            Check.Equal(0, f.Transport.Requests.Count, "no metadata supplementation before availability");
            await f.Finish(opening, dialog);
            f.AssertNoSearch();
        });
        Case("进入前关闭隐藏" + suffix, async () =>
        {
            await using var f = new MissingEpisodesFixture(anime: anime);
            f.Available(true);
            f.Settings.MoviePilot.Enabled = false;
            var opening = f.Open();
            var dialog = await f.NextDialog();
            Hidden(dialog);
            Check.Equal(0, f.Transport.Requests.Count, "disabled skips supplement");
            await f.Finish(opening, dialog);
            f.AssertNoSearch();
        });
        Case("进入前已断线隐藏" + suffix, async () =>
        {
            await using var f = new MissingEpisodesFixture(anime: anime);
            f.Available(true);
            f.Available(false);
            var opening = f.Open();
            var dialog = await f.NextDialog();
            Hidden(dialog);
            Check.Equal(0, f.Transport.Requests.Count, "disconnected skips supplement");
            await f.Finish(opening, dialog);
        });
        foreach (var replacement in new[] { false, true })
        {
            var changed = replacement ? "换成新可用连接" : "断线";
            Case("详情补读挂起期间" + changed + suffix, async () =>
            {
                await using var f = new MissingEpisodesFixture(anime: anime);
                f.Available(true);
                var original = f.MoviePilot.ConnectionStamp;
                f.Transport.Detail.Hold = true;
                var opening = f.Open();
                await f.Transport.Detail.Entered.WaitAsync(TimeSpan.FromSeconds(10));
                Check.Equal(0, f.AvailabilitySubscribers, "actual subscription begins only at dialog");
                if (replacement) f.ReplaceMoviePilot(); else f.Available(false);
                if (replacement)
                {
                    Check.True(f.MoviePilot.Available, "replacement already available");
                    Check.False(f.MoviePilot.IsCurrentConnection(original), "must not adopt replacement stamp");
                }
                f.Transport.Detail.Release();
                var dialog = await f.NextDialog();
                Hidden(dialog);
                Check.Equal(1, f.Transport.Requests.Count, "detail only");
                await f.Finish(opening, dialog);
                f.AssertNoSearch();
            });
            Case("详情已返回但父剧仍挂起期间" + changed + suffix, async () =>
            {
                await using var f = new MissingEpisodesFixture(season: true, anime: anime);
                f.Available(true);
                f.Transport.Parent.Hold = true;
                var opening = f.Open();
                await f.Transport.Parent.Entered.WaitAsync(TimeSpan.FromSeconds(10));
                Check.Equal(2, f.Transport.Requests.Count, "detail finished and parent entered");
                Check.Equal(0, f.Dialogs.Dialogs.Count, "dialog still not constructed");
                if (replacement) f.ReplaceMoviePilot(); else f.Available(false);
                f.Transport.Parent.Release();
                var dialog = await f.NextDialog();
                Hidden(dialog);
                await f.Finish(opening, dialog);
                f.AssertNoSearch();
            });
            Case("已打开后" + changed + "撤销" + suffix, async () =>
            {
                await using var f = new MissingEpisodesFixture(anime: anime);
                f.Available(true);
                var opening = f.Open();
                var dialog = await f.NextDialog();
                Visible(dialog, anime);
                Check.Equal(1, f.AvailabilitySubscribers, "actual event subscribed");
                if (replacement) f.ReplaceMoviePilot(); else f.Available(false);
                Check.True(f.Shell.DispatcherQueue.Count > 0, "actual event enqueues Hide");
                f.Shell.DispatcherQueue.Drain();
                await f.Finish(opening, dialog);
                Check.False(dialog.IsOpen, "dialog revoked");
                f.AssertNoSearch();
            });
            Case("已选择并关闭后才" + changed + "仍拒绝搜索" + suffix, async () =>
            {
                await using var f = new MissingEpisodesFixture(anime: anime);
                f.Available(true);
                f.Dialogs.DeferCloseCompletion = true;
                var opening = f.Open();
                var dialog = await f.NextDialog();
                Visible(dialog, anime);
                if (anime) dialog.InvokeAniBt(f.Missing[0]); else dialog.InvokeMoviePilot(f.Missing[0]);
                Check.False(dialog.IsOpen, "actual ChooseSearch called Hide");
                Check.False(opening.IsCompleted, "Shell still awaits closed boundary");
                if (replacement) f.ReplaceMoviePilot(); else f.Available(false);
                await f.Finish(opening, dialog);
                f.AssertNoSearch();
            });
        }
        Case("补读断线后恢复重开可用" + suffix, async () =>
        {
            await using var f = new MissingEpisodesFixture(anime: anime);
            f.Available(true);
            f.Transport.Detail.Hold = true;
            var first = f.Open();
            await f.Transport.Detail.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            f.Available(false);
            f.Transport.Detail.Release();
            var unavailable = await f.NextDialog();
            Hidden(unavailable);
            await f.Finish(first, unavailable);
            f.Available(true);
            var second = f.Open();
            var restored = await f.NextDialog();
            Visible(restored, anime);
            if (anime) restored.InvokeAniBt(f.Missing[0]); else restored.InvokeMoviePilot(f.Missing[0]);
            await f.Finish(second, restored);
            Check.Equal(1, f.SearchHandoffs, "restored fresh operation handoff only");
        });
        Case("弹前断线点击陈旧搜索不得关闭清单或静默交接" + suffix, async () =>
        {
            await using var f = new MissingEpisodesFixture(anime: anime);
            f.Available(true);
            f.Transport.Detail.Hold = true;
            var opening = f.Open();
            await f.Transport.Detail.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            f.Available(false);
            f.Transport.Detail.Release();
            var dialog = await f.NextDialog();
            // Invoke the actual production handler before inspecting row values: the pre-fix
            // control flow chooses a stale search and closes this list with no handoff.
            if (anime) dialog.InvokeAniBt(f.Missing[0]); else dialog.InvokeMoviePilot(f.Missing[0]);
            var stayedOpen = dialog.IsOpen;
            var selected = dialog.SearchEpisode;
            await f.Finish(opening, dialog);
            Check.True(stayedOpen, "rejected stale choice must not close the list");
            Check.True(selected is null, "stale choice must not select an episode");
            f.AssertNoSearch();
        });
        Case("补读中换连接不自动认领但重新打开可用" + suffix, async () =>
        {
            await using var f = new MissingEpisodesFixture(anime: anime);
            f.Available(true);
            f.Transport.Detail.Hold = true;
            var first = f.Open();
            await f.Transport.Detail.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            f.ReplaceMoviePilot();
            f.Transport.Detail.Release();
            var oldOperation = await f.NextDialog();
            Hidden(oldOperation);
            await f.Finish(first, oldOperation);
            var second = f.Open();
            var newOperation = await f.NextDialog();
            Visible(newOperation, anime);
            if (anime) newOperation.InvokeAniBt(f.Missing[0]); else newOperation.InvokeMoviePilot(f.Missing[0]);
            await f.Finish(second, newOperation);
            Check.Equal(1, f.SearchHandoffs, "only new explicit operation can adopt new connection");
        });
        Case("已打开后断线撤销再恢复重开" + suffix, async () =>
        {
            await using var f = new MissingEpisodesFixture(anime: anime);
            f.Available(true);
            var first = f.Open();
            var original = await f.NextDialog();
            f.Available(false);
            f.Shell.DispatcherQueue.Drain();
            await f.Finish(first, original);
            f.Available(true);
            var second = f.Open();
            var restored = await f.NextDialog();
            Visible(restored, anime);
            await f.Finish(second, restored);
            f.AssertNoSearch();
        });
        Case("进入不可用后恢复不借本次清单认领连接" + suffix, async () =>
        {
            await using var f = new MissingEpisodesFixture(anime: anime);
            var first = f.Open();
            var unavailable = await f.NextDialog();
            Hidden(unavailable);
            f.Available(true);
            Hidden(unavailable);
            await f.Finish(first, unavailable);
            var second = f.Open();
            var restored = await f.NextDialog();
            Visible(restored, anime);
            await f.Finish(second, restored);
            f.AssertNoSearch();
        });
        Case("原连接正常选择只在清单关闭后交接" + suffix, async () =>
        {
            await using var f = new MissingEpisodesFixture(anime: anime);
            f.Available(true);
            f.Dialogs.DeferCloseCompletion = true;
            var opening = f.Open();
            var dialog = await f.NextDialog();
            Visible(dialog, anime);
            if (anime) dialog.InvokeAniBt(f.Missing[0]); else dialog.InvokeMoviePilot(f.Missing[0]);
            Check.True(ReferenceEquals(f.Missing[0], dialog.SearchEpisode), "actual selected episode");
            f.AssertNoSearch();
            await f.Finish(opening, dialog);
            Check.Equal(1, f.SearchHandoffs, "handoff after closed completion");
            Check.Equal(anime, dialog.SearchWithAniBt, "actual source choice");
        });
        foreach (var parent in new[] { false, true })
        {
            foreach (var revoked in new[] { false, true })
            {
                Case((parent ? "父剧" : "详情") + "挂起后" + (revoked ? "原scope撤销" : "旧页面") + "不构造清单" + suffix, async () =>
                {
                    await using var f = new MissingEpisodesFixture(season: parent, anime: anime);
                    f.Available(true);
                    var gate = parent ? f.Transport.Parent : f.Transport.Detail;
                    gate.Hold = true;
                    var opening = f.Open();
                    await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
                    if (revoked) f.RevokeOriginal(); else f.Shell.ContentFrame.Content = new object();
                    gate.Release();
                    await opening.WaitAsync(TimeSpan.FromSeconds(10));
                    Check.Equal(0, f.Dialogs.Dialogs.Count, "stale operation never constructs dialog");
                    Check.False(f.Shell.MissingShowing, "old/cancelled showing guard released");
                    f.AssertNoSearch();
                });
            }
        }
        foreach (var revoked in new[] { false, true })
        {
            Case("已打开选择后" + (revoked ? "原scope撤销" : "旧页面") + "拒绝交接" + suffix, async () =>
            {
                await using var f = new MissingEpisodesFixture(anime: anime);
                f.Available(true);
                f.Dialogs.DeferCloseCompletion = true;
                var opening = f.Open();
                var dialog = await f.NextDialog();
                if (anime) dialog.InvokeAniBt(f.Missing[0]); else dialog.InvokeMoviePilot(f.Missing[0]);
                if (revoked) f.RevokeOriginal(); else f.Shell.ContentFrame.Content = new object();
                await f.Finish(opening, dialog);
                f.AssertNoSearch();
            });
        }
        Case("详情失败且期间断线仍隐藏入口" + suffix, async () =>
        {
            await using var f = new MissingEpisodesFixture(anime: anime);
            f.Available(true);
            f.Transport.Detail.Hold = true;
            f.Transport.DetailStatus = HttpStatusCode.InternalServerError;
            var opening = f.Open();
            await f.Transport.Detail.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            f.Available(false);
            f.Transport.Detail.Release();
            var dialog = await f.NextDialog();
            Hidden(dialog);
            Check.Equal(0, f.Shell.Notices.Count, "supplement failure retains readable list");
            await f.Finish(opening, dialog);
        });
        Case("原连接可用时详情失败继续使用原条目" + suffix, async () =>
        {
            await using var f = new MissingEpisodesFixture(anime: anime);
            f.Available(true);
            f.Transport.DetailStatus = HttpStatusCode.InternalServerError;
            var opening = f.Open();
            var dialog = await f.NextDialog();
            Visible(dialog, anime);
            if (anime) dialog.InvokeAniBt(f.Missing[0]); else dialog.InvokeMoviePilot(f.Missing[0]);
            await f.Finish(opening, dialog);
            Check.Equal(1, f.SearchHandoffs, "current connection still works on readable fallback");
        });
    }

    private static void Hidden(MissingEpisodesDialog dialog)
    {
        Check.Equal(1, dialog.Rows.Count, "readable missing episode retained");
        Check.Equal(Visibility.Visible, dialog.ListVisibility, "valid list stays visible");
        Check.True(dialog.Rows.All(row => row.SearchVisibility == Visibility.Collapsed), "MP row entrance must collapse");
        Check.True(dialog.Rows.All(row => row.AnimeSearchVisibility == Visibility.Collapsed), "source-dropdown container must collapse");
        Check.True(dialog.SummaryText.Length > 0, "actual heading retained");
        var row = dialog.Rows.Single();
        dialog.InvokeMoviePilot(row.Episode);
        dialog.InvokeAniBt(row.Episode);
        Check.True(dialog.SearchEpisode is null, "actual handlers reject unavailable choice");
        Check.True(dialog.IsOpen, "rejected click must not close list");
    }
    private static void Visible(MissingEpisodesDialog dialog, bool anime)
    {
        Check.Equal(1, dialog.Rows.Count, "actual list row count");
        var row = dialog.Rows.Single();
        Check.Equal(anime ? Visibility.Collapsed : Visibility.Visible, row.SearchVisibility, "actual MP visibility");
        Check.Equal(anime ? Visibility.Visible : Visibility.Collapsed, row.AnimeSearchVisibility, "actual source-container visibility");
    }
    private static string? ParseReport(string[] args)
    {
        if (args.Length == 0) return null;
        if (args.Length != 2 || args[0] != "--report") throw new ArgumentException("only --report <absolute in-tree path> accepted");
        var path = Path.GetFullPath(args[1]);
        var root = Path.GetFullPath(ProductionProvenance.Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.IsPathFullyQualified(args[1]) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("report must stay in this worktree");
        return path;
    }
    private static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Provenance()
    {
        Check.Equal(ProductionProvenance.ShellSha256, FileHash(ProductionProvenance.ShellFile), "built Shell source must match current input");
        Check.Equal(ProductionProvenance.DialogSha256, FileHash(ProductionProvenance.DialogFile), "linked Dialog must match current source");
        Check.Equal(ProductionProvenance.XamlSha256, FileHash(ProductionProvenance.XamlFile), "XAML must match linked source");
    }
    private static void XamlBoundary()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Production.MissingEpisodesDialog.xaml")
            ?? throw new TestFailure("actual XAML resource absent");
        var xml = XDocument.Load(stream);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement Named(string name) => xml.Descendants().Single(element => (string?)element.Attribute(x + "Name") == name);
        var mp = Named("SearchMissingMoviePilot");
        var sources = Named("SearchMissingSources");
        Check.Equal("{x:Bind SearchVisibility}", (string?)mp.Attribute("Visibility"), "actual MP binding");
        Check.Equal("OnSearchMoviePilot", (string?)mp.Attribute("Click"), "actual MP handler");
        Check.Equal("{x:Bind AnimeSearchVisibility}", (string?)sources.Attribute("Visibility"), "actual source container binding");
        Check.True(Named("MissingMoviePilot").Ancestors().Contains(sources), "MP menu within collapsed container");
        Check.True(Named("MissingAniBt").Ancestors().Contains(sources), "AniBT menu within collapsed container");
        Check.Equal("OnSearchAniBt", (string?)Named("MissingAniBt").Attribute("Click"), "actual AniBT handler");
        Check.Equal("{x:Bind Episode}", (string?)Named("MissingAniBt").Attribute("Tag"), "actual episode handoff binding");
    }
    private sealed record CaseResult(string Name, string Status, string? Error);
}
