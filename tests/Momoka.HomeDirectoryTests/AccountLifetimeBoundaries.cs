using System.Collections.Concurrent;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Services;

namespace Microsoft.Extensions.DependencyInjection
{
    internal static class BoundaryServiceExtensions
    {
        internal static T? GetService<T>(this IServiceProvider services) => (T?)services.GetService(typeof(T));
    }
}

namespace Momoka.Shell.Services
{
    internal sealed class SubtitleTranslationController
    {
        internal bool Hold { get; set; }
        internal int Stops { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal event Action<string> Noticed { add { } remove { } }
        internal Task StopAsync()
        {
            Stops++;
            Entered.TrySetResult();
            return Hold ? Release.Task : Task.CompletedTask;
        }
    }
}

namespace Momoka.Shell.Views
{
    internal sealed class ToggleBoundary { internal bool IsOn { get; set; } }
    internal sealed class MenuBoundary { internal void Hide() { } }
    internal sealed class CloseBoundary { internal void Close() { } }
    internal sealed class AccountModeBoundary : IDisposable
    {
        internal bool Busy => false;
        internal bool AggregateMode => false;
        internal bool NoticeOpen => false;
        internal string? NoticeMessage => null;
        internal string? NoticeTitle => null;
        internal InfoBarSeverity NoticeSeverity => InfoBarSeverity.Informational;
        internal int Starts { get; private set; }
        internal void Attach(object owner, ISettingsService settings, EmbySession session) { }
        internal Task SetAggregateModeAsync(bool value) { Starts++; return Task.CompletedTask; }
        internal void Cancel() { }
        public void Dispose() { }
    }
    internal sealed class PlayerBoundary
    {
        internal PlayerVmBoundary ViewModel { get; } = new();
        internal int Closed { get; private set; }
        internal void Shutdown() => Closed++;
    }
    internal sealed class PlayerVmBoundary
    {
        internal bool Hold { get; set; }
        internal int Stops { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal event Action PlaybackStarted { add { } remove { } }
        internal event Action PlayerHidden { add { } remove { } }
        internal event Action MediaInfoRequested { add { } remove { } }
        internal event Action SettingsRequested { add { } remove { } }
        internal event Action DanmakuRequested { add { } remove { } }
        internal event Action<string, InfoBarSeverity> Noticed { add { } remove { } }
        internal event Action RefreshRequested { add { } remove { } }
        internal object? PreparePresentation { get; set; }
        internal Task ShutdownAsync()
        {
            Stops++;
            Entered.TrySetResult();
            return Hold ? Release.Task : Task.CompletedTask;
        }
    }
    internal sealed class DispatchBoundary
    {
        private readonly SynchronizationContext? _context = SynchronizationContext.Current;
        private readonly ConcurrentQueue<Action> _held = new();
        internal bool Hold { get; set; }
        internal bool TryEnqueue(Action action)
        {
            if (Hold) _held.Enqueue(action);
            // This test queue has no native UI thread or JoinableTaskFactory; Post preserves dispatcher ordering.
#pragma warning disable VSTHRD001
            else if (_context is not null) _context.Post(_ => action(), null);
#pragma warning restore VSTHRD001
            else action();
            return true;
        }
        internal void Drain() { while (_held.TryDequeue(out var action)) action(); }
    }
    internal sealed partial class ShellPage
    {
        internal Momoka.Shell.ViewModels.SignInViewModel Login => SignIn.ViewModel;
        internal bool LoginVisible => SignInVisible;
        internal bool ContentVisible => ContentHost.Visibility == Microsoft.UI.Xaml.Visibility.Visible;
        internal PlayerVmBoundary PlayerStop => Player.ViewModel;
        internal DispatchBoundary Notifications => DispatcherQueue;
        internal int SourceOperations => _sourceOperations;
        internal bool PendingSourcesChange => _pendingSourcesChange;
        internal int AggregateStarts => _accountMode.Starts;
        internal void RequestSignIn() => ShowSignIn();
        internal Task SwitchForTest(ServerProfile server, AccountProfile account) => SwitchAsync(server, account);
        internal Task AggregateForTest() => SetAccountAggregateModeAsync(true);
        internal void ReplaySignedIn() => OnSignedIn(this, EventArgs.Empty);
        internal void ReplaySignedOut() => OnSignedOut("anonymous-fixture");
        internal void ReplaySourcesChanged() => OnSourcesChanged();
        internal void BeginSourceOperation() { _sourceOperations++; _pendingSourcesChange = true; }
        internal void FinishSourceOperation() => FinishChangingSources();
        internal void DisposeLogin() => SignIn.ViewModel.Dispose();
        private void StopMoviePilotMonitoring() { }
        private void HideSettings() { }
        private void OnSubtitleTranslationNotice(string message) { }
        private void OnHeadlessPlaybackStarted() { }
        private void OnHeadlessPlaybackHidden() { }
        private void OnHeadlessMediaInfoRequested() { }
        private void OnPlayerSettingsRequested() { }
        private void OnDanmakuRequested() { }
        private void RefreshActive() { }
        private void Notify(string message) => Notify(message, InfoBarSeverity.Informational);
    }
}
