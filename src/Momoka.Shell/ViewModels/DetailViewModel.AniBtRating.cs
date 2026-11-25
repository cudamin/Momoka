using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Momoka.AniBt;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Localization;
using Momoka.Shell.Platform;

namespace Momoka.Shell.ViewModels;

public sealed partial class DetailViewModel
{
    private readonly LoadGeneration _aniBtRatingLoads = new();
    private AniBtClient? _aniBtRatingClient;
    private IUiDispatcher? _aniBtRatingDispatcher;
    private long? _aniBtSubject;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AniBtRatingVisibility))]
    public partial string? AniBtRatingText { get; set; }

    public Visibility AniBtRatingVisibility => Show(AniBtRatingText is not null);

    private void AttachAniBtRating(IServiceProvider? services)
    {
        _aniBtRatingClient = services?.GetService(typeof(AniBtClient)) as AniBtClient;
        _aniBtRatingDispatcher = services?.GetService(typeof(IUiDispatcher)) as IUiDispatcher;
        if (_aniBtRatingClient is not null) Settings.MoviePilot.AniBtChanged += OnAniBtRatingSettingChanged;
    }

    private void DetachAniBtRating()
    {
        if (_aniBtRatingClient is not null) Settings.MoviePilot.AniBtChanged -= OnAniBtRatingSettingChanged;
        _aniBtRatingClient = null;
        _aniBtRatingDispatcher = null;
        ResetAniBtRating();
    }

    private void ResetAniBtRating()
    {
        _aniBtSubject = null;
        _aniBtRatingLoads.Cancel();
        AniBtRatingText = null;
    }

    private void OnAniBtRatingSettingChanged(object? sender, EventArgs args)
    {
        var visit = _browseVisit;
        _aniBtRatingDispatcher?.Run(() =>
        {
            if (ReferenceEquals(visit, _browseVisit)) RefreshAniBtRating();
        });
    }

    private void UpdateAniBtRatingSubject(EmbyItem detail)
    {
        _aniBtSubject = AniBtRatingIdentity.For(detail);
        RefreshAniBtRating();
    }

    private void RefreshAniBtRating()
    {
        _aniBtRatingLoads.Cancel();
        AniBtRatingText = null;
        if (!Attached || !Settings.MoviePilot.AniBtEnabled || _aniBtSubject is not { } subject
            || _aniBtRatingClient is not { } client) return;

        _ = LoadAniBtRatingAsync(client, subject, _aniBtRatingLoads.Begin());
    }

    private async Task LoadAniBtRatingAsync(AniBtClient client, long subject, CancellationToken token)
    {
        try
        {
            var score = await client.ReferenceRatingAsync(subject, token).ConfigureAwait(true);
            if (score is { } value && Attached && _aniBtRatingLoads.IsCurrent(token) && Settings.MoviePilot.AniBtEnabled)
                AniBtRatingText = UiText.Format("AniBT 参考分 {0:0.0#}", value);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (AniBtException error)
        {
            Log.Debug(Category, error.Message);
        }
    }
}
