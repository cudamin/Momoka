using System.Text.Json.Nodes;

namespace Momoka.Plex;

public sealed partial class PlexClient
{
    public Task<JsonObject> GetSectionDetailsAsync(PlexConnection c, string section, CancellationToken ct = default) =>
        RootAsync(PlexEndpoint.Api(c.ApiBase, "library/sections/" + PlexEndpoint.Segment(section)
            + Query([new("includeDetails", "1"), new("includeAdvanced", "1")])), c.Call, ct);

    public Task<JsonObject> GetSectionFilterOptionsAsync(PlexConnection c, string section, string field,
        IReadOnlyList<PlexParameter> parameters, CancellationToken ct = default) =>
        RootAsync(PlexEndpoint.Api(c.ApiBase, "library/sections/" + PlexEndpoint.Segment(section) + "/"
            + PlexEndpoint.Segment(field) + Query(parameters)), c.Call, ct);

    public Task<JsonObject> GetSectionFirstCharactersAsync(PlexConnection c, string section,
        IReadOnlyList<PlexParameter> parameters, CancellationToken ct = default) =>
        RootAsync(PlexEndpoint.Api(c.ApiBase, "library/sections/" + PlexEndpoint.Segment(section) + "/firstCharacter"
            + Query(parameters)), c.Call, ct);
}
