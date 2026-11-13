using System.Text.Json;
using Momoka.Configuration;
using Momoka.LocalMedia;
using Momoka.Shell.Platform;
using Momoka.Shell.Services;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    public static int Main()
    {
        LocalLibraryHomeTests.Register();
        LocalLibraryArtworkTests.Register();
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
}
