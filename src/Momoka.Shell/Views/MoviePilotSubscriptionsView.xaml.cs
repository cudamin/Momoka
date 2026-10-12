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

    public MoviePilotHomeRow? Row
    {
        get => (MoviePilotHomeRow?)GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    public MoviePilotSubscriptionsView()
    {
        InitializeComponent();
    }

    internal void Attach(MoviePilotService service, Action<MoviePilotSubscription>? open)
    {
        _standalone.Attach(service, open);
        ViewModel.UseConfirm(ConfirmDialog.For(this, dialog => MoviePilotDialog.ShowAsync(dialog, service)));
    }

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
