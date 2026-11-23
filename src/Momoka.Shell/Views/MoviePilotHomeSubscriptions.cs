using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

/// <summary>主页两排订阅共用一次读取和一套连接生命周期；只在进入、手动刷新或连接恢复时读取。</summary>
internal sealed class MoviePilotHomeSubscriptions
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
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
    }

    internal void Release()
    {
        if (Service is { } service) service.AvailabilityChanged -= OnAvailabilityChanged;
        Service = null;
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

    /// <summary>主页那两排跟随「拖动封面翻页」开关；订阅列表页自己建的属主不设，保持默认关。</summary>
    internal void ApplyDragPaging(bool enabled)
    {
        Movies.DragPaging = enabled;
        Series.DragPaging = enabled;
    }
}

/// <summary>共享订阅数据中的一排；排序不重建数据，也不增加网络请求。</summary>
public sealed partial class MoviePilotHomeRow : ObservableObject
{
    internal MoviePilotHomeRow(MoviePilotHomeSubscriptions owner, string key) { Owner = owner; Key = key; }
    internal MoviePilotHomeSubscriptions Owner { get; }
    public string Key { get; }
    public string Title => HomeLayout.FixedTitle(Key)!;
    public double RowHeight => Owner.ViewModel.RowHeight;

    /// <summary>主页上这一排能不能用鼠标拖动翻页，绑到两排订阅各自 <see cref="ShelfStrip.DragPaging"/>。</summary>
    [ObservableProperty]
    public partial bool DragPaging { get; set; }

    internal bool HasContent => Owner.Service?.Available == true && Owner.ViewModel.Visible == Visibility.Visible
        && (Owner.ViewModel.NoticeOpen || (Key == HomeLayout.MoviePilotMovies ? Owner.ViewModel.Movies.Count : Owner.ViewModel.Series.Count) > 0);
}
