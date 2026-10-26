using System.Collections.Concurrent;
using System.ComponentModel;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Tests;

// Only XAML/dispatcher/unused hosted-page endpoints are substitutes; activity logic is extracted.
namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        private readonly ConcurrentQueue<Action> _pending = new();
        public int Count => _pending.Count;
        public bool TryEnqueue(Action action) { _pending.Enqueue(action); return true; }
        public void Drain() { while (_pending.TryDequeue(out var action)) action(); }
    }
}

namespace Microsoft.UI.Xaml
{
    public class FrameworkElement
    {
        public object? Tag { get; set; }
        public object? XamlRoot { get; set; }
        public event EventHandler? Loaded;
        public event EventHandler? Unloaded;
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
        public void RaiseLoaded() => Loaded?.Invoke(this, EventArgs.Empty);
        public void RaiseUnloaded() => Unloaded?.Invoke(this, EventArgs.Empty);
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
        public void Enter(object request) => OnNavigatedTo(new(request));
        public void Leave() => OnNavigatedFrom(new());
    }
    public sealed class Frame { public object? Content { get; set; } }
    public sealed class CollectionViewSource { public object? Source { get; set; } }
}

namespace Momoka.Shell.ViewModels
{
    public sealed class SettingsCategoryGroup { }
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
        public void SyncSaveState() => Runtime.Invoke(Inner, "SyncSaveState");
        public void RefreshHomeRows() => Runtime.Invoke(Inner, "RefreshHomeRows");
        public void Cancel() => Runtime.Invoke(Inner, "Cancel");
        public void Activate() => Runtime.Invoke(Inner, "Activate");
        public void UseConfirm(object ignored) { }
        public void UseFilePickers(object save, object open) { }
    }
}

namespace Momoka.Shell.Views
{
    internal static class HomeMotion
    {
        internal static event Action? PreferenceChanged;
        internal static void Stop(Microsoft.UI.Xaml.FrameworkElement element) { }
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
        private void InitializeComponent()
        {
            // XAML boundary only: default values do not create an activity visit; shared hook writes by reflection.
            _request = null;
            _activityToken = default;
        }
        private void ObserveCategoryGroups() { }
        private void ApplySettingsMotionPreference() { }
        private void QueueSettingsEntrance() { }
        private void StopSettingsEntrance() { }
        private void DetachSettingsContentMotion() { }
        private void StopSettingsContentMotion() { }
        private void ShowHosted() { }
        private Task RefreshServerAdministrationAsync() => throw new InvalidOperationException("本隔离夹具不订阅来源管理");
        public object? NavigationRequest => _request;
    }
}

namespace Momoka.Shell
{
    internal sealed partial class ShellNavigationProbe
    {
        internal static Task UntilForFixture(Func<bool> ready) => UntilAsync(ready);
        private static Task LayoutAsync(Microsoft.UI.Xaml.FrameworkElement element) => Task.CompletedTask;
        private static void Field(object target, string name, object? value) =>
            target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(target, value);
        private static T Get<T>(object target, string name) =>
            (T)target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(target)!;
    }
}
