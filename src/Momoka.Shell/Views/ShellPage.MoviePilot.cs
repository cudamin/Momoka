using Momoka.MoviePilot;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private MoviePilotService? _moviePilotService;
    private readonly DispatcherTimer _moviePilotTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource? _moviePilotCheck;
    private ContentDialog? _moviePilotDialog;
    public event EventHandler? MoviePilotAvailabilityChanged;

    private void StartMoviePilotMonitoring(MoviePilotService service)
    {
        _moviePilotService = service;
        service.AvailabilityChanged += OnMoviePilotAvailabilityChanged;
        Settings.MoviePilot.ConnectionChanged += OnMoviePilotConfigurationChanged;
        _moviePilotTimer.Tick += CheckMoviePilot;
        _moviePilotTimer.Start();
        CheckMoviePilot(null, null);
    }

    private async void CheckMoviePilot(object? sender, object? args)
    {
        if (_moviePilotService is not { } service || _moviePilotCheck is not null) return;
        _moviePilotTimer.Interval = TimeSpan.FromSeconds(30);
        using var check = new CancellationTokenSource();
        _moviePilotCheck = check;
        try { await service.RefreshAvailabilityAsync(check.Token); }
        catch (OperationCanceledException) { }
        finally { _moviePilotCheck = null; }
    }

    private void OnMoviePilotConfigurationChanged(object? sender, EventArgs args)
    {
        _moviePilotCheck?.Cancel();
        _moviePilotTimer.Stop();
        _moviePilotTimer.Interval = TimeSpan.FromMilliseconds(700);
        _moviePilotTimer.Start();
    }

    private void OnMoviePilotAvailabilityChanged(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        MoviePilotAvailabilityChanged?.Invoke(this, EventArgs.Empty);
        if (_moviePilotService is null || _moviePilotService.Available) return;
        _moviePilotWindow?.Hide();
        _moviePilotDialog?.Hide();
        if (ContentFrame.Content is MoviePilotSubscriptionPage) GoTo("home");
        PruneMoviePilotHistory();
        SyncChrome();
    });

    private void PruneMoviePilotHistory()
    {
        if (_moviePilotService?.Available == true) return;
        for (var index = ContentFrame.BackStack.Count - 1; index >= 0; index--)
        {
            if (ContentFrame.BackStack[index].SourcePageType != typeof(MoviePilotSubscriptionPage)) continue;
            ContentFrame.BackStack.RemoveAt(index);
            if (index < _backTrails.Count) _backTrails.RemoveAt(index);
        }
        for (var index = ContentFrame.ForwardStack.Count - 1; index >= 0; index--)
        {
            if (ContentFrame.ForwardStack[index].SourcePageType != typeof(MoviePilotSubscriptionPage)) continue;
            ContentFrame.ForwardStack.RemoveAt(index);
            if (index < _forwardTrails.Count) _forwardTrails.RemoveAt(index);
        }
    }

    private void StopMoviePilotMonitoring()
    {
        _moviePilotTimer.Stop();
        _moviePilotTimer.Tick -= CheckMoviePilot;
        _moviePilotCheck?.Cancel();
        Settings.MoviePilot.ConnectionChanged -= OnMoviePilotConfigurationChanged;
        if (_moviePilotService is { } service) service.AvailabilityChanged -= OnMoviePilotAvailabilityChanged;
        _moviePilotService = null;
    }
}
