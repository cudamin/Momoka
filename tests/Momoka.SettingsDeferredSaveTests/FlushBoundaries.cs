using Momoka.Diagnostics;
using Momoka.Services;

namespace Microsoft.Extensions.DependencyInjection
{
    // Optional local monitoring is absent; the fixture resolves only its narrow settings owner.
    internal static class FixtureResolution
    {
        internal static T? GetService<T>(this Momoka.Shell.FlushContainer container) where T : class => container.Settings as T;
    }
}

namespace Momoka.Shell.Services
{
    // The real App close method asks for this optional owner. No local monitor is registered here,
    // and an accidental request to stop a real filesystem monitor must fail rather than run one.
    internal sealed class LocalLibraryCoordinator
    {
        internal Task StartAsync() => throw new InvalidOperationException("本夹具禁止本地目录监控");
        internal Task StopAsync() => throw new InvalidOperationException("本夹具禁止本地目录监控");
    }
}

namespace Momoka.Shell
{
    // 系统主题监听属于真实外壳；主窗口关闭时生产代码会停掉它（App.xaml.cs 的 OnWindowClosed）。
    // 这个夹具没有真实监听，只记录调用，供关窗路径核对「关了就停」而不是留下后台观察。
    internal static class ThemeHost
    {
        internal static int StopSystemWatchCalls { get; private set; }
        internal static void StopSystemWatch() => StopSystemWatchCalls++;
    }
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

        // 设置窗口拆成三个分部之后，Hide() 收场时要先停掉系统外观观察（SettingsWindow.Appearance.cs）。
        // 这个夹具不订阅系统外观事件，停不停都一样，所以什么也不做。
        private void PauseSystemAppearance() { }
    }
    internal static class ShellSelfCheck
    {
        internal static void ReportCrash(object options, Exception error, string message) => throw new InvalidOperationException("不允许自检");
    }
}
