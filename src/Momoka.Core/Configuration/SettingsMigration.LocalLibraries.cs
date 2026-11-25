namespace Momoka.Configuration;

public static partial class LocalLibrarySettingsMigration
{
    public static void Normalize(LocalMediaSettings settings)
    {
        settings.Libraries ??= [];
        settings.Libraries.RemoveAll(library => library is null);
        if (!settings.LibrariesMigrated)
        {
            if (settings.Libraries.Count == 0 && !string.IsNullOrWhiteSpace(settings.SourceDirectory))
                settings.Libraries.Add(new()
                {
                    Name = Path.GetFileName(settings.SourceDirectory.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : "本地媒体库",
                    SourceDirectory = settings.SourceDirectory,
                    Recursive = settings.Recursive
                });
            settings.LibrariesMigrated = true;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var library in settings.Libraries)
        {
            if (string.IsNullOrWhiteSpace(library.Id) || !ids.Add(library.Id))
            {
                library.Id = Guid.NewGuid().ToString("N");
                ids.Add(library.Id);
                library.MonitorEnabled = false;
            }
            library.Name = string.IsNullOrWhiteSpace(library.Name) ? "本地媒体库" : library.Name.Trim();
            library.SourceDirectory ??= "";
        }
        if (settings.SelectedLibraryId is null || !ids.Contains(settings.SelectedLibraryId))
            settings.SelectedLibraryId = settings.Libraries.FirstOrDefault()?.Id ?? "";
    }
}
