using System.Reflection;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using static Momoka.Tests.CoreSourceContractTests;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class CallerSourceContractTests
{
    internal static void Register()
    {
        Test("R042 外层实际 Shell 入口仍要求当前 scope 和可用 MP，源锚唯一", () =>
        {
            var source = ReadSource("ShellPage.AniBt.cs");
            var entry = Between(source, "    private async Task ShowAniBtScrapeAsync(", "        _aniBtScrapeShowing = true;");
            Assert.Contains("!scope.IsCurrent", entry);
            Assert.Contains("!moviePilot.Available", entry);
            Assert.Contains("new AniBtScrapeDialog(_services.GetRequiredService<AniBtService>(), scope, item)", source);
            Assert.Contains("await MoviePilotDialog.ShowAsync(dialog, moviePilot);", source);
        });
        Test("R042 实际菜单分派 AniBtScrape 保 current 守卫，不把独立搜索收成同一契约", () =>
        {
            var source = ReadSource("ItemCommands.cs");
            var start = Unique(source, "            case ItemCommand.AniBtScrape:");
            var invoke = Unique(source, "                shell.ShowAniBtScrape(scope, item);");
            var body = source[start..(invoke + "                shell.ShowAniBtScrape(scope, item);".Length)];
            Assert.Contains("if (!scope.IsCurrent) return;", body);
            Assert.Contains("shell.SearchAniBtVersions(item);", source);
        });
        Test("R042 实际 MoviePilotDialog 监控 MP 连接，不据此推断 Emby 来源切换或模态可达", () =>
        {
            var source = ReadSource("MoviePilotDialog.cs");
            Assert.Contains("service.AvailabilityChanged += Changed;", source);
            Assert.Contains("service.IsCurrentConnection(connection)", source);
            Assert.False(source.Contains("EmbySession", StringComparison.Ordinal),
                "当前 Emby 来源守卫在实际 VM/Core 委托，不能把 MP 关闭对话框当来源保证");
        });
        Program.AsyncCase("R042 实际 VM identify-only 有效预览零写，显式确认 Apply/Refresh 各一次", async _ =>
        {
            using var f = new SourceFixture();
            using var vm = new AniBtScrapeViewModel();
            await vm.LoadAsync(f.Service, f.Scope, f.Item);
            Assert.True(vm.CanPreview);
            Assert.False(vm.CanSubmit);
            await vm.PreviewAsync();
            Assert.True(vm.CanSubmit);
            Assert.Contains("series-1", vm.Rows.Single().PreviewDetails);
            f.AssertWrites(0, 0);
            var reads = Searches(f);
            await vm.SubmitAsync();
            f.AssertWrites(1, 1);
            Assert.Equal(reads, Searches(f));
            Assert.False(vm.CanSubmit);
            Assert.Contains("已用 Emby 识别", vm.Rows.Single().Result);
        });
        Program.AsyncCase("R042 实际 VM 初始 Load 等待中来源失效，不接纳旧行", async _ =>
        {
            using var f = new SourceFixture();
            using var vm = new AniBtScrapeViewModel();
            using var pause = f.Transport.PauseNext(request => request.Path.Contains("torrent-match", StringComparison.Ordinal));
            var load = vm.LoadAsync(f.Service, f.Scope, f.Item);
            await pause.Started.Task;
            f.SwitchToOther();
            pause.Resume();
            await load;
            Assert.Equal(0, vm.Rows.Count);
            Assert.False(vm.CanPreview);
            f.AssertWrites(0, 0);
        });
        Program.AsyncCase("R042 实际 VM Load 后聚合切来源，Preview/Submit 入口零新增请求", async _ =>
        {
            using var f = new SourceFixture();
            using var vm = new AniBtScrapeViewModel();
            await vm.LoadAsync(f.Service, f.Scope, f.Item);
            var before = f.Replies.Requests.Count;
            f.SwitchToOther();
            Assert.False(vm.CanPreview);
            await vm.PreviewAsync();
            await vm.SubmitAsync();
            Assert.Equal(before, f.Replies.Requests.Count);
            Assert.Equal(0, Searches(f));
            f.AssertWrites(0, 0);
        });
        Program.AsyncCase("R042 实际 VM 已预览后聚合切来源，旧 canSubmit 不借 available 继续", async _ =>
        {
            using var f = new SourceFixture();
            using var vm = new AniBtScrapeViewModel();
            await vm.LoadAsync(f.Service, f.Scope, f.Item);
            await vm.PreviewAsync();
            Assert.True(vm.CanSubmit);
            var before = f.Replies.Requests.Count;
            f.SwitchToOther();
            Assert.False(vm.CanSubmit);
            await vm.SubmitAsync();
            Assert.Equal(before, f.Replies.Requests.Count);
            f.AssertWrites(0, 0);
        });
        Program.AsyncCase("R042 实际 VM RemoteSearch 等待中切来源，Current 委托阻止回传确认凭证", async _ =>
        {
            using var f = new SourceFixture();
            using var vm = new AniBtScrapeViewModel();
            await vm.LoadAsync(f.Service, f.Scope, f.Item);
            using var pause = f.Transport.PauseNext(IsSearch);
            var preview = vm.PreviewAsync();
            await pause.Started.Task;
            f.SwitchToOther();
            pause.Resume();
            await preview;
            Assert.False(vm.CanSubmit);
            Assert.Equal("", vm.Rows.Single().PreviewDetails);
            Assert.False(pause.CancellationObserved, "实际聚合旧来源仍有 lifetime，仅 caller Current 拒绝");
            f.AssertWrites(0, 0);
        });
        Program.AsyncCase("R042 实际 VM 只读预览 Cancel 撤销活动/版本/LoadGeneration，零写入", async _ =>
        {
            using var f = new SourceFixture();
            using var vm = new AniBtScrapeViewModel();
            await vm.LoadAsync(f.Service, f.Scope, f.Item);
            using var pause = f.Transport.PauseNext(IsSearch);
            var preview = vm.PreviewAsync();
            await pause.Started.Task;
            vm.Cancel();
            await preview;
            Assert.True(pause.CancellationObserved);
            Assert.True(f.Scope.IsCurrent);
            Assert.False(vm.CanPreview);
            Assert.False(vm.CanSubmit);
            f.AssertWrites(0, 0);
        });
        Program.AsyncCase("R042 实际 VM mixed 整批 transfer 成功后切来源，Emby identify 和 scan 零条", async _ =>
        {
            using var f = new SourceFixture(2, mixed: true);
            using var vm = new AniBtScrapeViewModel();
            await vm.LoadAsync(f.Service, f.Scope, f.Item);
            await vm.PreviewAsync();
            f.AssertWrites(0, 0);
            using var pause = f.Transport.PauseNext(request => IsTransferWrite(request) && f.Replies.TransferWrites == 2);
            var submit = vm.SubmitAsync();
            await pause.Started.Task;
            f.SwitchToOther();
            pause.Resume();
            await submit;
            f.AssertWrites(0, 0, transfer: 2);
            Assert.Contains("已完成整理", vm.Rows[0].Result);
            Assert.Contains("已完成整理", vm.Rows[1].Result);
            Assert.Contains("取消", vm.Rows[2].Result);
            Assert.False(vm.CanSubmit);
        });
        Program.AsyncCase("R042 实际 VM Apply 已发后切来源，Current 委托阻止后续 Refresh", async _ =>
        {
            using var f = new SourceFixture();
            using var vm = new AniBtScrapeViewModel();
            await vm.LoadAsync(f.Service, f.Scope, f.Item);
            await vm.PreviewAsync();
            using var pause = f.Transport.PauseNext(IsApply);
            var submit = vm.SubmitAsync();
            await pause.Started.Task;
            f.SwitchToOther();
            pause.Resume();
            await submit;
            f.AssertWrites(1, 0);
            Assert.Contains("取消", vm.Rows.Single().Result);
            Assert.False(vm.CanSubmit);
        });
        Program.AsyncCase("R042 实际 VM Apply 在途取消预览版本，即便 source 仍 current 也拒绝 Refresh", async _ =>
        {
            using var f = new SourceFixture();
            using var vm = new AniBtScrapeViewModel();
            await vm.LoadAsync(f.Service, f.Scope, f.Item);
            await vm.PreviewAsync();
            using var pause = f.Transport.PauseNext(IsApply);
            var submit = vm.SubmitAsync();
            await pause.Started.Task;
            vm.CancelPreview();
            Assert.True(f.Scope.IsCurrent);
            pause.Resume();
            await submit;
            f.AssertWrites(1, 0);
            Assert.False(vm.CanSubmit);
        });
        Program.AsyncCase("R042 实际 Dialog 预览后聚合切来源，惰性 primary 分派不写旧库", async queue =>
        {
            using var f = new SourceFixture();
            var dialog = new AniBtScrapeDialog(f.Service, f.Scope, f.Item);
            dialog.OpenForFixture();
            await queue.IdleAsync();
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertWrites(0, 0);
            Assert.True(dialog.ViewModel.CanSubmit);
            f.SwitchToOther();
            await dialog.PrimaryForFixtureAsync(queue);
            f.AssertWrites(0, 0);
            Assert.False(dialog.ViewModel.CanSubmit);
            Assert.True(dialog.CloseForFixture());
            dialog.ViewModel.Dispose();
        });
    }

    private static string ReadSource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Production." + name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static int Unique(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0 && source.IndexOf(marker, start + marker.Length, StringComparison.Ordinal) < 0,
            "当前生产源码标记缺失/歧义：" + marker);
        return start;
    }

    private static string Between(string source, string start, string end)
    {
        var begin = Unique(source, start);
        var finish = Unique(source, end);
        Assert.True(finish > begin, "生产源码边界反序");
        return source[begin..finish];
    }
}
