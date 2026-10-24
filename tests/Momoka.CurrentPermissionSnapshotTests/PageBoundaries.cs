using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Tests;

namespace Momoka.Shell.Views;

internal interface ISessionScopedPage
{
    bool IsCurrentSession { get; }
    string? SourceKey { get; }
}

internal sealed record PermissionRequest(IServiceProvider Services);
internal sealed class PermissionServices(EmbySession session) : IServiceProvider
{
    public object? GetService(Type type) => type == typeof(EmbySession) ? session : null;
}
internal sealed class PermissionFrame
{
    internal object? Content { get; set; }
}
internal sealed class PermissionGroups
{
    internal object? Source { get; set; }
}

/// <summary>
/// Production subscription, selection callback, refresh and queued callback methods are generated
/// verbatim into this type. Only non-executed framework owners/navigation values are substitutes.
/// </summary>
internal sealed partial class PermissionPage : IDisposable
{
    private const string Category = "permission-fixture";
    private readonly LoadGeneration _activity = new();
    private CancellationToken _activityToken;
    private EmbySession? _session;
    internal SettingsVmProxy ViewModel { get; }
    internal PermissionQueue DispatcherQueue { get; } = new();
    private PermissionFrame HostedFrame { get; } = new();
    private PermissionGroups CategoryGroupsSource { get; } = new();
    internal int HostedRefreshes { get; private set; }
    internal int Releases { get; private set; }

    internal PermissionPage(SettingsVmProxy vm, EmbySession session)
    {
        ViewModel = vm;
        ConnectSelection();
        Open(session);
    }
    internal void Open(EmbySession session)
    {
        StopSourceMonitoring();
        _activityToken = _activity.Begin();
        ViewModel.Activate();
        SubscribeProduction(session);
    }
    internal Task Refresh() => RefreshServerAdministrationAsync();
    internal void Hide()
    {
        _activity.Cancel();
        ViewModel.Cancel();
        StopSourceMonitoring();
    }
    private void ObserveCategoryGroups() { }
    private void StopSettingsContentMotion() { }
    private void QueueSettingsEntrance() { }
    private void ShowHosted()
    {
        HostedRefreshes++;
        if (!ViewModel.ShowsHosted) Releases++;
    }
    public void Dispose() { Hide(); _activity.Dispose(); }
}
