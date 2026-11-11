using Momoka.Plex;

namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    public Task<PlexLibraryCatalog> GetPlexLibraryCatalogAsync(string section, CancellationToken ct, bool refresh = false) =>
        Connection.IsPlex ? PlexMedia.GetLibraryCatalogAsync(section, ct, refresh) : throw new NotSupportedException("当前连接不是 Plex");
    public Task<IReadOnlyList<PlexFilterOption>> GetPlexLibraryFilterOptionsAsync(string section, string type, string field,
        PlexFilterSelection selection, CancellationToken ct) => Connection.IsPlex
        ? PlexMedia.GetLibraryFilterOptionsAsync(section, type, field, selection, ct) : throw new NotSupportedException("当前连接不是 Plex");
    public Task<IReadOnlyList<PlexFirstCharacter>> GetPlexLibraryFirstCharactersAsync(string section,
        PlexFilterSelection selection, CancellationToken ct) => Connection.IsPlex
        ? PlexMedia.GetLibraryFirstCharactersAsync(section, selection, ct) : throw new NotSupportedException("当前连接不是 Plex");
}
