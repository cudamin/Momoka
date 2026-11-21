using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Music;
using Momoka.Shell.Localization;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

public sealed partial class MusicLibraryViewModel : PageViewModel
{
    private readonly LoadGeneration _loads = new();
    private EmbySessionScope? _scope;
    private EmbyImageStore? _images;
    private LibraryRequest? _request;
    private bool _active;
    private int _offset;
    private int _total;
    private string _loadedSearch = "";
    private MusicSection _loadedSection;
    private bool _insideAlbum;
    private string _playingKey = "";

    public ObservableCollection<CardItem> Rows { get; } = [];
    [ObservableProperty] public partial string Title { get; set; } = UiText.Translate("音乐");
    [ObservableProperty] public partial string Search { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackViewVisibility))]
    [NotifyPropertyChangedFor(nameof(WallVisibility))]
    public partial MusicSection Section { get; set; }
    [ObservableProperty] public partial bool MoreAvailable { get; set; }
    [ObservableProperty] public partial string CountLabel { get; set; } = "";
    [ObservableProperty] public partial Visibility TabsVisibility { get; set; }
    [ObservableProperty] public partial Visibility EmptyVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial Visibility FavoriteTabVisibility { get; set; }
    [ObservableProperty] public partial Visibility SearchVisibility { get; set; } = Visibility.Visible;
    [ObservableProperty] public partial Visibility ColumnHeaderVisibility { get; set; }
    public IReadOnlyList<EmbyItem> Tracks => Rows.Select(row => row.Item).Where(item => MusicCatalog.IsAudio(item.Type)).ToArray();

    /// <summary>歌曲和收藏是一张曲目表；专辑、艺术家、歌单是一面方形封面墙。</summary>
    public Visibility TrackViewVisibility => Show(!MusicCatalog.UsesCoverWall(Section));

    /// <inheritdoc cref="TrackViewVisibility"/>
    public Visibility WallVisibility => Show(MusicCatalog.UsesCoverWall(Section));

    /// <summary>封面墙一格的尺寸；解码宽度和画出来的宽度都从它来（见 CardItem 的构造参数）。</summary>
    public double TileWidth => MusicCatalog.WallTileWidth;

    public double TileHeight => MusicCatalog.WallTileHeight;

    internal void Attach(LibraryRequest request, EmbySession session, EmbyImageStore images)
    {
        _request = request;
        _scope = request.Source ?? session.Capture();
        _images = images;
        _active = true;
        _insideAlbum = request.MusicContainerType == EmbyItemType.MusicAlbum;
        Title = request.Title;
        Search = request.SearchTerm ?? "";
        Section = MusicSection.Tracks;
        TabsVisibility = request.MusicContainerType is null ? Visibility.Visible : Visibility.Collapsed;
        FavoriteTabVisibility = _scope.Connection.IsPlex ? Visibility.Collapsed : Visibility.Visible;
        SearchVisibility = (_scope.Connection.IsFnMusic || _scope.Connection.IsPlex) && request.MusicContainerType is not null
            ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// 把「正在放的是哪一首」落到曲目表上。页面从底部播放栏那一侧读到变化后调这里 ——
    /// 视图模型不订阅播放服务，播放状态归播放服务所有，这里只负责让已有的行跟着换颜色。
    /// </summary>
    internal void MarkPlaying(string? key)
    {
        _playingKey = key ?? "";
        foreach (var row in Rows)
            row.IsPlaying = _playingKey.Length > 0 && row.Item.IdentityKey == _playingKey;
    }

    public override Task ReloadAsync() => LoadAsync(reset: true);
    internal Task LoadMoreAsync() => MoreAvailable ? LoadAsync(reset: false) : Task.CompletedTask;

    internal Func<CancellationToken, Task<IReadOnlyList<EmbyItem>>> CaptureQueueLoader()
    {
        var scope = _scope ?? throw new InvalidOperationException("音乐来源尚未连接");
        var request = _request ?? throw new InvalidOperationException("音乐页面已离开");
        var section = _loadedSection;
        var search = _loadedSearch;
        // Capture values, never the page lifetime. Navigation must not cancel music's owned preparation.
        return async token =>
        {
            var items = new List<EmbyItem>();
            var offset = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var query = MusicCatalog.Query(request.MusicRootId ?? request.ParentId,
                    request.MusicContainerType is null ? null : request.ParentId,
                    request.MusicContainerType, section, offset, search);
                var page = await scope.ExecuteAsync((client, cancellation) => client.GetItemsAsync(query, cancellation), token);
                scope.ThrowIfUnavailable();
                token.ThrowIfCancellationRequested();
                foreach (var item in page.Items.Where(item => MusicCatalog.IsAudio(item.Type))) { scope.Bind(item); items.Add(item); }
                offset += page.Items.Count;
                if (page.Items.Count == 0 || offset >= page.TotalRecordCount) break;
            }
            return items;
        };
    }

    internal async Task SelectAsync(MusicSection section)
    {
        Section = section;
        if (_scope?.Connection.IsFnMusic == true)
        {
            SearchVisibility = section == MusicSection.Favorites || _request?.MusicContainerType is not null
                ? Visibility.Collapsed : Visibility.Visible;
            if (section == MusicSection.Favorites) Search = "";
        }
        if (_scope?.Connection.IsPlex == true)
        {
            SearchVisibility = section == MusicSection.Playlists || _request?.MusicContainerType is not null
                ? Visibility.Collapsed : Visibility.Visible;
            if (section == MusicSection.Playlists) Search = "";
        }
        await ReloadAsync();
    }

    private async Task LoadAsync(bool reset)
    {
        if (!_active || _scope is not { IsAvailable: true } scope || _request is not { } request || _images is null) return;
        if (!reset && Busy) return;
        var token = _loads.Begin();
        Busy = true;
        IsReady = false;
        NoticeOpen = false;
        if (reset)
        {
            foreach (var row in Rows) row.AbandonPoster();
            Rows.Clear();
            _offset = 0;
            _total = 0;
            MoreAvailable = false;
            ColumnHeaderVisibility = Visibility.Collapsed;
            EmptyVisibility = Visibility.Collapsed;
        }
        var query = MusicCatalog.Query(request.MusicRootId ?? request.ParentId,
            request.MusicContainerType is null ? null : request.ParentId,
            request.MusicContainerType, reset ? Section : _loadedSection, _offset, reset ? Search : _loadedSearch);
        try
        {
            var result = await scope.ExecuteAsync((client, cancellation) => client.GetItemsAsync(query, cancellation), token);
            if (!Live()) return;
            _loadedSearch = query.SearchTerm ?? "";
            if (reset) _loadedSection = Section;
            _offset += result.Items.Count;
            _total = result.TotalRecordCount;
            var wall = MusicCatalog.UsesCoverWall(reset ? Section : _loadedSection);
            foreach (var item in result.Items)
            {
                scope.Bind(item);
                Rows.Add(wall
                    ? new CardItem(item, _images, MusicCatalog.WallTileWidth, aspect: 1,
                        subtitle: MusicCatalog.WallSubtitle(item), indicators: false)
                    : new CardItem(item, _images, MusicCatalog.TrackArtWidth, aspect: 1,
                        subtitle: MusicCatalog.RowSubtitle(item, _insideAlbum), indicators: false)
                    { IsPlaying = _playingKey.Length > 0 && item.IdentityKey == _playingKey });
            }
            MoreAvailable = result.Items.Count > 0 && _offset < _total;
            CountLabel = MusicCatalog.CountLabel(_loadedSection, _total);
            ColumnHeaderVisibility = Show(!wall && Rows.Count > 0);
            EmptyVisibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || !scope.IsAvailable) { }
        catch (Exception error)
        {
            if (Live()) Report(UiText.Translate("读取音乐失败"), error);
        }
        finally
        {
            if (Live()) { Busy = false; IsReady = true; }
        }

        bool Live() => _active && _loads.IsCurrent(token) && ReferenceEquals(_scope, scope) && scope.IsAvailable;
    }

    internal void Release()
    {
        _active = false;
        _loads.Cancel();
        foreach (var row in Rows) row.AbandonPoster();
    }

    public static string Duration(EmbyItem item) => MusicCatalog.IsAudio(item.Type)
        ? TimeFormat.Clock(item.RunTimeTicks ?? 0) : EmbyItemType.ToChinese(item.Type);
}
