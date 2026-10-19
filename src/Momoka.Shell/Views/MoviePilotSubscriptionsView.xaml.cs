using Momoka.MoviePilot;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotSubscriptionsView : UserControl
{
    private readonly MoviePilotHomeSubscriptions _standalone = new();
    private MoviePilotHomeSubscriptions Source => Row?.Owner ?? _standalone;
    internal MoviePilotSubscriptionsViewModel ViewModel => Source.ViewModel;

    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(
        nameof(Row), typeof(MoviePilotHomeRow), typeof(MoviePilotSubscriptionsView),
        new PropertyMetadata(null, (owner, _) => ((MoviePilotSubscriptionsView)owner).Bindings.Update()));

    /// <summary>
    /// 整页宿主（订阅列表）才给那两颗「刷新订阅」；主页那两排的标题右边只有标题和读数。
    /// 两处共用这一份视图，所以由宿主点名，而不是在标记里按 <see cref="Row"/> 猜。
    /// </summary>
    public static readonly DependencyProperty ShowRefreshProperty = DependencyProperty.Register(
        nameof(ShowRefresh), typeof(bool), typeof(MoviePilotSubscriptionsView),
        new PropertyMetadata(false, OnShowRefreshChanged));

    public bool ShowRefresh
    {
        get => (bool)GetValue(ShowRefreshProperty);
        set => SetValue(ShowRefreshProperty, value);
    }

    private static void OnShowRefreshChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (MoviePilotSubscriptionsView)sender;
        var visibility = args.NewValue is true ? Visibility.Visible : Visibility.Collapsed;
        view.RefreshSubscriptions.Visibility = visibility;
        view.RefreshSeriesSubscriptions.Visibility = visibility;
    }

    public MoviePilotHomeRow? Row
    {
        get => (MoviePilotHomeRow?)GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    public MoviePilotSubscriptionsView()
    {
        InitializeComponent();

        // 两排的名字从 Core 那一份取，不在这里再写一遍字面量（2026-10-13 晚改名时发现这里本来各有一份，
        // 和 HomeLayout.FixedTitle、和侧栏那两条入口的字面量是同一句话的四个副本）。这一控件同时供主页
        // 那两排和订阅列表页使用，所以改名一处生效也要求这一处跟着走。
        MoviesHead.Title = HomeLayout.FixedTitle(HomeLayout.MoviePilotMovies) ?? string.Empty;
        SeriesHead.Title = HomeLayout.FixedTitle(HomeLayout.MoviePilotSeries) ?? string.Empty;
    }

    internal void Attach(MoviePilotService service, Action<MoviePilotSubscription>? open)
    {
        _standalone.Attach(service, open);
        ViewModel.UseConfirm(ConfirmDialog.For(this, dialog => MoviePilotDialog.ShowAsync(dialog, service)));
    }

    /// <summary>
    /// 整页宿主用（MoviePilotSubscriptionsPage）：把这一排接到调用方自己的订阅数据上，两边共用同一套读取、
    /// 刷新与订阅管理菜单。**Row 必须先落定再装确认框** —— <see cref="ViewModel"/> 是从 Row 的属主那儿取的，
    /// 反过来的话那一问就装到这一页用不到的备用属主上了。
    /// </summary>
    internal void AttachRow(MoviePilotHomeSubscriptions owner, MoviePilotHomeRow row, MoviePilotService service,
        Action<MoviePilotSubscription>? open)
    {
        owner.Attach(service, open);
        Row = row;
        ViewModel.UseConfirm(ConfirmDialog.For(this, dialog => MoviePilotDialog.ShowAsync(dialog, service)));
    }

    /// <summary>整页宿主用：松开那一排，之后 <see cref="ViewModel"/> 回到这一控件自己的备用属主。</summary>
    internal void DetachRow() => Row = null;

    internal void Release()
    {
        _standalone.Release();
    }

    private void Open(MoviePilotSubscriptionCard card) => Source.Open(card);
    private Visibility MoviesVisible(Visibility visible, bool notice) =>
        Row?.Key != HomeLayout.MoviePilotSeries && (visible == Visibility.Visible || notice) ? Visibility.Visible : Visibility.Collapsed;
    private Visibility SeriesVisible(Visibility visible, bool notice) =>
        Row?.Key != HomeLayout.MoviePilotMovies && (visible == Visibility.Visible || notice) ? Visibility.Visible : Visibility.Collapsed;
    private void OnPosterOpen(object? sender, EventArgs args) { if (sender is MoviePilotSubscriptionPoster { Card: { } card }) Open(card); }
    private void OnPosterRequested(object? sender, EventArgs args)
    { if (sender is MoviePilotSubscriptionPoster { Card: { } card }) _ = ViewModel.LoadPosterAsync(card); }
    private void OnPosterMenu(object? sender, EventArgs args)
    {
        if (sender is MoviePilotSubscriptionPoster { Card: { } card } poster) Menu(card).ShowAt(poster.MenuAnchor);
    }
    private MenuFlyout Menu(MoviePilotSubscriptionCard card)
    {
        if (Source.Service is { } service)
            ViewModel.UseConfirm(ConfirmDialog.For(this, dialog => MoviePilotDialog.ShowAsync(dialog, service)));
        return MoviePilotSubscriptionMenu.Create(this, ViewModel, card, () => Open(card), Source.Service);
    }
}
