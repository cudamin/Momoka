using Momoka.Diagnostics;
using Momoka.Services;

namespace Microsoft.Extensions.DependencyInjection
{
    // The exact App method resolves only the narrow settings service; no real DI container is disposed.
    internal static class FixtureResolution
    {
        internal static T? GetService<T>(this Momoka.Shell.FlushContainer container) where T : class => container.Settings as T;
    }
}

namespace Momoka.Shell
{
    internal sealed class FlushContainer(ISettingsService settings) : IDisposable
    {
        internal ISettingsService Settings { get; } = settings;
        internal bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
    internal sealed class InertActivation : IDisposable
    {
        internal bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
    internal sealed class InertShutdown
    {
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task ShutdownAsync() => Completion.Task;
        internal void Shutdown() { }
    }
    internal sealed partial class FlushAppFixture
    {
        private const string Category = "app";
        private InertActivation? _activation = new();
        private readonly object _options = new();
        private InertShutdown? _shell;
        private FlushContainer? _container;
        internal bool Exited { get; private set; }
        internal bool Remembered { get; private set; }
        internal InertShutdown ShutdownBoundary { get; }
        internal FlushContainer ContainerBoundary { get; }
        internal FlushAppFixture(ISettingsService settings)
        {
            _shell = ShutdownBoundary = new();
            _container = ContainerBoundary = new(settings);
        }
        internal Task Prepare() => PrepareWindowCloseAsync();
        internal void Close() => OnWindowClosed();
        private void RememberWindow() => Remembered = true;
        private void Exit() => Exited = true;
    }
}

namespace Momoka.Shell.Views
{
    internal sealed class InertSettingsPage(Action release)
    {
        internal void ReleaseHosted() => release();
    }
    internal sealed class InertSettingsWindow
    {
        internal InertAppWindow AppWindow { get; } = new();
    }
    internal sealed class InertAppWindow
    {
        internal bool Hidden { get; private set; }
        internal void Hide() => Hidden = true;
    }
    internal sealed partial class FlushWindowFixture
    {
        private bool _open = true;
        private readonly InertSettingsWindow _window = new();
        private InertSettingsPage? Page { get; }
        internal event Action? Hidden;
        internal bool IsHidden => _window.AppWindow.Hidden;
        internal FlushWindowFixture(Action release) => Page = new(release);
    }
    internal static class ShellSelfCheck
    {
        internal static void ReportCrash(object options, Exception error, string message) => throw new InvalidOperationException("不允许自检");
    }
}
