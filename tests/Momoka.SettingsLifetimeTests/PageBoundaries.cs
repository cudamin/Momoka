using System.ComponentModel;
using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Tests;

// These substitutes have no WinRT/HWND/XAML implementation. VM behavior is never copied here.
namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        private readonly Queue<Action> _pending = new();
        public int Enqueued { get; private set; }
        public int Count => _pending.Count;
        public bool TryEnqueue(Action action) { Enqueued++; _pending.Enqueue(action); return true; }
        public void Drain() { while (_pending.TryDequeue(out var action)) action(); }
    }
}

namespace Microsoft.UI.Xaml
{
    public class DependencyObject { }
    public class FrameworkElement : DependencyObject
    {
        public object? Tag { get; set; }
        public object? XamlRoot { get; set; }
        public event EventHandler? Loaded;
        public event EventHandler? Unloaded;
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
        public void RaiseLoaded() => Loaded?.Invoke(this, EventArgs.Empty);
        public void RaiseUnloaded() => Unloaded?.Invoke(this, EventArgs.Empty);
    }
    public sealed class Window
    {
        public Microsoft.UI.Windowing.AppWindow AppWindow { get; } = new();
        public int Activations { get; private set; }
        public void Activate() => Activations++;
        public void Close() => AppWindow.RaiseClosing();
    }
}

namespace Microsoft.UI.Xaml.Navigation
{
    public enum NavigationCacheMode { Required }
    public sealed class NavigationEventArgs(object? parameter = null)
    {
        public object? Parameter { get; } = parameter;
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public class Page : Microsoft.UI.Xaml.FrameworkElement
    {
        public object? Content { get; set; }
        public Microsoft.UI.Xaml.Navigation.NavigationCacheMode NavigationCacheMode { get; set; }
        protected virtual void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs args) { }
        protected virtual void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs args) { }
        public void Enter(object? request) => OnNavigatedTo(new(request));
        public void Leave() => OnNavigatedFrom(new());
    }
    public sealed class Frame
    {
        public object? Content { get; set; }
        public List<object> BackStack { get; } = [];
        public int Navigations { get; private set; }
        public void Navigate(Type page, object request)
        {
            Navigations++;
            if (page == typeof(Momoka.Shell.Views.SettingsPage) && Content is Momoka.Shell.Views.SettingsPage cached)
            {
                cached.Enter(request);
                return;
            }
            if (Content is Page old) old.Leave();
            var created = (Page)Activator.CreateInstance(page)!;
            Content = created;
            created.Enter(request);
        }
    }
    public sealed class CollectionViewSource
    {
        private object? _source;
        public int Writes { get; private set; }
        public object? Source { get => _source; set { _source = value; Writes++; } }
    }
}

namespace Microsoft.UI.Windowing
{
    public sealed class AppWindowClosingEventArgs : EventArgs { public bool Cancel { get; set; } }
    public sealed class AppWindow
    {
        public int Hidden { get; private set; }
        public int Shown { get; private set; }
        public event Action<AppWindow, AppWindowClosingEventArgs>? Closing;
        public void Hide() => Hidden++;
        public void Show() => Shown++;
        public AppWindowClosingEventArgs RaiseClosing()
        {
            var args = new AppWindowClosingEventArgs();
            Closing?.Invoke(this, args);
            return args;
        }
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    internal static class FixtureServicesExtensions
    {
        public static T GetRequiredService<T>(this IServiceProvider services) where T : class =>
            (T)(services.GetService(typeof(T)) ?? throw new InvalidOperationException("未授权的服务边界 " + typeof(T).Name));
    }
}

namespace Momoka.Shell.Interop
{
    internal static class Native
    {
        public static void DestroyIcon(IntPtr icon) => throw new InvalidOperationException("禁止原生图标调用");
    }
}

namespace Momoka.Shell.Platform
{
    internal interface ISystemLauncher { }
    internal interface IStartupRegistration { }
}

namespace Momoka.Shell
{
    internal static class ThemeHost
    {
        public static event Action<Momoka.Theming.UiTheme>? Changed;
        public static void Raise() => Changed?.Invoke(default!);
    }
}

namespace Momoka.Shell.ViewModels
{
    public sealed class SettingsCategoryGroup { }
    public sealed class SettingsCategoryEntry(object inner)
    {
        public string Label => ShellRuntime.Current.Get<string>(inner, "Label");
        public string Key => ShellRuntime.Current.Get<string>(inner, "Key");
    }

    public sealed class SettingsViewModel
    {
        internal object Inner { get; } = ShellRuntime.Current.TakePageViewModel();
        private ShellRuntime Runtime => ShellRuntime.Current;
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => ((INotifyPropertyChanged)Inner).PropertyChanged += value;
            remove => ((INotifyPropertyChanged)Inner).PropertyChanged -= value;
        }
        public object CategoryGroups => Runtime.Get<object>(Inner, "CategoryGroups");
        public bool IsReady => Runtime.Get<bool>(Inner, "IsReady");
        public string SelectedCategory { get => Runtime.Get<string>(Inner, "SelectedCategory"); set => Runtime.Set(Inner, "SelectedCategory", value); }
        public string? SelectedSourceKey => Runtime.Get<string?>(Inner, "SelectedSourceKey");
        public bool ShowsHosted => Runtime.Get<bool>(Inner, "ShowsHosted");
        public SettingsCategoryEntry? SelectedEntry => Runtime.Get<object?>(Inner, "SelectedEntry") is { } entry ? new(entry) : null;
        public string? ConsoleCategory => Runtime.Get<string?>(Inner, "ConsoleCategory");
        public static string FirstCardCategory => "主页";
        public const string NotificationsCategory = "通知";
        public const string DashboardCategory = "网页控制台";
        public const string FnNasPasswordCategory = "修改密码";
        public const string FnNasPreferencesCategory = "播放偏好";
        public const string FnNasLibrariesCategory = "媒体库";
        public const string FnNasUsersCategory = "用户";
        public const string FnNasSettingsCategory = "设置";
        public const string FnNasTasksCategory = "任务计划";
        public bool CanOpenServerCategory(string category) => (bool)Runtime.Invoke(Inner, "CanOpenServerCategory", category)!;
        public bool OpensFnNasPage(string category) => (bool)Runtime.Invoke(Inner, "OpensFnNasPage", category)!;
        public string? ServerRoute(string category) => (string?)Runtime.Invoke(Inner, "ServerRoute", category);
        public SettingsCategoryEntry? EntryFor(string category) => Runtime.Invoke(Inner, "EntryFor", category) is { } entry ? new(entry) : null;
        public Task RefreshServerAdministrationAsync() => (Task)Runtime.Invoke(Inner, "RefreshServerAdministrationAsync")!;
        public Task RefreshAudioDevicesAsync() => (Task)Runtime.Invoke(Inner, "RefreshAudioDevicesAsync")!;
        public void SyncSaveState() => Runtime.Invoke(Inner, "SyncSaveState");
        public void RefreshHomeRows() => Runtime.Invoke(Inner, "RefreshHomeRows");
        public void Cancel() => Runtime.Invoke(Inner, "Cancel");
        public void Activate() => Runtime.Invoke(Inner, "Activate");
        public void UseConfirm(object ignored) { }
        public void UseFilePickers(object save, object open) { }
        internal void Attach(ISettingsService settings, ShaderStaging shaders, FontLibrary fonts, AppPaths paths,
            Platform.ISystemLauncher launcher, Platform.IStartupRegistration startup, AudioDeviceCatalogue devices,
            Func<Task> subtitle, Func<string, Task> directory, Func<Task> screenshot, MoviePilotService moviePilot,
            MoviePilotCredentials credentials, Momoka.Updates.UpdateClient updates, IServerCapabilities capabilities, EmbyImageStore images) =>
            throw new InvalidOperationException("本夹具只运行已构建缓存页，不允许完整 UI 重建");
        public Task ReloadAsync() => throw new InvalidOperationException("禁止整页/native Reload");
    }

    public sealed class FnNasPreferencesViewModel
    {
        internal object Inner { get; } = ShellRuntime.Current.New("FnNasPreferencesViewModel");
        public bool IsCurrentSession => ShellRuntime.Current.Get<bool>(Inner, "IsCurrentSession");
        public string? SourceKey => ShellRuntime.Current.Get<string?>(Inner, "SourceKey");
        public Task? LastRead { get; private set; }
        public void Attach(EmbySession session, string? sourceKey) => ShellRuntime.Current.Invoke(Inner, "Attach", session, sourceKey);
        public Task ReloadAsync() => LastRead = (Task)ShellRuntime.Current.Invoke(Inner, "ReloadAsync")!;
        public void Cancel() => ShellRuntime.Current.Invoke(Inner, "Cancel");
    }
}

namespace Momoka.Shell.Views
{
    internal static class HomeMotion
    {
        internal static event Action? PreferenceChanged;
        internal static void Stop(Microsoft.UI.Xaml.FrameworkElement element) { }
        internal static void Reveal(Microsoft.UI.Xaml.FrameworkElement element) { }
        internal static void Raise() => PreferenceChanged?.Invoke();
    }
    internal static class ConfirmDialog { internal static object For(object page) => new(); }
    internal static class SettingsFile
    {
        internal static object SaveFor(object page) => new();
        internal static object OpenFor(object page) => new();
    }

    public sealed partial class SettingsPage
    {
        internal Microsoft.UI.Xaml.Controls.Frame HostedFrame { get; } = new();
        internal Microsoft.UI.Xaml.Controls.CollectionViewSource CategoryGroupsSource { get; } = new();
        private void InitializeComponent() { }
        private void ObserveCategoryGroups() { }
        private void ApplySettingsMotionPreference() { }
        private void QueueSettingsEntrance() { }
        private void StopSettingsEntrance() { }
        private void DetachSettingsContentMotion() { }
        private void StopSettingsContentMotion() { }
        internal Task RefreshForFixture() => RefreshServerAdministrationAsync();
        internal void PrepareCached(SettingsRequest request) => _request = request;
    }

    internal sealed partial class SettingsWindow
    {
        internal SettingsWindow(SettingsPage page)
        {
            _frame = new() { Content = page };
            _window = new();
            _repaint = _ => { };
            _blankIcon = IntPtr.Zero;
            _window.AppWindow.Closing += OnClosing;
        }
        internal Microsoft.UI.Windowing.AppWindow BoundaryWindow => _window.AppWindow;
    }

    public sealed partial class FnNasPreferencesPage
    {
        private Microsoft.UI.Xaml.FrameworkElement PageLayout { get; } = new();
        private void InitializeComponent() { }
    }

    // Untouched sibling UI pages cannot execute here. The selected native preferences page above is real.
    internal abstract class RejectedPage : Microsoft.UI.Xaml.Controls.Page, IShellContent
    {
        public object? NavigationRequest => null;
        public void Release() { }
        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs args) =>
            throw new InvalidOperationException("夹具未授权该内嵌 UI 页");
    }
    internal sealed class ServersPage : RejectedPage { }
    internal sealed class DiagnosticsPage : RejectedPage { }
    internal sealed class NotificationsPage : RejectedPage { }
    internal sealed class FnNasPasswordPage : RejectedPage { }
    internal sealed class FnNasLibrariesPage : RejectedPage { }
    internal sealed class FnNasUsersPage : RejectedPage { }
    internal sealed class FnNasSettingsPage : RejectedPage { }
    internal sealed class FnNasTasksPage : RejectedPage { }
    internal sealed class DashboardPage : RejectedPage
    {
        public string? SourceKey => null;
        public bool TryShowRoute(string route) => false;
    }
    internal sealed record ServerRequest(IServiceProvider Services);
    internal sealed record DiagnosticsRequest(IServiceProvider Services);
    internal sealed record NotificationsRequest(IServiceProvider Services, string? SourceKey);
    internal sealed record FnNasPasswordRequest(IServiceProvider Services, string? SourceKey);
    internal sealed record FnNasLibrariesRequest(IServiceProvider Services, string? SourceKey);
    internal sealed record FnNasUsersRequest(IServiceProvider Services, string? SourceKey);
    internal sealed record FnNasSettingsRequest(IServiceProvider Services, string? SourceKey);
    internal sealed record FnNasTasksRequest(IServiceProvider Services, string? SourceKey);
    internal sealed record DashboardRequest(IServiceProvider Services, string Route, bool Immersive, string? SourceKey);
}
