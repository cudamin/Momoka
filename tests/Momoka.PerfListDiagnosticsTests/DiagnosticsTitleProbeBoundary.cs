using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Services;
using Momoka.Shell.Platform;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell
{

    internal sealed partial class ShellNavigationProbe
    {
        private readonly FrameworkElement _root = new();
        private readonly FixtureOptions _options = new(new Momoka.Infrastructure.AppPaths(Path.Combine(AppContext.BaseDirectory, "title-probe-artifacts")));
        internal bool FailCapture { get; init; }
        internal bool FailTitleBinding { get; init; }
        internal int Captures { get; private set; }
        internal int Mounted => _root.Children.Count;
        internal Task TitleForFixture() => DiagnosticsInitialTitleAsync();
        private static TitleFixture CreateFixture() => new();
        private static void Require(bool value, string message) => Momoka.Tests.Assert.True(value, message);
        private T Get<T>(object page, string name)
        {
            var view = (Views.DiagnosticsPage)page;
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = FailTitleBinding ? "wrong binding" : view.ViewModel.NowPlaying ?? "" });
            return (T)(object)panel;
        }
        private static IEnumerable<FrameworkElement> Descendants(FrameworkElement element) => element.Children;
        private static Task LayoutAsync(FrameworkElement _) => Task.CompletedTask;
        private static Task SettleAsync() => Task.CompletedTask;
        private static void ResizeProbeClient(int width, int height) { }
        private static void ResizeInspect(int width, int height) { }
        private Task SaveProbeScreenAsync(string name)
        {
            Captures++;
            return FailCapture ? Task.FromException(new IOException("anonymous title capture failure")) : Task.CompletedTask;
        }
        private sealed record FixtureOptions(Momoka.Infrastructure.AppPaths Paths);
        private sealed class TitleFixture : IDisposable
        {
            private readonly Momoka.Tests.MemoryEnvironment _environment = new();
            internal Momoka.Emby.EmbySession Session => _environment.Session;
            internal Momoka.Configuration.AppSettings Settings => _environment.Settings;
            internal ISettingsService SettingsService { get; }
            internal IServerCapabilities Capabilities { get; } = new TitleCapabilities();
            internal TitleActions Actions { get; } = new();
            internal IUiDispatcher Dispatcher { get; } = new TitleDispatcher();
            internal ISystemLauncher Launcher { get; } = new TitleLauncher();
            internal TitleFixture() => SettingsService = new SettingsService(null!, Settings);
            internal Task SelectAsync() => Task.CompletedTask;
            public void Dispose() => _environment.Dispose();
        }
        private sealed class TitleActions : Views.IShellActions
        {
            internal int Plays => 0;
            public void Notify(string message, InfoBarSeverity severity) => throw new InvalidOperationException("Title probe cannot notify");
        }
        private sealed class TitleCapabilities : IServerCapabilities
        {
            public Version? ServerVersion => null;
            public bool? IsAdministrator => false;
            public Task ProbeAsync(CancellationToken token = default) => throw new InvalidOperationException("Title probe cannot probe server");
        }
        private sealed class TitleDispatcher : IUiDispatcher
        {
            public bool Post(Action work) { work(); return true; }
            public void Run(Action work) => work();
        }
        private sealed class TitleLauncher : ISystemLauncher
        {
            public void OpenFolder(string path) => throw new InvalidOperationException("Title probe cannot launch");
            public void OpenUrl(string url) => throw new InvalidOperationException("Title probe cannot launch");
        }
        private sealed class DiagnosticsProbeClipboard : IClipboard
        {
            public void SetText(string text) => throw new InvalidOperationException("Title probe cannot copy");
        }
    }
}

namespace Momoka.Shell.Views
{
    public sealed class DiagnosticsPage : FrameworkElement
    {
        internal DiagnosticsViewModel ViewModel { get; } = new();
        internal void Release() => ViewModel.Cancel();
    }
}
