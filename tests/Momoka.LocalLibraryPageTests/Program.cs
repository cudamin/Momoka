using System.Text.Json;
using Momoka.Configuration;
using Momoka.LocalMedia;
using Momoka.Shell.Platform;
using Momoka.Shell.Services;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    public static int Main()
    {
        LocalLibraryHomeTests.Register();
        LocalLibraryArtworkTests.Register();
        Test("本地库命令栏：有限列与原生溢出，整组能力隐藏且保留原入口", () =>
        {
            var xml = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "LocalLibraryPage.xaml"));
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            XElement Named(string name) => xml.Descendants().Single(element => (string?)element.Attribute(xaml + "Name") == name);
            var bar = Named("LocalLibraryCommands");
            Assert.Equal("CommandBar", bar.Name.LocalName);
            Assert.Equal("Right", (string?)bar.Attribute("DefaultLabelPosition"));
            Assert.Equal("False", (string?)bar.Attribute("IsOpen"));
            Assert.True(bar.Attribute("IsSticky") is null && bar.Attribute("IsDynamicOverflowEnabled") is null);
            Assert.Equal("{x:Bind local:LocalLibraryPage.Show(ViewModel.Available), Mode=OneWay}", (string?)bar.Attribute("Visibility"));
            var columns = bar.Parent!.Elements().Single(element => element.Name.LocalName == "Grid.ColumnDefinitions").Elements().ToArray();
            Assert.Equal("Auto", (string?)columns[0].Attribute("Width"));
            Assert.Equal("*", (string?)columns[1].Attribute("Width"));
            Assert.True(ReferenceEquals(Named("LocalLibraryCount").Parent, bar.Parent));
            foreach (var (name, handler) in new[] { ("LocalLibraryRefresh", "OnRefresh"), ("LocalLibraryOpenSource", "OnOpenSource") })
            {
                var command = Named(name);
                Assert.Equal("AppBarButton", command.Name.LocalName);
                Assert.Equal(handler, (string?)command.Attribute("Click"));
                Assert.True(ReferenceEquals(command.Parent, bar));
                Assert.False(string.IsNullOrWhiteSpace((string?)command.Attribute("Label")));
                Assert.Equal((string?)command.Attribute("Label"), (string?)command.Attribute("AutomationProperties.Name"));
                Assert.Equal((string?)bar.Attribute("Visibility"), (string?)command.Attribute("Visibility"));
            }
        });
        Test("本地库命令栏：真实页面刷新接线守卫重复提交、离页与移除", () =>
        {
            using var context = new CommandContext();
            var service = Service();
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            service.Refresh = id => { Assert.Equal("one", id); calls++; return pending.Task; };
            var page = new LocalLibraryPage();
            page.Attach(new(service, "one", new Launcher()));
            var rows = page.ViewModel.Rows;
            var row = rows.Single();
            page.InvokeRefresh();
            page.InvokeRefresh();
            Assert.Equal(1, calls);
            Assert.True(page.ViewModel.Busy);
            pending.SetResult();
            context.RunNext();
            Assert.False(page.ViewModel.Busy);
            Assert.True(ReferenceEquals(rows, page.ViewModel.Rows) && ReferenceEquals(row, rows.Single()));
            page.InvokeRefresh();
            Assert.Equal(2, calls);
            page.Release();
            page.InvokeRefresh();
            Assert.Equal(2, calls);
            page.Attach(new(service, "one", new Launcher()));
            service.Configurations.Clear();
            service.RaiseChanged();
            Assert.False(page.ViewModel.Available);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, LocalLibraryPage.Show(page.ViewModel.Available));
            page.InvokeRefresh();
            Assert.Equal(2, calls);
            page.Release();
        });
        Test("本地库命令栏：真实打开目录接线仅给当前有效目录，离页不启动", () =>
        {
            var directory = Path.Combine(FindRoot(), "artifacts", "local-library-command-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var service = Service();
            service.Configurations[0].SourceDirectory = directory;
            var launcher = new Launcher();
            var page = new LocalLibraryPage();
            page.Attach(new(service, "one", launcher));
            page.InvokeOpenSource();
            Assert.Equal(1, launcher.Opens);
            Directory.Delete(directory);
            page.InvokeOpenSource();
            Assert.Equal(1, launcher.Opens);
            Assert.True(page.ViewModel.NoticeOpen);
            page.Release();
            page.InvokeOpenSource();
            Assert.Equal(1, launcher.Opens);
        });
        Test("本地库命令栏：旧刷新失败不能覆盖重新附加的另一库", () =>
        {
            using var context = new CommandContext();
            var service = Service();
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            service.Refresh = _ => pending.Task;
            var page = new LocalLibraryPage();
            page.Attach(new(service, "one", new Launcher()));
            page.InvokeRefresh();
            pending.SetException(new IOException("anonymous refresh failure"));
            context.WaitForNext();
            page.Attach(new(service, "two", new Launcher()));
            context.RunNext();
            Assert.Equal("剧集库", page.ViewModel.Title);
            Assert.Equal("two-entry", page.ViewModel.Rows.Single().Id);
            Assert.False(page.ViewModel.Busy || page.ViewModel.NoticeOpen);
            page.Release();
        });
        Test("本地库VM：仅显示当前库已入库文件，重复通知保留行集合", () =>
        {
            var service = Service();
            using var vm = new LocalLibraryViewModel();
            var actions = new Queue<Action>();
            vm.Attach(service, "one", new Launcher(), action => { actions.Enqueue(action); return true; });
            var rows = vm.Rows;
            var row = rows.Single();
            Assert.Equal("电影库", vm.Title);
            Assert.Equal("1 项", vm.CountText);
            Assert.False(vm.CanPlay);
            service.RaiseChanged();
            actions.Dequeue()();
            Assert.True(ReferenceEquals(rows, vm.Rows));
            Assert.True(ReferenceEquals(row, vm.Rows.Single()));
        });
        Test("本地库VM：换库后旧队列通知无效，离页退订", () =>
        {
            var service = Service();
            using var vm = new LocalLibraryViewModel();
            var actions = new Queue<Action>();
            bool Dispatch(Action action) { actions.Enqueue(action); return true; }
            vm.Attach(service, "one", new Launcher(), Dispatch);
            service.RaiseChanged();
            vm.Attach(service, "two", new Launcher(), Dispatch);
            Assert.Equal(1, service.Subscribers);
            service.Configurations.Single(config => config.Id == "two").Name = "迟到通知不应改标题";
            actions.Dequeue()();
            Assert.Equal("剧集库", vm.Title);
            vm.Cancel();
            Assert.Equal(0, service.Subscribers);
        });
        Test("本地库VM：属性通知中重入另一库，旧同步不继续覆盖", () =>
        {
            var service = Service();
            using var vm = new LocalLibraryViewModel();
            var reentered = false;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(vm.Title) || reentered) return;
                reentered = true;
                vm.Attach(service, "two", new Launcher(), action => { action(); return true; });
            };
            vm.Attach(service, "one", new Launcher(), action => { action(); return true; });
            Assert.Equal("剧集库", vm.Title);
            Assert.Equal("two-entry", vm.Rows.Single().Id);
            Assert.Equal(service.Configurations[1].SourceDirectory, vm.SourceDirectory);
            Assert.Equal(1, service.Subscribers);
        });
        Test("本地库VM：不存在的目录不给会创建目录的launcher", () =>
        {
            var service = Service();
            using var vm = new LocalLibraryViewModel();
            var launcher = new Launcher();
            vm.Attach(service, "one", launcher, action => { action(); return true; });
            vm.OpenSourceDirectory();
            vm.OpenLocation(vm.Rows.Single());
            Assert.Equal(0, launcher.Opens);
            Assert.True(vm.NoticeOpen);
        });
        Test("本地库VM：加载状态通知中取消，不启动旧刷新", () =>
        {
            var service = Service();
            var refreshes = 0;
            service.Refresh = _ => { refreshes++; return Task.CompletedTask; };
            using var vm = new LocalLibraryViewModel();
            vm.Attach(service, "one", new Launcher(), action => { action(); return true; });
            vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.Busy) && vm.Busy) vm.Cancel(); };
            var task = vm.ReloadAsync();
            Assert.True(task.IsCompletedSuccessfully);
            Assert.Equal(0, refreshes);
            Assert.False(vm.Busy);
        });
        return Run();
    }

    private static LocalLibraryCoordinator Service()
    {
        var service = new LocalLibraryCoordinator();
        var missing = Path.Combine(Path.GetTempPath(), "momoka-missing-local-library-" + Guid.NewGuid().ToString("N"));
        service.Configurations.Add(new LocalMediaLibrarySettings { Id = "one", Name = "电影库", SourceDirectory = missing });
        service.Configurations.Add(new LocalMediaLibrarySettings { Id = "two", Name = "剧集库", SourceDirectory = missing + "-two" });
        service.Entries.Add(Entry("one", "one-entry", true, missing));
        service.Entries.Add(Entry("one", "failed", false, missing));
        service.Entries.Add(Entry("two", "two-entry", true, missing));
        return service;
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("当前测试不在Momoka工作树内");
    }

    // 使用Core正式JSON模型，避免fixture猜它是record构造器还是可变对象。
    private static LocalMediaLibraryEntry Entry(string library, string id, bool inLibrary, string directory)
        => JsonSerializer.Deserialize<LocalMediaLibraryEntry>(JsonSerializer.Serialize(new
        {
            Id = id,
            LibraryId = library,
            TargetPath = Path.Combine(directory, "Movie.mkv"),
            Source = new { RootPath = directory, Path = Path.Combine(directory, "Movie.mkv") },
            Metadata = new { Title = "Movie", Year = 2026, MediaType = 0, MediaSource = "tmdb", MediaId = "1" },
            Mode = 0,
            Outputs = inLibrary ? new[] { new { Path = Path.Combine(directory, "Movie.mkv"), Kind = 0, Success = true, Message = "" } } : [],
            AddedAt = DateTimeOffset.UtcNow,
            IsInLibrary = inLibrary,
            Complete = true
        }))!;

    private sealed class Launcher : ISystemLauncher
    {
        internal int Opens { get; private set; }
        public void OpenFolder(string path) => Opens++;
        public void OpenUrl(string url) => throw new InvalidOperationException("本地库不应打开URL");
    }

    // Keep the production async handler's continuation on the test's owner thread, as on WinUI's dispatcher.
    private sealed class CommandContext : SynchronizationContext, IDisposable
    {
        private readonly SynchronizationContext? _previous = Current;
        private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
        public CommandContext() => SetSynchronizationContext(this);
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Enqueue((callback, state));
        public void WaitForNext() => Assert.True(SpinWait.SpinUntil(() => !_queue.IsEmpty, TimeSpan.FromSeconds(3)),
            "真实刷新续体没有到达受控UI调度边界");
        public void RunNext()
        {
            WaitForNext();
            Assert.True(_queue.TryDequeue(out var work));
            work.Callback(work.State);
        }
        public void Dispose() => SetSynchronizationContext(_previous);
    }
}
