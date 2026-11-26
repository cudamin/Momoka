using Microsoft.Extensions.DependencyInjection;
using Momoka.Scrobbling;

namespace Momoka.Shell.Views;

public sealed partial class SettingsPage
{
    private ScrobblingService? _scrobblingService;
    private Action? _scrobblingChanged;
    private CancellationTokenSource? _scrobblingCheck;

    private void AttachScrobblingForVisit(ScrobblingService service, Func<Task> loginDouban, Func<Task> logoutDouban)
    {
        ViewModel.UseScrobbling(service, loginDouban, logoutDouban, VerifyScrobblingAsync);
        WatchScrobbling();
        _ = VerifyScrobblingAsync();
    }

    private void WatchScrobbling()
    {
        StopScrobblingMonitoring();
        if (_request is not { } request) return;
        var activity = _activityToken;
        var service = request.Services.GetRequiredService<ScrobblingService>();
        _scrobblingService = service;
        _scrobblingChanged = () => DispatcherQueue.TryEnqueue(() =>
        {
            if (_activity.IsCurrent(activity) && ReferenceEquals(_scrobblingService, service)) ViewModel.RefreshScrobblingRows();
        });
        service.Changed += _scrobblingChanged;
    }

    private void StopScrobblingMonitoring()
    {
        _scrobblingCheck?.Cancel();
        _scrobblingCheck?.Dispose();
        _scrobblingCheck = null;
        if (_scrobblingService is { } service && _scrobblingChanged is { } changed) service.Changed -= changed;
        _scrobblingService = null;
        _scrobblingChanged = null;
    }

    private async Task VerifyScrobblingAsync()
    {
        if (_request is not { } request || !_activity.IsCurrent(_activityToken)) return;
        _scrobblingCheck?.Cancel();
        _scrobblingCheck?.Dispose();
        var check = CancellationTokenSource.CreateLinkedTokenSource(_activityToken);
        _scrobblingCheck = check;
        try { await request.Services.GetRequiredService<ScrobblingService>().CheckConnectionsAsync(check.Token); }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_scrobblingCheck, check)) _scrobblingCheck = null;
            check.Dispose();
        }
    }

    private async Task LoginDoubanAsync()
    {
        if (_request is not { } request) return;
        var activity = _activityToken;
        var service = request.Services.GetRequiredService<ScrobblingService>();
        service.SetDoubanSessionChanging(true);
        try { await request.Services.GetRequiredService<Media.DoubanSession>().LoginAsync(this, activity); }
        finally { service.SetDoubanSessionChanging(false); }
        if (_activity.IsCurrent(activity) && ReferenceEquals(_request, request)) await VerifyScrobblingAsync();
    }

    private async Task LogoutDoubanAsync()
    {
        if (_request is not { } request) return;
        var service = request.Services.GetRequiredService<ScrobblingService>();
        var activity = _activityToken;
        service.SetDoubanSessionChanging(true);
        try { await request.Services.GetRequiredService<Media.DoubanSession>().LogoutAsync(activity); }
        finally { service.SetDoubanSessionChanging(false); }
        if (_activity.IsCurrent(activity) && ReferenceEquals(_request, request))
        {
            service.SetStatus("豆瓣已退出登录");
            await VerifyScrobblingAsync();
        }
    }
}
