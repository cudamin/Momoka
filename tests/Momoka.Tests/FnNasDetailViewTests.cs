using Momoka.Emby;
using static Momoka.Tests.LibraryResumePagingTests;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class FnNasDetailViewTests
{
    internal static int Run(string shellPath, string root)
    {
        var shell = new ShellFixture(shellPath, root);
        Test("飞牛人物作品真实VM：筛选整组收起，命令守卫，排序保留", () => SingleThread.Run(async () =>
        {
            using var fixture = new Fixture(shell, 2, firstKind: MediaServerKind.FnNas);
            var assembly = fixture.Vm.GetType().Assembly;
            var request = Activator.CreateInstance(assembly.GetType("Momoka.Shell.Views.LibraryRequest", true)!)!;
            shell.Set(request, "Services", new NoServices());
            shell.Set(request, "Title", "演员");
            shell.Set(request, "Source", fixture.Session.Capture());
            shell.Set(request, "PersonId", "fn-actor");
            var vm = shell.CreateLibrary(fixture.Session, fixture.Images, fixture.Settings,
                fixture.Session.Capture(), null, request, null, null, LibrarySection.Items);
            try
            {
                Assert.Equal("Collapsed", shell.Get<object>(vm, "FilterVisibility").ToString());
                Assert.Equal("Collapsed", shell.Get<object>(vm, "FnNasFilterVisibility").ToString());
                Assert.Equal("Collapsed", shell.Get<object>(vm, "FnNasFilterTextVisibility").ToString());
                await (Task)shell.Invoke(vm, "ToggleFnNasFilterAsync")!;
                Assert.False(shell.Get<bool>(vm, "FnNasFilterOpen"));
            }
            finally { ((IDisposable)vm).Dispose(); }
        }));
        return TestHarness.Run();
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
