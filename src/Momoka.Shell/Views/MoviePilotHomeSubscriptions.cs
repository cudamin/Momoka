using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.Views;

/// <summary>主页两排订阅共用一次读取、一只刷新计时器和一套连接生命周期。</summary>
internal sealed class MoviePilotHomeSubscriptions
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMinutes(1) };
    private Action<MoviePilotSubscription>? _open;
    internal MoviePilotService? Service { get; private set; }
    internal MoviePilotSubscriptionsViewModel ViewModel { get; } = new();
    internal MoviePilotHomeRow Movies { get; }
    internal MoviePilotHomeRow Series { get; }
    internal event Action? Changed;

    internal MoviePilotHomeSubscriptions()
    {
        Movies = new(this, HomeLayout.MoviePilotMovies);
        Series = new(this, HomeLayout.MoviePilotSeries);
        _refresh.Tick += async (_, _) => { if (!ViewModel.Busy && !ViewModel.Working) await ViewModel.ReloadAsync(); };
        ViewModel.PropertyChanged += (_, _) => Changed?.Invoke();
    }

    internal void Attach(MoviePilotService service, Action<MoviePilotSubscription>? open)
    {
        Release();
        Service = service;
        _open = open;
        service.AvailabilityChanged += OnAvailabilityChanged;
        ViewModel.Attach(service);
        _ = ViewModel.ReloadAsync();
        _refresh.Start();
    }

    internal void Release()
    {
        if (Service is { } service) service.AvailabilityChanged -= OnAvailabilityChanged;
        Service = null;
        _refresh.Stop();
        ViewModel.Cancel();
        _open = null;
    }

    private void OnAvailabilityChanged(object? sender, EventArgs args) => _dispatcher.TryEnqueue(() =>
    {
        if (!ReferenceEquals(sender, Service)) return;
        ViewModel.RefreshAvailability();
        if (Service?.Available == true && !ViewModel.Busy && !ViewModel.Working) _ = ViewModel.ReloadAsync();
    });

    internal void Open(MoviePilotSubscriptionCard card)
    {
        if (ViewModel.Owns(card)) _open?.Invoke(card.Subscription);
    }
}

/// <summary>共享订阅数据中的一排；排序不重建数据，也不增加网络请求。</summary>
public sealed class MoviePilotHomeRow
{
    internal MoviePilotHomeRow(MoviePilotHomeSubscriptions owner, string key) { Owner = owner; Key = key; }
    internal MoviePilotHomeSubscriptions Owner { get; }
    public string Key { get; }
    public string Title => HomeLayout.FixedTitle(Key)!;
    public double RowHeight => Owner.ViewModel.RowHeight;
    internal bool HasContent => Owner.Service?.Available == true && Owner.ViewModel.Visible == Visibility.Visible
        && (Owner.ViewModel.NoticeOpen || (Key == HomeLayout.MoviePilotMovies ? Owner.ViewModel.Movies.Count : Owner.ViewModel.Series.Count) > 0);
}
