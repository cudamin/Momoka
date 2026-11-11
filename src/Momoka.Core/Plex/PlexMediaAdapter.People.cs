using System.Globalization;
using System.Text.Json.Nodes;
using Momoka.Emby;

namespace Momoka.Plex;

public sealed partial class PlexMediaAdapter
{
    private static void AddBrowseRelations(EmbyItem item, JsonObject value)
    {
        // Music keeps its own genre metadata; its section must never become a type=1 video query.
        if (item.Type is not (EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season
            or EmbyItemType.Episode or EmbyItemType.Video)) return;
        var section = NativeId(value, "librarySectionID");
        item.PlexSectionId = section;
        // Episodes and seasons belong to a show library; local tag discovery lists shows once.
        var type = item.Type is EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Episode ? 2 : 1;
        foreach (var (element, field, creditType) in new[]
        {
            ("Role", "actor", "Actor"), ("Director", "director", "Director"), ("Writer", "writer", "Writer")
        })
            foreach (var tag in Elements(value, element))
            {
                var name = Blank(Text(tag, "tag"));
                if (name is null) continue;
                var id = NativeId(tag, "id");
                var relation = section is not null && id is not null
                    ? new PlexBrowseRelation(section, type, field, id) : null;
                item.People.Add(new EmbyPerson
                {
                    Id = relation?.Identity ?? "",
                    Name = name,
                    Role = Blank(Text(tag, "role")),
                    Type = creditType,
                    PrimaryImageTag = Blank(Text(tag, "thumb")),
                    PlexRelation = relation,
                    DisplayWithoutIdentity = true
                });
            }

        item.Genres = [];
        foreach (var genre in Elements(value, "Genre"))
        {
            var name = Blank(Text(genre, "tag"));
            if (name is null) continue;
            if (!item.Genres.Contains(name, StringComparer.Ordinal)) item.Genres.Add(name);
            if (section is not null && NativeId(genre, "id") is { } id)
                item.PlexGenres.TryAdd(name, new PlexBrowseRelation(section, type, "genre", id));
        }
    }

    private static string? NativeId(JsonObject value, string key)
    {
        if (value[key] is not JsonValue scalar) return null;
        var text = scalar.TryGetValue<string>(out var stringValue) ? stringValue
            : scalar.TryGetValue<long>(out var number) ? number.ToString(CultureInfo.InvariantCulture) : null;
        return PlexBrowseRelation.IsNativeId(text) ? text : null;
    }
}
