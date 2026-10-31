using System.Reflection;
using System.Runtime.CompilerServices;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Momoka.Tests;

internal static class MoviePilotLifetimeTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> cases)
    {
        void Test(string name, Func<Task> run) => cases.Add(("PERF-01 " + name, run));
        Test("恢复通知重入新搜索，旧快照不覆盖新空态与通知", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service); await vm.SearchAsync("旧词");
            var saved = vm.Capture()! with { EmptyNotice = "旧空态", ShowEmptyNotice = true, NoticeTitle = "旧通知", NoticeOpen = true };
            vm.Release(); vm.Attach(f.Service);
            Task? fresh = null; var reentered = false;
            vm.PropertyChanged += (_, args) =>
            {
                if (!reentered && args.PropertyName == nameof(vm.EmptyNotice) && vm.EmptyNotice == "旧空态")
                { reentered = true; fresh = vm.SearchAsync("新词"); }
            };
            Check.False(vm.Restore(saved)); await fresh!;
            Check.Contains("新词", vm.Heading); Check.False(vm.ShowEmptyNotice); Check.False(vm.NoticeOpen);
            Check.Equal(1, vm.Results.Count);
        });
        Test("搜索返回快照保留词与结果，释放图片和旧行后重接动作且不重搜", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            var panel = new MoviePilotSearchPanel(); var opened = 0;
            panel.Attach(f.Service, new Launcher(), (_, _) => opened++);
            await panel.SearchAsync("保留这个词"); var old = panel.ViewModel.Results.Single(); var poster = old.Poster!;
            panel.Results.ScrollView!.VerticalOffset = 183;
            var offset = panel.ScrollOffset;
            var snapshot = panel.ViewModel.Capture();
            var searches = f.Http.Searches;
            panel.Release();
            Check.Equal(0, f.Subscribers); Check.Equal(0, panel.ViewModel.Results.Count); Check.Null(old.Poster);
            Check.Equal(0, poster.Subscribers);
            panel.Attach(f.Service, new Launcher(), (_, _) => opened++);
            Check.True(panel.ViewModel.Restore(snapshot)); panel.RestoreScroll(offset);
            Check.Contains("保留这个词", panel.ViewModel.Heading); Check.Equal(183d, panel.ScrollOffset);
            Check.Equal(searches, f.Http.Searches); Check.Equal(1, f.Subscribers);
            var fresh = panel.ViewModel.Results.Single(); Check.False(ReferenceEquals(old, fresh));
            panel.OpenResult(old); Check.Equal(0, opened);
            panel.OpenResult(fresh); Check.Equal(1, opened);
            await fresh.SearchResourcesCommand.ExecuteAsync(null); Check.Equal(1, panel.ResourceOverlay.Opens);
            await panel.ViewModel.RefreshSubscriptionStatesAsync();
            Check.Same(fresh, panel.ViewModel.Results.Single()); Check.Equal(searches, f.Http.Searches);
            panel.Release();
        });
        Test("返回快照拒绝旧连接，旧状态刷新不能覆盖新搜索", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service); await vm.SearchAsync("保存");
            var snapshot = vm.Capture();
            vm.Release(); f.Settings.MoviePilot.Username = "new-anonymous-fixture"; await f.ConnectAsync(); vm.Attach(f.Service);
            Check.False(vm.Restore(snapshot)); Check.Equal(0, vm.Results.Count);
            await vm.SearchAsync("新的");
            var hold = new TaskCompletionSource<System.Net.Http.HttpResponseMessage?>();
            f.Http.Before = (request, _) => request.Path == "subscribe/" ? hold.Task : Task.FromResult<System.Net.Http.HttpResponseMessage?>(null);
            var refresh = vm.RefreshSubscriptionStatesAsync();
            f.Http.Before = null; await vm.SearchAsync("更新的"); var fresh = vm.Results.Single();
            hold.SetResult(MoviePilotTransport.Reply("[]")); await refresh;
            Check.Same(fresh, vm.Results.Single()); Check.Contains("更新的", vm.Heading);
        });
        Test("同页来源来回切换保留完成结果的详情与资源动作", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            var panel = new MoviePilotSearchPanel(); var opened = 0;
            panel.Attach(f.Service, new Launcher(), (_, _) => opened++); await panel.SearchAsync("切换");
            var row = panel.ViewModel.Results.Single(); var before = f.Http.Searches;
            panel.CancelSearch(); panel.CancelSearch();
            panel.OpenResult(row); await row.SearchResourcesCommand.ExecuteAsync(null);
            Check.Equal(1, opened); Check.Equal(1, panel.ResourceOverlay.Opens); Check.Equal(before, f.Http.Searches);
            panel.Release();
        });
        Test("未 Attach/Release 后不读、不打开，连续 Attach 只有一份订阅", async () =>
        {
            using var f = new MoviePilotFixture();
            var panel = new MoviePilotSearchPanel();
            await panel.SearchAsync("未接入");
            await panel.PrepareSearchAsync();
            Check.Equal(0, f.Http.Requests.Count);
            var stable = panel.ViewModel.Results;
            for (var i = 0; i < 25; i++)
            {
                panel.Attach(f.Service, new Launcher());
                Check.Equal(1, f.Subscribers);
                panel.Attach(f.Service, new Launcher());
                Check.Equal(1, f.Subscribers);
                panel.Release(); panel.Release();
                Check.Equal(0, f.Subscribers);
            }
            await panel.ViewModel.ReloadAsync();
            Check.Equal(0, f.Http.Requests.Count);
            Check.Same(stable, panel.ViewModel.Results);
        });
        Test("离页清结果/图片事件/动作；旧行也不能借重开的媒体入口", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            var panel = new MoviePilotSearchPanel(); var oldOpened = 0; var freshOpened = 0;
            panel.Attach(f.Service, new Launcher(), (_, _) => oldOpened++);
            await panel.SearchAsync("旧结果");
            var old = panel.ViewModel.Results.Single(); var image = old.Poster!;
            Check.Equal(1, image.Subscribers);
            panel.OpenResult(old); Check.Equal(1, oldOpened);
            panel.Release();
            Check.Equal(0, f.Subscribers); Check.Equal(0, panel.ViewModel.Results.Count);
            Check.Null(old.Poster); Check.Equal(0, image.Subscribers);
            Check.False(old.CanSubscribe); Check.False(old.CanSearchResources);
            image.Fail(); Check.False(old.CanSubscribe);
            await old.SubscribeCommand.ExecuteAsync(null); await old.SearchResourcesCommand.ExecuteAsync(null);
            panel.OpenResult(old); Check.Equal(1, oldOpened); Check.Equal(0, f.Http.Writes);
            var before = f.Http.Searches;
            panel.Attach(f.Service, new Launcher(), (_, _) => freshOpened++);
            Check.Equal(before, f.Http.Searches); Check.Equal(0, panel.ViewModel.Results.Count);
            await panel.SearchAsync("新结果"); var fresh = panel.ViewModel.Results.Single();
            panel.OpenResult(old); Check.Equal(0, freshOpened);
            panel.OpenResult(fresh); Check.Equal(1, freshOpened);
            await fresh.SearchResourcesCommand.ExecuteAsync(null); Check.Equal(1, panel.ResourceOverlay.Opens);
            panel.Release();
        });
        Test("已捕获旧订阅及断线队列不能清新访问，恢复不自动搜索", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            var panel = new MoviePilotSearchPanel(); panel.Attach(f.Service, new Launcher());
            await panel.SearchAsync("旧词"); DispatcherQueue.Drain();
            var oldHandler = f.Handlers.Single();
            f.Settings.MoviePilot.Enabled = false;
            Check.True(DispatcherQueue.Pending.Count > 0);
            panel.Release();
            f.Settings.MoviePilot.Enabled = true; await f.ConnectAsync();
            var before = f.Http.Searches;
            panel.Attach(f.Service, new Launcher());
            oldHandler(f.Service, EventArgs.Empty);
            Check.Equal(before, f.Http.Searches);
            await panel.SearchAsync("新词"); var fresh = panel.ViewModel.Results.Single();
            DispatcherQueue.Drain();
            Check.Same(fresh, panel.ViewModel.Results.Single());
            Check.False(panel.ViewModel.Busy); Check.Equal(1, f.Subscribers);
            panel.Release();
        });
        Test("同页 CancelSearch 不 Dispose、不重复订阅，重新明确搜索仍可用", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            var panel = new MoviePilotSearchPanel(); panel.Attach(f.Service, new Launcher());
            await panel.SearchAsync("首词"); var before = f.Http.Searches;
            panel.CancelSearch(); panel.CancelSearch();
            Check.Equal(1, f.Subscribers); Check.Equal(before, f.Http.Searches);
            await panel.PrepareSearchAsync(); Check.Equal(before, f.Http.Searches);
            await panel.SearchAsync("二词"); Check.Equal(before + 1, f.Http.Searches);
            Check.Equal(1, panel.ViewModel.Results.Count); panel.Release();
            using var stream = typeof(MoviePilotLifetimeTests).Assembly.GetManifestResourceStream("Production.LibraryPage.xaml.cs")!;
            using var reader = new StreamReader(stream); var source = await reader.ReadToEndAsync();
            var release = Between(source, "    public void Release()", "    protected override void OnNavigatedFrom(");
            var switchSource = Between(source, "    private void ApplySource()", "    private void OnSearchCategoryChanged(");
            Check.Contains("MoviePilotPanel.Release();", release);
            Check.Contains("MoviePilotPanel.CancelSearch();", switchSource);
            Check.False(switchSource.Contains("MoviePilotPanel.Release();", StringComparison.Ordinal));
        });
        Test("实际搜索来源：导航不受标签显隐影响，恢复不自动搜索", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            var page = new LibraryPage(); page.AttachFixture(f.Service);
            var before = f.Http.Searches;
            page.AniBtSourceTab.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            page.SwitchFixture(3);
            Check.Equal(Microsoft.UI.Xaml.Visibility.Visible, page.AniBtPanel.Visibility);
            Check.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, page.EmbyContent.Visibility);
            page.AniBtSourceTab.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            page.SwitchFixture(3);
            Check.Equal(Microsoft.UI.Xaml.Visibility.Visible, page.AniBtPanel.Visibility);
            Check.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, page.EmbyContent.Visibility);
            Check.Equal(before, f.Http.Searches);
            page.Release();
        });
        Test("实际搜索来源：MoviePilot 掉线保留选择与恢复说明，不改成媒体库", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            var page = new LibraryPage(); page.AttachFixture(f.Service);
            page.SwitchFixture(2);
            var before = f.Http.Searches;
            f.Settings.MoviePilot.Enabled = false;
            page.SwitchFixture(2);
            Check.Equal(2, page.SourceTabs.SelectedIndex);
            Check.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, page.MoviePilotResources.Visibility);
            Check.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, page.EmbyContent.Visibility);
            Check.Equal(Microsoft.UI.Xaml.Visibility.Visible, page.SearchUnavailableSurface.Visibility);
            Check.Equal("MoviePilot 尚未连接", page.SearchUnavailableTitle.Text);
            Check.Contains("设置 → MoviePilot", page.SearchUnavailableMessage.Text);
            f.Settings.MoviePilot.Enabled = true; await f.ConnectAsync();
            page.SwitchFixture(2);
            Check.Equal(2, page.SourceTabs.SelectedIndex);
            Check.Equal(Microsoft.UI.Xaml.Visibility.Visible, page.MoviePilotResources.Visibility);
            Check.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, page.SearchUnavailableSurface.Visibility);
            Check.Equal(before, f.Http.Searches);
            page.Release();
        });
        Test("实际 LibraryPage 离页/来源切换原样方法：单例退订、取消、重入与滚动快照", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            var page = new LibraryPage(); page.AttachFixture(f.Service); Check.Equal(2, f.Subscribers);
            await page.MoviePilotPanel.SearchAsync("页内词"); var before = f.Http.Searches;
            page.SwitchFixture(0); Check.Equal(2, f.Subscribers); Check.Equal(before, f.Http.Searches);
            Check.Equal(1, page.ViewModel.Resumes); Check.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, page.MoviePilotPanel.Visibility);
            page.SwitchFixture(1); Check.Equal(before, f.Http.Searches); Check.Equal(Microsoft.UI.Xaml.Visibility.Visible, page.MoviePilotPanel.Visibility);
            await page.MoviePilotPanel.SearchAsync("再次显式提交"); Check.Equal(before + 1, f.Http.Searches);
            page.NavigateFromFixture(); Check.Equal(0, f.Subscribers); Check.Equal(0, page.MoviePilotPanel.ViewModel.Results.Count);
            Check.Equal(1, page.Navigation); Check.Equal(1, page.Departures); Check.Equal(1, page.ViewModel.PosterReleases);
            Check.Equal(73d, page.SavedOffset); Check.Equal(3, page.SavedCount);
            page.Release(); Check.Equal(1, page.Navigation);
            before = f.Http.Searches; page.AttachFixture(f.Service); Check.Equal(2, f.Subscribers); Check.Equal(before, f.Http.Searches);
            await page.MoviePilotPanel.SearchAsync("返回后显式提交"); Check.Equal(before + 1, f.Http.Searches);
            page.NavigateFromFixture(); Check.Equal(0, f.Subscribers);
        });
        Test("Release 的 Busy 同步通知重接新访问，旧尾部不清新结果/请求", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            var panel = new MoviePilotSearchPanel(); panel.Attach(f.Service, new Launcher());
            var hold = new TaskCompletionSource<System.Net.Http.HttpResponseMessage?>();
            f.Http.Before = (request, _) => request.Path == "media/search" && request.Query.Contains("old", StringComparison.Ordinal)
                ? hold.Task : Task.FromResult<System.Net.Http.HttpResponseMessage?>(null);
            var old = panel.SearchAsync("old"); await Check.Until(() => panel.ViewModel.Busy);
            Task? fresh = null; var reentered = false;
            panel.ViewModel.PropertyChanged += (_, args) =>
            {
                if (!reentered && args.PropertyName == nameof(panel.ViewModel.Busy) && !panel.ViewModel.Busy)
                {
                    reentered = true; panel.Attach(f.Service, new Launcher()); fresh = panel.SearchAsync("new");
                }
            };
            panel.Release(); Check.True(reentered); await fresh!;
            var current = panel.ViewModel.Results.Single(); Check.Contains("new", panel.ViewModel.Heading); Check.Equal(1, f.Subscribers);
            hold.SetResult(MoviePilotTransport.Reply("[]")); await old;
            Check.Same(current, panel.ViewModel.Results.Single()); Check.False(panel.ViewModel.Busy);
            panel.Release();
        });
        Test("清空词同步 Ready 通知重入新查询，旧空态不清结果与新标题", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service);
            var hold = new TaskCompletionSource<System.Net.Http.HttpResponseMessage?>();
            f.Http.Before = (request, _) => request.Path == "media/search" && request.Query.Contains("old", StringComparison.Ordinal)
                ? hold.Task : Task.FromResult<System.Net.Http.HttpResponseMessage?>(null);
            var old = vm.SearchAsync("old"); await Check.Until(() => vm.Busy);
            Task? fresh = null; var reentered = false;
            vm.PropertyChanged += (_, args) =>
            {
                if (!reentered && args.PropertyName == nameof(vm.IsReady) && vm.IsReady)
                { reentered = true; fresh = vm.SearchAsync("new"); }
            };
            await vm.SearchAsync(""); await fresh!; Check.Contains("new", vm.Heading);
            Check.Equal(1, vm.Results.Count); Check.False(vm.ShowEmptyNotice); Check.False(vm.Busy);
            hold.SetResult(MoviePilotTransport.Reply("[]")); await old; Check.Equal(1, vm.Results.Count);
        });
        Test("Heading 同步通知重入新查询，旧入口不得再发自己的请求", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service);
            var reentered = false; Task? fresh = null;
            vm.PropertyChanged += (_, args) =>
            {
                if (!reentered && args.PropertyName == nameof(vm.Heading))
                { reentered = true; fresh = vm.SearchAsync("new"); }
            };
            await vm.SearchAsync("old"); await fresh!; Check.Equal(1, f.Http.Searches);
            Check.Contains("new", vm.Heading); Check.Equal(1, vm.Results.Count);
        });
        Test("50次离页的面板/VM/行/图片/回调均弱引用回收，旧队列保留也不保留页面", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            var references = new List<WeakReference>();
            for (var i = 0; i < 50; i++) references.AddRange(await CreateReleasedAsync(f));
            Check.Equal(0, f.Subscribers);
            Check.True(DispatcherQueue.Pending.Count >= 50, "故意保留旧队列以证明它不成为根");
            Collect();
            Check.Equal(0, references.Count(reference => reference.IsAlive));
            DispatcherQueue.Drain();
        });
        Test("VM保留也必须解除页面确认/资源/季/媒体闭包与结果图片", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service);
            var owner = InstallClosures(vm);
            await vm.SearchAsync("匿名"); var image = CapturePoster(vm);
            vm.Release(); Collect();
            Check.False(owner.IsAlive, "释放后仍保留页面闭包"); Check.False(image.IsAlive, "释放后仍保留旧图片"); Check.Equal(0, f.Subscribers);
        });
        Test("迟到 success 被离页取消，新查询仍拥有结果/忙态", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            var panel = new MoviePilotSearchPanel(); panel.Attach(f.Service, new Launcher());
            var hold = new TaskCompletionSource<System.Net.Http.HttpResponseMessage?>();
            CancellationToken oldToken = default;
            f.Http.Before = (request, token) =>
            {
                if (request.Path != "media/search" || !request.Query.Contains("old", StringComparison.Ordinal))
                    return Task.FromResult<System.Net.Http.HttpResponseMessage?>(null);
                oldToken = token; return hold.Task;
            };
            var old = panel.SearchAsync("old"); await Check.Until(() => oldToken.CanBeCanceled);
            panel.Release(); Check.True(oldToken.IsCancellationRequested);
            panel.Attach(f.Service, new Launcher()); await panel.SearchAsync("new");
            var fresh = panel.ViewModel.Results.Single();
            hold.SetResult(MoviePilotTransport.Reply("""[{"title":"迟到旧结果","type":"电影","media_source":"themoviedb","media_id":"old"}]"""));
            await old; DispatcherQueue.Drain();
            Check.Same(fresh, panel.ViewModel.Results.Single()); Check.False(panel.ViewModel.Busy);
            Check.False(panel.ViewModel.NoticeOpen); panel.Release();
        });
        Test("迟到 error 不报新访问错误，换服务时旧发送者也无权更新", async () =>
        {
            using var oldService = new MoviePilotFixture(); using var freshService = new MoviePilotFixture();
            await oldService.ConnectAsync(); await freshService.ConnectAsync();
            using var vm = new MoviePilotSearchViewModel(); vm.Attach(oldService.Service);
            var hold = new TaskCompletionSource<System.Net.Http.HttpResponseMessage?>();
            oldService.Http.Before = (request, _) => request.Path == "media/search" ? hold.Task : Task.FromResult<System.Net.Http.HttpResponseMessage?>(null);
            var handler = oldService.Handlers.Single(); var old = vm.SearchAsync("旧词");
            vm.Release(); vm.Attach(freshService.Service); await vm.SearchAsync("新词");
            var row = vm.Results.Single(); handler(oldService.Service, EventArgs.Empty);
            hold.SetException(new HttpRequestException("匿名迟到失败")); await old;
            Check.Same(row, vm.Results.Single()); Check.False(vm.NoticeOpen); Check.False(vm.Busy);
            Check.Equal(0, oldService.Subscribers); Check.Equal(1, freshService.Subscribers);
        });
        Test("新查询/空词都释放上批图片，订阅季旧finally不移除重开访问的操作锁", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            f.Http.SearchJson = """[{"title":"匿名剧","type":"电视剧","media_source":"themoviedb","media_id":"42","poster_path":"https://poster.invalid/fixture.jpg"}]""";
            using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service);
            var first = new TaskCompletionSource(); var second = new TaskCompletionSource(); var picks = 0;
            vm.UseSeasonPicker((_, _) => { picks++; return first.Task; });
            await vm.SearchAsync("一词"); var row = vm.Results.Single(); var oldImage = row.Poster!;
            var oldAction = row.SubscribeCommand.ExecuteAsync(null); Check.Equal(1, picks);
            vm.Release(); Check.Null(row.Poster); Check.Equal(0, oldImage.Subscribers);
            vm.Attach(f.Service); vm.UseSeasonPicker((_, _) => { picks++; return second.Task; });
            await vm.SearchAsync("二词"); var fresh = vm.Results.Single();
            var newAction = fresh.SubscribeCommand.ExecuteAsync(null); Check.Equal(2, picks);
            first.SetResult(); await oldAction;
            Check.Equal(MoviePilotSubscribeState.Working, fresh.State);
            var alias = new MoviePilotResult(fresh.Media, _ => Task.CompletedTask, _ => Task.CompletedTask);
            // Actual callback plus a same-identity extra current row tests the VM's per-visit lock.
            var method = typeof(MoviePilotSearchViewModel).GetMethod("SubscribeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            vm.Results.Add(alias); await (Task)method.Invoke(vm, [alias])!;
            Check.Equal(2, picks);
            second.SetResult(); await newAction;
            var freshImage = fresh.Poster!;
            await vm.SearchAsync(""); Check.Equal(0, vm.Results.Count); Check.Null(fresh.Poster); Check.Equal(0, freshImage.Subscribers);
            Check.Equal(0, f.Http.Writes);
        });
        Test("同步 Busy 通知重入新查询，只新查询发读取；永久 Dispose 不可重新 Attach", async () =>
        {
            using var f = new MoviePilotFixture(); await f.ConnectAsync();
            using var vm = new MoviePilotSearchViewModel(); vm.Attach(f.Service);
            Task? replacement = null; var once = false;
            vm.PropertyChanged += (_, args) =>
            {
                if (!once && args.PropertyName == nameof(vm.Busy) && vm.Busy)
                { once = true; replacement = vm.SearchAsync("new"); }
            };
            await vm.SearchAsync("old"); await replacement!;
            Check.Equal(1, f.Http.Searches); Check.Contains("new", vm.Heading);
            vm.Dispose(); vm.Attach(f.Service); await vm.SearchAsync("不得重开");
            Check.Equal(0, f.Subscribers); Check.Equal(1, f.Http.Searches);
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference[]> CreateReleasedAsync(MoviePilotFixture f)
    {
        var panel = new MoviePilotSearchPanel(); var callbackOwner = new CallbackOwner();
        panel.Attach(f.Service, new Launcher(), callbackOwner.Open);
        await panel.SearchAsync("匿名");
        var row = panel.ViewModel.Results.Single(); var poster = row.Poster!;
        var refs = new[] { new WeakReference(panel), new WeakReference(panel.ViewModel), new WeakReference(row),
            new WeakReference(poster), new WeakReference(poster.Payload), new WeakReference(callbackOwner) };
        f.Handlers.Single()(f.Service, EventArgs.Empty);
        panel.Release(); return refs;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CapturePoster(MoviePilotSearchViewModel vm) => new(vm.Results.Single().Poster!);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference InstallClosures(MoviePilotSearchViewModel vm)
    {
        var owner = new CallbackOwner();
        vm.UseConfirm(owner.Confirm); vm.UseResourceOpener(owner.Resources);
        vm.UseSeasonPicker(owner.Picker); vm.UseMediaOpener(owner.Open);
        return new(owner);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Collect() { for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); } }
    private sealed class CallbackOwner
    {
        internal void Open(MoviePilotMedia media, MoviePilotSubscription? subscription) { }
        internal Task<bool> Confirm(string title, string message, string primary) => Task.FromResult(false);
        internal Task Resources(MoviePilotMedia media) => Task.CompletedTask;
        internal Task Picker(MoviePilotMedia media, CancellationToken token) => Task.CompletedTask;
    }
    private static string Between(string source, string start, string end)
    {
        var begin = source.IndexOf(start, StringComparison.Ordinal); Check.True(begin >= 0);
        var finish = source.IndexOf(end, begin + start.Length, StringComparison.Ordinal); Check.True(finish > begin);
        return source[begin..finish];
    }
}
