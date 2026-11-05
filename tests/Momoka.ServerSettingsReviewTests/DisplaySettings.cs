using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;

internal static class DisplaySettings
{
    public static void Run()
    {
        var failures = new List<string>();
        void Check(string name, Action action)
        {
            try { action(); }
            catch (Exception error) { failures.Add(name + ": " + error.Message); }
        }
        foreach (var import in new[] { false, true }) Check("R12 restore/import " + import, () =>
        {
            var live = new AppSettings();
            live.Ui.Display["local-library"] = new() { View = LibraryView.List, CardSize = LibraryCardSize.Large, GroupIntoCollections = false, Fields = [LibraryField.Genres] };
            var original = live.Ui.Display;
            var source = new AppSettings();
            if (import) source.Ui.Display["foreign-library"] = new();
            SettingsPreferences.Apply(live, source);
            if (!ReferenceEquals(original, live.Ui.Display) || live.Ui.Display["local-library"].View != LibraryView.List || live.Ui.Display.Count != 1)
                throw new InvalidOperationException("Local display preferences were overwritten");
            var backup = SettingsPreferences.ToBackupDocument(live);
            if (backup.Ui.Display.Count != 0) throw new InvalidOperationException("Backup contains local library display identity");
        });
        foreach (var display in new[] { "null", "{\"library\":null}", "{\"library\":{\"Fields\":null}}", "{\"library\":{\"Fields\":[]}}" })
            Check("R13 display " + display, () =>
            {
                var paths = new AppPaths(Path.Combine(AppContext.BaseDirectory, "isolated-settings", Guid.NewGuid().ToString("N")));
                Directory.CreateDirectory(paths.Root);
                File.WriteAllText(paths.SettingsFile, "{\"SchemaVersion\":" + AppSettings.CurrentSchemaVersion + ",\"Ui\":{\"Display\":" + display + "}}");
                var settings = new SettingsStore(paths, PassthroughSecretProtector.Instance).Load();
                foreach (var value in settings.Ui.Display.Values)
                {
                    var normalized = value.Normalized();
                    if (display.Contains("[]", StringComparison.Ordinal) && normalized.Fields.Count != 0)
                        throw new InvalidOperationException("Explicit empty field selection was lost");
                }
            });
        foreach (var withBackup in new[] { false, true }) Check("R16 current backup " + withBackup, () =>
        {
            var root = Path.Combine(AppContext.BaseDirectory, "isolated-settings", Guid.NewGuid().ToString("N"));
            var current = new AppPaths(Path.Combine(root, "current"));
            var old = new AppPaths(Path.Combine(root, "old"));
            Directory.CreateDirectory(current.Root); Directory.CreateDirectory(old.Root);
            File.WriteAllText(old.SettingsFile, "{\"SchemaVersion\":2}");
            if (withBackup) File.WriteAllText(current.SettingsBackupFile, "{\"SchemaVersion\":2}");
            if (AppPaths.ResolveProfile([current.Root, old.Root]).Root != (withBackup ? current.Root : old.Root))
                throw new InvalidOperationException("Readonly profile resolution skipped current backup");
            if (File.Exists(current.SettingsFile)) throw new InvalidOperationException("Readonly resolver wrote settings");
        });
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        Console.WriteLine("R12/R13/R16: 8 display preservation, null loading and readonly profile cases passed.");
    }
}
