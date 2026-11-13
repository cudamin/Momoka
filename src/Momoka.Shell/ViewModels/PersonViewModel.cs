using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Media;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 人物页：一个人自己的资料先出来，作品按「节目」「电影」「集」三排和「影视作品」的分组清单接在后面。
/// <para>
/// 资料和作品是两条独立的路：人物条目读失败时，作品仍按 id 去问，页面至少还能当「这个人参演了什么」用；
/// 反过来作品读失败也不影响出生地和链接。所以这里没有一个大 <c>Task.WhenAll</c> 把整页绑在一起，只有
/// 每条路各自的失败与重试。
/// </para>
/// <para>
/// 这个类只做 Emby/Jellyfin：飞牛的人物没有独立详情条目（官方协议只有 person/list 与 person/item/list），
/// Plex 的人物是本地标签、没有可导航的身份。两种来源仍在原来的作品网格里，不伪造一个人物页。
/// </para>
/// </summary>
public sealed partial class PersonViewModel : PageViewModel
{
    private const string Category = "人物";
    private const int PortraitWidth = 200;

    private IShellActions? _actions;
    private EmbyImageStore? _images;
    private EmbySessionScope? _scope;
    private EmbyItem? _person;
    private EmbyItem? _detail;
    private string _personId = "";
    private CardItem? _commandCard;
    private bool _personFailed;
    private bool _creditsFailed;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string BirthLine { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeathVisibility))]
    public partial string DeathLine { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BirthplaceVisibility))]
    public partial string BirthplaceLine { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverviewVisibility))]
    public partial string Overview { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PortraitVisibility), nameof(PortraitPlaceholderVisibility))]
    public partial ImageSource? Portrait { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteVisibility), nameof(NotFavoriteVisibility), nameof(FavoriteActionTip))]
    public partial bool Favorite { get; set; }

    [ObservableProperty]
    public partial CardShelf? SeriesShelf { get; set; }

    [ObservableProperty]
    public partial CardShelf? MovieShelf { get; set; }

    [ObservableProperty]
    public partial CardShelf? EpisodeShelf { get; set; }

    [ObservableProperty]
    public partial Visibility SeriesVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility MovieVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility EpisodeVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility FilmographyVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility OtherVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility EmptyVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility RetryVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility FavoriteActionVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility EditVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial IReadOnlyList<OtherFact> OtherFacts { get; set; } = [];

    /// <summary>影视作品一组一组；行和标题在 <see cref="ApplyFilmography"/> 里一次填好。</summary>
    public ObservableCollection<PersonCreditGroup> Filmography { get; } = [];

    public Visibility BirthVisibility => Show(BirthLine.Length > 0);

    public Visibility DeathVisibility => Show(DeathLine.Length > 0);

    public Visibility BirthplaceVisibility => Show(BirthplaceLine.Length > 0);

    public Visibility OverviewVisibility => Show(Overview.Length > 0);

    public Visibility PortraitVisibility => Show(Portrait is not null);

    public Visibility PortraitPlaceholderVisibility => Show(Portrait is null);

    public Visibility FavoriteVisibility => Show(Favorite);

    public Visibility NotFavoriteVisibility => Show(!Favorite);

    public string FavoriteActionTip => Favorite ? "取消收藏" : "添加到收藏";

    /// <summary>页面上「更多」和右键菜单共用的这一张卡：人物自己是这个页面的主角，不是货架上的一张海报。</summary>
    internal CardItem CommandCard => _commandCard ?? throw new InvalidOperationException("人物页尚未附加");

    /// <summary>The person this page should show, once the stub or the fetched entry has an id.</summary>
    internal EmbyItem Subject => _detail ?? _person ?? throw new InvalidOperationException("人物页尚未附加");

    internal void Attach(PersonRequest request, IShellActions actions, EmbyImageStore images)
    {
        // The Frame reuses a page instance for the same page type: a second person opened through
        // back/forward lands on this same view model. Everything below belongs to the previous person
        // until it is replaced here, and a stale portrait or filmography group would read as fact.
        SeriesShelf?.Clear();
        MovieShelf?.Clear();
        EpisodeShelf?.Clear();
        Filmography.Clear();
        OtherFacts = [];
        Portrait = null;
        BirthLine = "";
        DeathLine = "";
        BirthplaceLine = "";
        Overview = "";
        Favorite = false;
        _detail = null;
        _personFailed = false;
        _creditsFailed = false;
        RetryVisibility = Visibility.Collapsed;
        EmptyVisibility = Visibility.Collapsed;
        FilmographyVisibility = Visibility.Collapsed;
        OtherVisibility = Visibility.Collapsed;
        SeriesVisibility = Visibility.Collapsed;
        MovieVisibility = Visibility.Collapsed;
        EpisodeVisibility = Visibility.Collapsed;
        FavoriteActionVisibility = Visibility.Collapsed;
        EditVisibility = Visibility.Collapsed;

        _actions = actions;
        _images = images;
        _person = request.Person;
        _personId = request.Person.Id;
        Name = request.Person.Name;

        SeriesShelf = new CardShelf("节目", images, CardSize.PosterWidth, wide: false);
        MovieShelf = new CardShelf("电影", images, CardSize.PosterWidth, wide: false);
        EpisodeShelf = new CardShelf("集", images, CardSize.WideWidth, wide: true);

        ReplaceCommandCard(request.Person);
        _scope = images.CaptureScope(request.Person);
        if (_scope is not { IsAvailable: true } scope)
        {
            Report("无法打开人物页", new InvalidOperationException("这个人物所属的账号已退出，请重新登录后打开"));
            return;
        }

        FavoriteActionVisibility = Show(scope.Connection.SupportsFavorites);
        EditVisibility = Show(ItemMenu.Allows(ItemCommand.EditMetadata,
            actions.MenuPermissionsFor(scope), scope.Connection.ServerKind, request.Person));
    }

    /// <summary>
    /// 读这一页。人物资料失败不抛给整页：作品仍按 id 去问，页面只是少几行字。
    /// 新的一趟会取消上一趟（见 <see cref="PageViewModel.BeginLoad"/>），迟到的结果不会覆盖新页面。
    /// </summary>
    public async Task LoadAsync()
    {
        if (_scope is not { } scope || !scope.IsAvailable) return;

        var token = BeginLoad();
        _personFailed = false;
        _creditsFailed = false;
        RetryVisibility = Visibility.Collapsed;
        EmptyVisibility = Visibility.Collapsed;

        try
        {
            var person = await ReadPersonAsync(scope, token).ConfigureAwait(true);
            if (!IsCurrent(token)) return;

            if (person is not null)
            {
                await ApplyPersonAsync(person, scope, token).ConfigureAwait(true);
                if (!IsCurrent(token)) return;
            }
            else
            {
                _personFailed = true;
                Notify("人物资料读取失败", "只显示已经取到的作品；可以稍后重试", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            }

            var seriesTask = ReadCreditsAsync(scope, null, [EmbyItemType.Series], EmbyFields.Browse,
                PersonDetail.RailLimit, EmbySortBy.ReleaseDate, descending: true, token);
            var movieTask = ReadCreditsAsync(scope, null, [EmbyItemType.Movie], EmbyFields.Browse,
                PersonDetail.RailLimit, EmbySortBy.ReleaseDate, descending: true, token);
            var episodeTask = ReadCreditsAsync(scope, null, [EmbyItemType.Episode], EmbyFields.Browse,
                PersonDetail.RailLimit, EmbySortBy.ReleaseDate, descending: true, token);
            var groupTasks = PersonDetail.CreditTypes
                .Select(type => ReadCreditsAsync(scope, type, PersonDetail.FilmographyTypes, EmbyFields.Credits,
                    PersonDetail.FilmographyLimit, EmbySortBy.ReleaseDate, descending: true, token))
                .ToList();

            var all = new List<Task<ItemsResult?>>(groupTasks.Count + 3) { seriesTask, movieTask, episodeTask };
            all.AddRange(groupTasks);
            await Task.WhenAll(all).ConfigureAwait(true);
            if (!IsCurrent(token)) return;

            var series = await seriesTask.ConfigureAwait(true);
            var movies = await movieTask.ConfigureAwait(true);
            var episodes = await episodeTask.ConfigureAwait(true);
            var groups = new List<ItemsResult?>(groupTasks.Count);
            foreach (var task in groupTasks) groups.Add(await task.ConfigureAwait(true));

            if (!scope.IsAvailable) return;

            if (series is not null) SeriesShelf?.Fill(series.Items);
            if (movies is not null) MovieShelf?.Fill(movies.Items);
            if (episodes is not null) EpisodeShelf?.Fill(episodes.Items);
            ApplyFilmography(groups);

            // 三条作品路全空、且人物资料也没回来，才是真的「这一页什么都没有」；只缺其中一条时
            // 不拿一句「没有作品」盖掉已经取到的那几排。
            _creditsFailed = series is null && movies is null && episodes is null
                && groups.All(group => group is null);
            if (_creditsFailed && !_personFailed)
                Notify("作品读取失败", "人物资料已显示，作品清单可以稍后重试", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);

            EndLoad(token);
            UpdateVisibility();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (!IsCurrent(token)) return;
            _personFailed = true;
            _creditsFailed = true;
            Report("读取演职人员失败", error);
            EndLoad(token);
            UpdateVisibility();
        }
    }

    private async Task<EmbyItem?> ReadPersonAsync(EmbySessionScope scope, CancellationToken token)
    {
        try
        {
            var person = await scope.ExecuteAsync(
                    (client, cancellation) => client.GetPersonAsync(_person!, cancellation), token)
                .ConfigureAwait(true);
            scope.ThrowIfUnavailable();
            return person;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            Log.Debug(Category, $"读取人物资料失败（{_person?.Name}）：{error.Message}");
            return null;
        }
    }

    private async Task<ItemsResult?> ReadCreditsAsync(EmbySessionScope scope, string? personType,
        IReadOnlyList<string> includeItemTypes, string fields, int limit, string sortBy, bool descending,
        CancellationToken token)
    {
        if (_personId.Length == 0) return null;

        try
        {
            var result = await scope.ExecuteAsync((client, cancellation) => client.GetPersonCreditsAsync(
                    _personId, personType, includeItemTypes, fields, limit, sortBy, descending, cancellation), token)
                .ConfigureAwait(true);
            scope.ThrowIfUnavailable();
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            Log.Debug(Category, $"读取人物作品失败（{_person?.Name} / {personType ?? "全部"}）：{error.Message}");
            return null;
        }
    }

    private async Task ApplyPersonAsync(EmbyItem person, EmbySessionScope scope, CancellationToken token)
    {
        _detail = person;
        if (person.Id.Length > 0) _personId = person.Id;
        if (person.Name.Length > 0) Name = person.Name;
        BirthLine = PersonDetail.Birth(person, DateOnly.FromDateTime(DateTime.Now));
        DeathLine = PersonDetail.Death(person);
        BirthplaceLine = PersonDetail.Birthplace(person);
        Overview = ItemDetail.Prose(person.Overview);
        OtherFacts = PersonDetail.Facts(person);
        Favorite = person.UserData?.IsFavorite == true;
        FavoriteActionVisibility = Show(scope.Connection.SupportsFavorites);
        EditVisibility = Show(_actions is { } actions && ItemMenu.Allows(ItemCommand.EditMetadata,
            actions.MenuPermissionsFor(scope), scope.Connection.ServerKind, person));
        ToggleFavoriteCommand.NotifyCanExecuteChanged();

        ReplaceCommandCard(person);
        await LoadPortraitAsync(person, scope, token).ConfigureAwait(true);
    }

    private async Task LoadPortraitAsync(EmbyItem person, EmbySessionScope scope, CancellationToken token)
    {
        var tag = person.PrimaryImageTag is { Length: > 0 } fromServer
            ? fromServer
            : _person?.PrimaryImageTag;
        if (tag is not { Length: > 0 } || _personId.Length == 0 || _images is null) return;

        try
        {
            var picture = await _images.GetDecodedAsync<BitmapImage>(
                scope, _personId, EmbyImageStore.Primary, tag, EmbyImageStore.RequestWidth(PortraitWidth),
                bytes => PosterLoader.DecodeAsync(bytes, PortraitWidth), token).ConfigureAwait(true);
            if (IsCurrent(token) && picture is not null) Portrait = picture;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            // 没有照片不是错误：左边那一格继续用占位图，页面上其余的字照常读。
            Log.Debug(Category, $"读取人物头像失败（{person.Name}）：{error.Message}");
        }
    }

    private void ApplyFilmography(IReadOnlyList<ItemsResult?> groups)
    {
        Filmography.Clear();

        foreach (var (type, result) in PersonDetail.CreditTypes.Zip(groups))
        {
            if (result is null || result.Items.Count == 0) continue;

            var total = Math.Max(result.TotalRecordCount, result.Items.Count);
            var group = new PersonCreditGroup
            {
                Type = type,
                Heading = PersonDetail.Heading(type, total),
                Total = total
            };
            foreach (var item in result.Items)
                group.Rows.Add(PersonDetail.Credit(item, _personId, type));

            Filmography.Add(group);
        }
    }

    private void ReplaceCommandCard(EmbyItem item)
    {
        if (_commandCard is { } old) old.PropertyChanged -= OnCommandCardChanged;
        _commandCard = _images is { } images
            ? new CardItem(item, images, CardSize.CastWidth, indicators: false)
            : null;
        if (_commandCard is { } card) card.PropertyChanged += OnCommandCardChanged;
    }

    private void OnCommandCardChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CardItem.IsFavorite) && _commandCard is { } card)
            Favorite = card.IsFavorite;
    }

    private void UpdateVisibility()
    {
        SeriesVisibility = Show(SeriesShelf is { Cards.Count: > 0 });
        MovieVisibility = Show(MovieShelf is { Cards.Count: > 0 });
        EpisodeVisibility = Show(EpisodeShelf is { Cards.Count: > 0 });
        FilmographyVisibility = Show(Filmography.Count > 0);
        OtherVisibility = Show(OtherFacts.Count > 0);
        RetryVisibility = Show(_personFailed || _creditsFailed);

        var hasWorks = SeriesShelf is { Cards.Count: > 0 }
            || MovieShelf is { Cards.Count: > 0 }
            || EpisodeShelf is { Cards.Count: > 0 }
            || Filmography.Count > 0;
        EmptyVisibility = Show(IsReady && !Busy && !hasWorks);
    }

    protected override void BusyChanged() => ToggleFavoriteCommand.NotifyCanExecuteChanged();

    private bool CanFavorite => !Busy && _detail is not null && _personId.Length > 0
        && _scope is { IsAvailable: true, Connection.SupportsFavorites: true };

    [RelayCommand(CanExecute = nameof(CanFavorite))]
    private async Task ToggleFavoriteAsync()
    {
        if (!CanFavorite || _detail is not { } person || _scope is not { } scope) return;

        var favorite = person.UserData?.IsFavorite == true;
        try
        {
            var state = await scope.ExecuteAsync(
                    (client, token) => client.SetFavoriteAsync(_personId, !favorite, token), CancellationToken.None)
                .ConfigureAwait(true);
            if (!scope.IsAvailable || state is null) return;

            person.UserData = state;
            Favorite = state.IsFavorite;
            _commandCard?.ApplyUserState(state, watched: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            _actions?.Notify($"更新收藏失败：{Failure.Describe(error)}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }
    }

    internal void OpenCard(CardItem card) => _actions?.OpenItem(card.Item);

    internal void OpenAll(PersonCreditGroup group) => _actions?.OpenPersonCredits(Subject, group.Type);

    public override Task ReloadAsync() => LoadAsync();

    [RelayCommand]
    private void Retry() => _ = LoadAsync();

    public override void Dispose()
    {
        if (_commandCard is { } card) card.PropertyChanged -= OnCommandCardChanged;
        _commandCard = null;
        base.Dispose();
    }
}
