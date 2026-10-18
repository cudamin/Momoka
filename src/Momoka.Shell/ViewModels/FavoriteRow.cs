using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

public sealed partial class FavoriteRow : ObservableObject
{
    internal FavoriteRow(LibraryFavoriteRow entry, int cardWidth)
    {
        Entry = entry;
        CardWidth = cardWidth;
        Cards.CollectionChanged += (_, _) => RefreshState();
    }

    internal LibraryFavoriteRow Entry { get; }
    public string Title => Entry.Title;
    public FavoriteShelf Shelf => Entry.Shelf;
    public bool Wide => Entry.Wide;
    public ObservableCollection<CardItem> Cards { get; } = [];
    internal int ReadCount { get; set; }
    internal int Total { get; set; } = -1;
    internal bool HasMore => Total < 0 || ReadCount < Total;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowHeight))]
    public partial int CardWidth { get; set; }

    public double RowHeight => LibraryFavorites.RowHeight(CardWidth, Wide);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusVisibility))]
    [NotifyPropertyChangedFor(nameof(GroupVisibility))]
    [NotifyPropertyChangedFor(nameof(RetryVisibility))]
    [NotifyPropertyChangedFor(nameof(MoreVisibility))]
    public partial bool Loading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusVisibility))]
    [NotifyPropertyChangedFor(nameof(GroupVisibility))]
    [NotifyPropertyChangedFor(nameof(RetryVisibility))]
    [NotifyPropertyChangedFor(nameof(MoreVisibility))]
    public partial string Error { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupVisibility))]
    [NotifyPropertyChangedFor(nameof(MoreVisibility))]
    public partial bool Loaded { get; set; }

    public string StatusText => Loading ? "正在加载…" : Error;
    public Visibility StatusVisibility => Show(StatusText.Length > 0);
    public Visibility GroupVisibility => Show(!Loaded || Loading || Error.Length > 0 || Cards.Count > 0);
    public Visibility RetryVisibility => Show(!Loading && Error.Length > 0);
    public Visibility MoreVisibility => Show(Loaded && !Loading && Error.Length == 0 && HasMore);

    internal void RefreshState()
    {
        OnPropertyChanged(nameof(GroupVisibility));
        OnPropertyChanged(nameof(MoreVisibility));
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
