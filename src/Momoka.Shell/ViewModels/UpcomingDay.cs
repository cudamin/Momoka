using System.Collections.ObjectModel;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

public sealed class UpcomingDay(DateOnly? date, string title, int cardWidth)
{
    public DateOnly? Date { get; } = date;
    public string Title { get; } = title;
    public int CardWidth { get; } = cardWidth;
    public double RowHeight => LibraryUpcoming.RowHeight(CardWidth);
    public ObservableCollection<CardItem> Cards { get; } = [];
}
