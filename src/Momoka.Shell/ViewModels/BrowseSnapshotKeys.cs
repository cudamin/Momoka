using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// The cache key for one home or library snapshot. Everything that changes what the server would answer,
/// what the page would draw, or which login the answer belongs to goes in here; anything that does not
/// (window width, animation, scroll) stays out so a resize or scroll does not throw the content away.
/// <para>
/// 来源那一半带登录代次的对象哈希：同一账号退出再登录会换一份 <see cref="EmbySessionScope.UserStateOwner"/>，
/// 键随之换掉，旧快照不会被新登录覆盖或误用。仓库读取时还会按对象引用再核对一次，键只负责分槽。
/// </para>
/// </summary>
internal static class BrowseSnapshotKeys
{
    /// <summary>Only online sources participate: offline at capture or now means a different answer.</summary>
    internal static IReadOnlyList<SnapshotSource> Sources(IEnumerable<EmbySessionScope> scopes) =>
        [.. scopes.Where(scope => scope.IsAvailable).Select(SnapshotSource.Of)];

    internal static string Home(
        IReadOnlyList<SnapshotSource> sources,
        IReadOnlyList<EmbyItem> libraryViews,
        IReadOnlyList<(string Key, bool Visible)> plan,
        UiSettings ui,
        bool aggregate)
    {
        var text = new StringBuilder(256);
        AddSources(text, sources);

        foreach (var library in libraryViews)
        {
            Add(text, library.Source?.Key);
            Add(text, library.Id);
            Add(text, library.Name);
            Add(text, library.RecursiveItemCount);
        }

        foreach (var (key, visible) in plan)
        {
            Add(text, key);
            Add(text, visible);
        }

        Add(text, ui.ShowHomeBanner);
        Add(text, ui.CarouselSource.ToString());
        Add(text, ui.CarouselMedia.ToString());
        Add(text, ui.CarouselCount);
        Add(text, aggregate);
        return text.ToString();
    }

    internal static string Library(
        IReadOnlyList<SnapshotSource> sources,
        LibraryRequest request,
        string sortKey,
        bool descending,
        ItemFilters filters,
        FnNasFilterSelection fnNasFilters,
        LibraryDisplay display,
        int pageSize)
    {
        var text = new StringBuilder(320);
        AddSources(text, sources);

        Add(text, request.Title);
        Add(text, request.Tag);
        Add(text, request.ParentId);
        Add(text, request.ParentType);
        Add(text, request.Source?.Key);
        Add(text, request.CollectionType);
        Add(text, request.Section.ToString());
        Add(text, request.BrowseGroup);
        Add(text, request.SearchTerm);
        Add(text, request.SearchCategory.ToString());
        Add(text, request.Genre);
        Add(text, request.PersonId);
        Add(text, request.Row?.ToString());
        Add(text, request.IsFavorites);
        Add(text, request.IsSearch);

        Add(text, sortKey);
        Add(text, descending);
        AddList(text, filters.Toggles);
        AddList(text, filters.Genres);
        AddList(text, filters.OfficialRatings);
        AddList(text, filters.Studios);
        AddList(text, filters.Tags);
        AddList(text, filters.Years);
        foreach (var pair in filters.MediaValues.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Add(text, pair.Key);
            AddList(text, pair.Value);
        }

        Add(text, fnNasFilters.Type);
        Add(text, fnNasFilters.Watched);
        AddList(text, fnNasFilters.Genres);
        AddList(text, fnNasFilters.Resolutions);
        AddList(text, fnNasFilters.ColorRanges);
        AddList(text, fnNasFilters.AudioTypes);
        AddList(text, fnNasFilters.Locations);
        AddList(text, fnNasFilters.Decades);
        AddList(text, fnNasFilters.RecognitionStatuses);

        Add(text, display.View.ToString());
        Add(text, display.CardSize.ToString());
        Add(text, display.GroupIntoCollections);
        foreach (var field in display.Fields) Add(text, field.ToString());

        Add(text, pageSize);
        return text.ToString();
    }

    private static void AddSources(StringBuilder text, IReadOnlyList<SnapshotSource> sources)
    {
        Add(text, sources.Count);
        foreach (var source in sources.OrderBy(source => source.Key, StringComparer.Ordinal))
        {
            Add(text, source.Key);
            Add(text, RuntimeHelpers.GetHashCode(source.Owner).ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void Add(StringBuilder text, string? value)
    {
        if (value is null)
        {
            text.Append("-1|");
            return;
        }

        text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('|');
    }

    private static void Add(StringBuilder text, int? value) => Add(text, value?.ToString(CultureInfo.InvariantCulture));

    private static void Add(StringBuilder text, bool value) => Add(text, value ? "1" : "0");

    private static void AddList(StringBuilder text, IEnumerable<string> values)
    {
        var list = values.ToList();
        Add(text, list.Count);
        foreach (var value in list) Add(text, value);
    }
}
