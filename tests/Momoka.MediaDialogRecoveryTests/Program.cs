using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        foreach (var identify in new[] { true, false })
            foreach (var captureFailure in new[] { false, true })
                Test($"原样媒体UI探针 {(identify ? "Identify" : "FnNas")} pending截图失败={captureFailure}", () =>
                {
                    using var queue = new UiQueue();
                    queue.Run(async () =>
                    {
                        var probe = new Momoka.Shell.ShellNavigationProbe { FailPendingCapture = captureFailure };
                        Exception? error = null;
                        try { await (identify ? probe.IdentifyForFixtureAsync() : probe.FnNasForFixtureAsync()); }
                        catch (Exception failure) { error = failure; }
                        Assert.Equal(captureFailure, error is IOException, "截图失败不能冒成功；finally必须结清在飞保存后退出");
                        Assert.Equal(captureFailure || !identify ? 2 : 4, probe.Captures);
                    });
                });
        Test("R07-008 超限选图明确失败且不读取，取消保持无错误", () =>
        {
            using var queue = new UiQueue();
            queue.Run(async () =>
            {
                var file = new Windows.Storage.Pickers.FakeFile((64UL << 20) + 1);
                Windows.Storage.Pickers.FileOpenPicker.Selected = file;
                Exception? failure = null;
                try { await ArtworkFile.PickAsync(new Microsoft.UI.Xaml.XamlRoot()); }
                catch (Exception error) { failure = error; }
                Assert.NotNull(failure);
                Assert.Contains("64", failure!.Message);
                Assert.Equal(0, file.Opens);
                Windows.Storage.Pickers.FileOpenPicker.Selected = null;
                Assert.Null(await ArtworkFile.PickAsync(new Microsoft.UI.Xaml.XamlRoot()));
            });
        });
        foreach (var reset in new[] { false, true })
            Test($"R07-005 已确认识别写入保护关闭reset={reset}", () =>
            {
                using var queue = new UiQueue();
                queue.Run(async () =>
                {
                    var written = new TaskCompletionSource();
                    var dialog = Dialog((_, _) => written.Task, () => written.Task);
                    if (reset) dialog.Remove(); else dialog.Apply(new RemoteSearchResult { Name = "fixture" });
                    Assert.False(dialog.TryClose(), "写在飞时取消和点外必须由Closing阻止");
                    written.SetResult();
                    await queue.IdleAsync();
                    Assert.True(dialog.IsClosed && dialog.Touched);
                });
            });
        Test("R07-005 关闭搜索后迟到候选不启动预览解码", () =>
        {
            using var queue = new UiQueue();
            queue.Run(async () =>
            {
                var results = new TaskCompletionSource<List<RemoteSearchResult>>();
                var previews = 0;
                var dialog = new IdentifyDialog(new EmbyItem { Name = "fixture", Type = EmbyItemType.Movie }, "", () => Task.FromResult(new List<ExternalIdInfo>()),
                    _ => results.Task, _ => { previews++; return Task.FromResult<byte[]?>([1]); }, (_, _) => Task.CompletedTask, null);
                dialog.Search();
                Assert.True(dialog.TryClose());
                results.SetResult([new RemoteSearchResult { Name = "late", ImageUrl = "https://image.invalid/late" }]);
                await queue.IdleAsync();
                Assert.Equal(0, dialog.Candidates.Count);
                Assert.Equal(0, previews);
            });
        });
        foreach (var reset in new[] { false, true })
            Test($"R07-005 写失败保留错误与关闭恢复reset={reset}", () =>
            {
                using var queue = new UiQueue();
                queue.Run(async () =>
                {
                    var calls = 0;
                    Task Fail() { calls++; return Task.FromException(new IOException("fixture failure")); }
                    var dialog = Dialog((_, _) => Fail(), Fail);
                    if (reset) dialog.Remove(); else dialog.Apply(new RemoteSearchResult { Name = "fixture" });
                    await queue.IdleAsync();
                    Assert.Equal(1, calls);
                    Assert.False(dialog.Touched || dialog.IsClosed);
                    Assert.True(dialog.Problem.IsOpen);
                    Assert.True(dialog.TryClose());
                });
            });
        Test("R07-006 在飞关闭受保护，无改动及取消零保存", () =>
        {
            using var queue = new UiQueue();
            queue.Run(async () =>
            {
                var original = JsonNode.Parse("{\"title\":\"old\"}")!.AsObject();
                var write = new TaskCompletionSource();
                var calls = 0;
                var dialog = new FnNasMetadataDialog(original, _ => { calls++; return write.Task; });
                dialog.NameBox.Text = "new";
                dialog.Save();
                Assert.False(dialog.TryClose());
                write.SetResult();
                await queue.IdleAsync();
                Assert.Equal(1, calls);
                var noOp = new FnNasMetadataDialog(original, _ => { calls++; return Task.CompletedTask; });
                noOp.Save();
                Assert.True(noOp.IsClosed);
                Assert.Equal(1, calls);
                var canceled = new FnNasMetadataDialog(original, _ => { calls++; return Task.CompletedTask; });
                canceled.NameBox.Text = "new";
                Assert.True(canceled.TryClose());
                Assert.Equal(1, calls);
            });
        });
        Test("R07-006 飞牛保存失败保留草稿与原JSON，重试才关闭", () =>
        {
            using var queue = new UiQueue();
            queue.Run(async () =>
            {
                var detail = JsonNode.Parse("{\"title\":\"old\",\"overview\":\"old text\"}")!.AsObject();
                var calls = 0;
                var constructor = typeof(FnNasMetadataDialog).GetConstructors().Single();
                Func<JsonObject, Task> save = body =>
                {
                    calls++;
                    Assert.Equal("new", body["title"]!.GetValue<string>());
                    return calls == 1 ? Task.FromException(new IOException("fixture save failure")) : Task.CompletedTask;
                };
                var dialog = (FnNasMetadataDialog)constructor.Invoke(constructor.GetParameters().Length == 1 ? [detail] : [detail, save]);
                dialog.NameBox.Text = "new";
                dialog.Save();
                await queue.IdleAsync();
                Assert.Equal(1, calls);
                Assert.False(dialog.IsClosed);
                Assert.True(dialog.Problem.IsOpen);
                Assert.Equal("new", dialog.NameBox.Text);
                Assert.Equal("old", detail["title"]!.GetValue<string>());
                dialog.Save();
                await queue.IdleAsync();
                Assert.Equal(2, calls);
                Assert.True(dialog.IsClosed);
            });
        });
        return Run();
    }

    private static IdentifyDialog Dialog(Func<RemoteSearchResult, bool, Task> apply, Func<Task>? reset) =>
        new(new EmbyItem { Name = "fixture", Type = EmbyItemType.Movie }, "", () => Task.FromResult(new List<ExternalIdInfo>()),
            _ => Task.FromResult(new List<RemoteSearchResult>()), null, apply, reset);
}
