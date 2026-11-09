using System.Globalization;
using System.Text.Json;

namespace Momoka.Plex;

/// <summary>Plex Web's notifications envelope. Source versions and local filtering are recorded in docs/媒体库实时更新.md.</summary>
internal static class PlexRealtimeProtocol
{
    internal static bool IsLibraryChanged(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            JsonElement container;
            var legacy = Text(root, "_elementType") == "NotificationContainer";
            if (legacy) container = root;
            else if (!root.TryGetProperty("NotificationContainer", out container) || container.ValueKind != JsonValueKind.Object) return false;
            var type = Text(container, "type");
            if (type is not ("timeline" or "status")) return false;
            var name = legacy ? "_children" : type == "timeline" ? "TimelineEntry" : "StatusNotification";
            if (!container.TryGetProperty(name, out var entries) || entries.ValueKind != JsonValueKind.Array) return false;
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                if (type == "status")
                {
                    if (Text(entry, "notificationName") == "LIBRARY_UPDATE") return true;
                    continue;
                }
                // Ignore DVR guide traffic and processing progress. State 5 also wakes reconciliation
                // after metadata settles; it does not itself prove a new item belongs on the home page.
                if (Text(entry, "identifier") == "com.plexapp.plugins.library"
                    && PositiveId(entry, "itemID") && PositiveId(entry, "sectionID")
                    && entry.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.Number
                    && state.TryGetInt32(out var value) && value is 0 or 5 or 9) return true;
            }
        }
        catch (JsonException) { }
        return false;
    }

    private static string? Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private static bool PositiveId(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var field)) return false;
        return field.ValueKind switch
        {
            JsonValueKind.Number => field.TryGetInt64(out var id) && id > 0,
            JsonValueKind.String => long.TryParse(field.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0,
            _ => false
        };
    }
}
