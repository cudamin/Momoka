using System.Collections.ObjectModel;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

// Verbatim UpdateUpcomingDays + LoadAsync + exact query/source guard are generated at build.
// Only unrelated page chrome, other routes and the read endpoint are inert. Actual PageViewModel,
// LoadGeneration, LibraryRequest, UpcomingDay, CardItem and Core date grouping execute unchanged.
public sealed partial class LibraryViewModel : PageViewModel
{
    private const string Category = "预告测试";
    private const int NotLoaded = -1;
    private bool _active = true;
    private LibraryRequest? _request;
    private object? _actions = new();
    private readonly LoadGeneration _queries = new();
    private readonly LoadGeneration _jumps = new();
    private readonly LoadGeneration _favoriteLoads = new();
    private readonly HashSet<string> _resumeRead = new(StringComparer.Ordinal);
    private CancellationToken _queryToken;
    private AggregateQueryCursor? _cursor;
    private bool _firstPage;
    private bool _pageFailed;
    private int _total = NotLoaded;
    private int _upcomingReadCount;
    private readonly int _upcomingCardWidth = LibraryUpcoming.CardWidth;
    private readonly EmbyImageStore _images;
    private IReadOnlyList<EmbySessionScope> _sources = [];

    internal LibraryViewModel(EmbyImageStore images)
    {
        _images = images;
        _request = Request(null);
    }

    public ObservableCollection<CardItem> Cards { get; } = [];
    public ObservableCollection<UpcomingDay> UpcomingDays { get; } = [];
    internal bool IsUpcomingPage => _request?.Section == LibrarySection.Upcoming;
    private bool IsResumePage => false;
    private bool IsSectionLibrary => true;
    private bool IsRecommendedPage => false;
    private bool IsFavoritesPage => false;
    private bool UsesQueryCursor => false;
    private bool SourcesAvailable => _request?.Source?.IsAvailable ?? true;
    private bool HasMore => _total < 0 || _upcomingReadCount < _total;
    private bool Aggregated => false;
    private List<FavoriteBoundary> FavoriteRows { get; } = [];
    private string? EmptyNotice { get; set; }
    private bool ShowEmptyNotice { get; set; }
    private string Heading => _request?.Title ?? "测试";
    private string SearchText => "";
    internal event Action? PageArrived;
    internal int ReadCount => _upcomingReadCount;
    internal Func<int, CancellationToken, Task<ItemsResult?>> Read { get; set; } =
        (_, _) => throw new InvalidOperationException("PERF 测试没有真实读取端点");

    internal void Update() => UpdateUpcomingDays();
    internal Task Load(bool reset) => LoadAsync(reset);
    public override Task ReloadAsync() => LoadAsync(true);
    internal void SwitchSource(EmbySessionScope source)
    {
        base.Cancel();
        _queries.Cancel();
        _request = Request(source);
        _sources = [source];
        _active = true;
    }
    internal void Leave()
    {
        _active = false;
        _queries.Cancel();
        base.Cancel();
    }
    internal void LeaveUpcoming() { _request = _request! with { Section = LibrarySection.Items }; }
    private static LibraryRequest Request(EmbySessionScope? source) => new()
    {
        Services = null!, Title = "预告夹具", ParentId = "library", CollectionType = "tvshows",
        Section = LibrarySection.Upcoming, Source = source
    };
    internal CardItem NewCard(EmbyItem item) => new(item, _images, _upcomingCardWidth, wide: true, upcoming: true);
    private void RefreshUpcomingState() { }
    private void RowsArrived() { }
    private void ResetCursor() { _cursor = null; _resumeRead.Clear(); }
    private Task<ItemsResult?> ReadAsync(int start, CancellationToken token) => Read(start, token);
    private AggregateQueryCursor NewCursor() => throw new InvalidOperationException("非预告路由");
    private Task<ItemsResult> ReadCursorAsync(AggregateQueryCursor cursor, CancellationToken token) =>
        throw new InvalidOperationException("非预告路由");
    private Task LoadRecommendedAsync(bool reset) => throw new InvalidOperationException("非预告路由");
    private Task LoadFavoritesAsync(bool reset) => throw new InvalidOperationException("非预告路由");
    private sealed class FavoriteBoundary { internal bool Loading { get; set; } }

    public override void Dispose()
    {
        _queries.Dispose(); _jumps.Dispose(); _favoriteLoads.Dispose();
        base.Dispose();
    }
}
