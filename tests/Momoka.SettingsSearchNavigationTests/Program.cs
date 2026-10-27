using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Momoka.Diagnostics;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Tests;

internal static class Program
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(4);

    private static async Task<int> Main()
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("SettingsPage.Search.cs")!;
        Console.WriteLine($"SettingsPage.Search.cs SHA256: {Convert.ToHexString(SHA256.HashData(source))}");
        (string Name, Func<Task> Run)[] tests =
        [
            ("同步位置完成并退订，随后才聚焦", SynchronousCompletionAsync),
            ("仅最终目标位置完成可聚焦，中途和错误位置均继续等待", CorrelatedCompletionAsync),
            ("旧extent不能提前ChangeView，实际extent到达后定位", DelayedExtentAsync),
            ("小窗口容器迟到及零高度时继续等待", LateContainerAsync),
            ("坏suggestion拒绝且不回退，空提交使用首个结果", SuggestionAsync),
            ("快速二次搜索使旧队列不布局不滚动不聚焦", ReplacedQueueAsync),
            ("旧滚动完成不清新pending、不抢新焦点", ReplacedScrollAsync),
            ("分类离开使布局等待及完成回调失效", CategoryLeaveAsync),
            ("Release取消布局等待及已排队导航", CancelLayoutAsync),
            ("Release取消完成等待并清理事件，重入只聚焦新任务", CancelScrollAsync),
            ("dispatcher拒绝及仅分类结果不保留pending", DispatcherRefusedAsync),
            ("ChangeView异常仍退订并记录失败、不聚焦", ScrollFailureAsync),
            ("完成超时退订并记录失败、不聚焦", CompletionTimeoutAsync),
            ("焦点自动滚动不能覆盖搜索位置，完成后解除专属守卫", FocusBringIntoViewAsync),
            ("滚动请求拒绝时不聚焦、退订且不保留临时偏好", ClampedOffsetAsync)
        ];
        var failed = 0;
        foreach (var (name, run) in tests)
        {
            Log.Warnings.Clear();
            try { await run().WaitAsync(Deadline); Console.WriteLine($"通过 {name}"); }
            catch (Exception error) { failed++; Console.WriteLine($"失败 {name}: {error}"); }
        }
        Console.WriteLine($"共 {tests.Length} 项，通过 {tests.Length - failed}，失败 {failed}，跳过 0");
        return failed == 0 ? 0 : 1;
    }

    private static async Task SynchronousCompletionAsync()
    {
        using var f = new Fixture();
        f.Page.SettingsScroll.OnScroll = request =>
        {
            Check(f.Page.SettingsScroll.Subscribers == 1 && f.Focus.FocusCalls == 0);
            f.Page.SettingsScroll.Complete(request.CorrelationId);
            Check(f.Focus.FocusCalls == 0, "同步事件发生在ChangeView返回之前，不应提前聚焦");
        };
        f.Start();
        await f.CleanAsync();
        Check(f.Focus.FocusCalls == 1 && f.SubscribersAtFocus == 0);
        var request = f.Page.SettingsScroll.Requests.Single();
        Check(request.X == 0 && request.Y == 820);
        Check(request.DisableAnimation);
        using var noOp = new Fixture();
        noOp.Page.SettingsScroll.NoOperation = true;
        noOp.Start();
        await noOp.CleanAsync();
        Check(noOp.Focus.FocusCalls == 1 && noOp.Page.SettingsScroll.Requests.Count == 1);
    }

    private static async Task CorrelatedCompletionAsync()
    {
        using var f = new Fixture();
        f.Start();
        var id = await f.Started.Task;
        Check(f.Page.Pending && f.Focus.FocusCalls == 0 && f.Page.SettingsScroll.Subscribers == 1);
        f.Page.SettingsScroll.RaiseViewChanged(100, false);
        await Task.Delay(16);
        Check(f.Page.Pending && f.Focus.FocusCalls == 0);
        f.Page.SettingsScroll.RaiseViewChanged(820, true);
        await Task.Delay(16);
        Check(f.Page.Pending && f.Focus.FocusCalls == 0);
        f.Page.SettingsScroll.Complete(id);
        await f.CleanAsync();
        Check(f.Focus.FocusCalls == 1 && f.SubscribersAtFocus == 0);
    }

    private static async Task DelayedExtentAsync()
    {
        using var f = new Fixture();
        var secondLayout = Signal();
        f.Page.SettingsScroll.ExtentHeight = 100;
        f.Page.SettingsContent.Layout = () => { if (f.Page.SettingsContent.LayoutCalls >= 2) secondLayout.TrySetResult(); };
        f.Start();
        await secondLayout.Task.WaitAsync(Deadline);
        Check(f.Page.Pending && f.Page.SettingsScroll.Requests.Count == 0 && f.Focus.FocusCalls == 0);
        f.Page.SettingsScroll.ExtentHeight = f.Page.SettingsCardsContent.ActualHeight;
        f.Page.SettingsScroll.Complete(await f.Started.Task.WaitAsync(Deadline));
        await f.CleanAsync();
        Check(f.Page.SettingsContent.LayoutCalls >= 2 && f.Focus.FocusCalls == 1);
    }

    private static async Task LateContainerAsync()
    {
        using var f = new Fixture();
        var missing = Signal();
        var zeroHeight = Signal();
        f.Page.SettingsScroll.ActualHeight = 120;
        f.List.Containers.Clear();
        f.Target.ActualHeight = 0;
        f.Page.SettingsContent.Layout = () =>
        {
            if (f.List.Containers.Count == 0 && f.Page.SettingsContent.LayoutCalls >= 2) missing.TrySetResult();
            if (f.List.Containers.Count > 0 && f.Target.ActualHeight == 0) zeroHeight.TrySetResult();
        };
        f.Start();
        await missing.Task.WaitAsync(Deadline);
        Check(f.Page.SettingsScroll.Requests.Count == 0);
        f.List.Containers.Add(f.Row, f.Container);
        await zeroHeight.Task.WaitAsync(Deadline);
        Check(f.Page.Pending && f.Page.SettingsScroll.Requests.Count == 0 && f.Focus.FocusCalls == 0);
        f.Target.ActualHeight = 40;
        f.Page.SettingsScroll.Complete(await f.Started.Task.WaitAsync(Deadline));
        await f.CleanAsync();
        Check(f.Focus.FocusCalls == 1 && f.Page.SettingsScroll.Requests.Single().Y == 876);
    }

    private static async Task SuggestionAsync()
    {
        using var f = new Fixture();
        var box = new AutoSuggestBox { IsSuggestionListOpen = true };
        f.Page.ViewModel.SettingsSearchResults.Add(f.Result);
        f.Page.ViewModel.Accept = false;
        f.Page.Submit(box, new SettingsSearchResult("invalid", "", "bad", new(), new()));
        Check(!box.IsSuggestionListOpen && f.Page.ViewModel.SelectionCalls == 1 && f.Page.ViewModel.SelectedCategory == "old");
        Check(f.Page.DispatcherQueue.Count == 0 && !f.Page.HasPendingOwner && f.Page.SettingsScroll.Requests.Count == 0);
        f.Page.ViewModel.Accept = true;
        box.IsSuggestionListOpen = true;
        f.Page.Submit(box, null);
        Check(!box.IsSuggestionListOpen && f.Page.Pending && f.Page.DispatcherQueue.Count == 1);
        f.Page.DispatcherQueue.RunOne();
        f.Page.SettingsScroll.Complete(await f.Started.Task);
        await f.CleanAsync();
        Check(f.Focus.FocusCalls == 1);
    }

    private static async Task ReplacedQueueAsync()
    {
        using var f = new Fixture();
        Check(f.Page.NavigateSettingsSearchResult(f.Result));
        var (next, focus) = f.AddResult(1400);
        Check(f.Page.NavigateSettingsSearchResult(next));
        f.Page.DispatcherQueue.RunOne();
        Check(f.Page.Pending && f.Page.SettingsContent.LayoutCalls == 0 && f.Page.SettingsScroll.Requests.Count == 0);
        f.Page.DispatcherQueue.RunOne();
        f.Page.SettingsScroll.Complete(await f.Started.Task);
        await f.CleanAsync();
        Check(f.Focus.FocusCalls == 0 && focus.FocusCalls == 1 && f.Page.SettingsScroll.Requests.Single().Y == 1320);
    }

    private static async Task ReplacedScrollAsync()
    {
        using var f = new Fixture();
        f.Start();
        var first = await f.Started.Task;
        var (next, focus) = f.AddResult(1400);
        Check(f.Page.NavigateSettingsSearchResult(next));
        f.Page.DispatcherQueue.RunOne();
        await UntilAsync(() => f.Page.SettingsScroll.Requests.Count == 2);
        Check(f.Page.SettingsScroll.Subscribers == 2);
        var second = f.Page.SettingsScroll.Requests.Last().CorrelationId;
        f.Page.SettingsScroll.Complete(first);
        await UntilAsync(() => f.Page.SettingsScroll.Subscribers == 1);
        Check(f.Page.Pending && f.Focus.FocusCalls == 0 && focus.FocusCalls == 0);
        f.Page.SettingsScroll.Complete(second);
        await f.CleanAsync();
        Check(f.Focus.FocusCalls == 0 && focus.FocusCalls == 1);
    }

    private static async Task CategoryLeaveAsync()
    {
        using var layout = new Fixture();
        layout.List.Containers.Clear();
        layout.Start();
        layout.Page.ViewModel.SelectedCategory = "other";
        await layout.CleanAsync();
        Check(layout.Page.SettingsScroll.Requests.Count == 0 && layout.Focus.FocusCalls == 0);
        using var scroll = new Fixture();
        scroll.Start();
        var active = await scroll.Started.Task;
        scroll.Page.ViewModel.SelectedCategory = "other";
        scroll.Page.SettingsScroll.Complete(active);
        await scroll.CleanAsync();
        Check(scroll.Focus.FocusCalls == 0 && Log.Warnings.Count == 0);
    }

    private static async Task CancelLayoutAsync()
    {
        using var layout = new Fixture();
        layout.List.Containers.Clear();
        layout.Start();
        layout.Page.Release();
        await layout.CleanAsync();
        Check(layout.Page.SettingsScroll.Requests.Count == 0 && layout.Focus.FocusCalls == 0);
        using var queued = new Fixture();
        Check(queued.Page.NavigateSettingsSearchResult(queued.Result));
        queued.Page.Release();
        Check(!queued.Page.NavigateSettingsSearchResult(queued.Result) && queued.Page.ViewModel.SelectionCalls == 1);
        queued.Page.DispatcherQueue.Drain();
        await queued.CleanAsync();
        Check(queued.Page.SettingsContent.LayoutCalls == 0 && queued.Page.SettingsScroll.Requests.Count == 0 && Log.Warnings.Count == 0);
    }

    private static async Task CancelScrollAsync()
    {
        using var f = new Fixture();
        f.Start();
        var old = await f.Started.Task;
        f.Page.Release();
        await f.CleanAsync();
        f.Page.SettingsScroll.Complete(old);
        Check(f.Focus.FocusCalls == 0 && Log.Warnings.Count == 0);
        f.Page.Reenter();
        var (newResult, newFocus) = f.AddResult(1400);
        Check(f.Page.NavigateSettingsSearchResult(newResult));
        f.Page.DispatcherQueue.RunOne();
        await UntilAsync(() => f.Page.SettingsScroll.Requests.Count == 2);
        var next = f.Page.SettingsScroll.Requests.Last().CorrelationId;
        // ScrollViewer 没有关联号回执，旧位置若不同于新目标不能完成当前导航。
        f.Page.SettingsScroll.Complete(old);
        Check(f.Focus.FocusCalls == 0 && newFocus.FocusCalls == 0 && f.Page.Pending);
        f.Page.SettingsScroll.Complete(next);
        await f.CleanAsync();
        Check(f.Focus.FocusCalls == 0 && newFocus.FocusCalls == 1);
    }

    private static async Task DispatcherRefusedAsync()
    {
        using var f = new Fixture();
        f.Page.DispatcherQueue.Accept = false;
        Check(f.Page.NavigateSettingsSearchResult(f.Result));
        Check(!f.Page.HasPendingOwner && f.Page.DispatcherQueue.Count == 0 && f.Focus.FocusCalls == 0);
        f.Page.DispatcherQueue.Accept = true;
        Check(f.Page.NavigateSettingsSearchResult(f.Result));
        Check(f.Page.NavigateSettingsSearchResult(new("category", "", "hosted")));
        f.Page.DispatcherQueue.Drain();
        await f.CleanAsync();
        Check(f.Page.ViewModel.SelectedCategory == "hosted" && f.Page.SettingsScroll.Requests.Count == 0);
    }

    private static async Task ScrollFailureAsync()
    {
        using var f = new Fixture();
        f.Page.SettingsScroll.OnScroll = _ => throw new InvalidOperationException("scroll boundary failure");
        f.Start();
        await f.CleanAsync();
        Check(f.Focus.FocusCalls == 0 && Log.Warnings.Single().Message == "scroll boundary failure");
    }

    private static async Task ClampedOffsetAsync()
    {
        using var f = new Fixture();
        f.Page.SettingsScroll.Reject = true;
        f.Start();
        await f.CleanAsync();
        Check(f.Page.SettingsScroll.Requests.Count == 1 && f.Focus.FocusCalls == 0);
        Check(Log.Warnings.Single() is InvalidOperationException);
        Check(f.Page.SettingsScroll.BringIntoViewOnFocusChange);
    }

    private static async Task FocusBringIntoViewAsync()
    {
        using var f = new Fixture();
        var suppressed = false;
        f.Focus.Focused = () => suppressed = !f.Page.SettingsScroll.BringIntoViewOnFocusChange;
        f.Page.SettingsScroll.OnScroll = request => f.Page.SettingsScroll.Complete(request.CorrelationId);
        f.Start();
        await f.CleanAsync();
        Check(suppressed && f.Page.SettingsScroll.BringIntoViewOnFocusChange);
        using var originalOff = new Fixture();
        originalOff.Page.SettingsScroll.BringIntoViewOnFocusChange = false;
        originalOff.Page.SettingsScroll.OnScroll = request => originalOff.Page.SettingsScroll.Complete(request.CorrelationId);
        originalOff.Start();
        await originalOff.CleanAsync();
        Check(!originalOff.Page.SettingsScroll.BringIntoViewOnFocusChange, "不得把原本关闭的自动滚动偏好改为开启");
    }

    private static async Task CompletionTimeoutAsync()
    {
        using var f = new Fixture();
        f.Start();
        await f.CleanAsync();
        Check(f.Focus.FocusCalls == 0 && Log.Warnings.Single() is TimeoutException);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Check(bool condition, string message = "搜索导航契约不满足")
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static async Task UntilAsync(Func<bool> condition)
    {
        var start = Stopwatch.StartNew();
        while (!condition())
        {
            if (start.Elapsed > Deadline) throw new TimeoutException("等待测试边界状态超时");
            await Task.Delay(1);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public SettingsPage Page { get; } = new();
        public SettingSection Section { get; } = new();
        public SettingRow Row { get; } = new();
        public ItemsControl List { get; } = new() { Name = "SettingsRowsList" };
        public FrameworkElement Container { get; } = new();
        public FrameworkElement Target { get; } = new() { Top = 900 };
        public Control Focus { get; } = new();
        public SettingsSearchResult Result { get; }
        public TaskCompletionSource<int> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SubscribersAtFocus { get; private set; } = -1;
        public Fixture()
        {
            Result = new("row", "section", "video", Section, Row);
            Section.Rows.Add(Row);
            List.ItemsSource = Section.Rows;
            List.Containers.Add(Row, Container);
            Container.Children.Add(Target);
            Target.Children.Add(new Control { Visibility = Visibility.Collapsed });
            Target.Children.Add(new Control { IsTabStop = false });
            Target.Children.Add(Focus);
            var wrapper = new FrameworkElement();
            wrapper.Children.Add(List);
            Page.SettingsSections.Children.Add(wrapper);
            Page.SettingsScroll.OnScroll = request => Started.TrySetResult(request.CorrelationId);
            Focus.Focused = () => SubscribersAtFocus = Page.SettingsScroll.Subscribers;
        }
        public (SettingsSearchResult Result, Control Focus) AddResult(double top)
        {
            var row = new SettingRow();
            var focus = new Control { Top = top };
            Section.Rows.Add(row);
            List.Containers.Add(row, focus);
            return (new("next", "section", "video", Section, row), focus);
        }
        public void Start()
        {
            Check(Page.NavigateSettingsSearchResult(Result) && Page.Pending);
            Page.DispatcherQueue.RunOne();
        }
        public Task CleanAsync() => UntilAsync(() => !Page.HasPendingOwner && Page.SettingsScroll.Subscribers == 0);
        public void Dispose() => Page.Dispose();
    }
}
