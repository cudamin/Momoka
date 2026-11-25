using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Momoka.Bangumi;
using Momoka.Emby;
using Momoka.Infrastructure;

namespace Momoka.Shell.ViewModels;

public sealed partial class DetailViewModel
{
    private readonly LoadGeneration _bangumiRatingLoads = new();
    private BangumiClient? _bangumiRatingClient;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BangumiRatingVisibility))]
    [NotifyPropertyChangedFor(nameof(ScoreVisibility))]
    public partial string? BangumiRatingText { get; set; }

    public Visibility BangumiRatingVisibility => Show(BangumiRatingText is not null);

    private void AttachBangumiRating(IServiceProvider? services) =>
        _bangumiRatingClient = services?.GetService(typeof(BangumiClient)) as BangumiClient;

    private void DetachBangumiRating()
    {
        _bangumiRatingClient = null;
        ResetBangumiRating();
    }

    private void ResetBangumiRating()
    {
        _bangumiRatingLoads.Cancel();
        BangumiRatingText = null;
    }

    private void UpdateBangumiRatingSubject(EmbyItem detail)
    {
        ResetBangumiRating();
        if (Attached && _bangumiRatingClient is { } client && BangumiRatingIdentity.For(detail) is { } subject)
            _ = LoadBangumiRatingAsync(client, subject, _bangumiRatingLoads.Begin());
    }

    private async Task LoadBangumiRatingAsync(BangumiClient client, long subject, CancellationToken token)
    {
        try
        {
            var score = await client.RatingAsync(subject, token).ConfigureAwait(true);
            if (score is { } value && Attached && _bangumiRatingLoads.IsCurrent(token))
                BangumiRatingText = value.ToString("0.##", CultureInfo.InvariantCulture);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
