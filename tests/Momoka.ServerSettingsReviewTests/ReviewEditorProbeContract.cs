internal static class ReviewEditorProbeContract
{
    public static void Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Momoka.sln"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Cannot locate current probe sources");
        var registration = File.ReadAllText(Path.Combine(root.FullName, "src/Momoka.Shell/ShellNavigationProbe.cs"));
        foreach (var method in new[] { "ReviewMetadataEditorAsync", "ReviewNotificationEditorAsync" })
            if (!registration.Split('\n').Any(line => line.Contains(method + ",", StringComparison.Ordinal)
                && line.Contains("section: ShellProbeSections.Metadata", StringComparison.Ordinal)))
                throw new InvalidOperationException("Review editor probe is not registered in metadata: " + method);
        var source = File.ReadAllText(Path.Combine(root.FullName, "src/Momoka.Shell/ShellNavigationProbe.ReviewEditors.cs"));
        foreach (var forbidden in new[] { "EmbySession", "HttpClient", "CreateFixture(", "TestNotificationAsync(", "UpdateItemAsync(", "SaveNotificationAsync(" })
            if (source.Contains(forbidden, StringComparison.Ordinal)) throw new InvalidOperationException("Anonymous editor probe acquired a server boundary: " + forbidden);
        foreach (var required in new[] { "new MetadataDialog(", "new NotificationEditDialog(", "new NotificationEditorContext(", "CheckBoxAutomationPeer", "MetadataInvoke(", "new[] { 1100, 760 }", "review-metadata-", "review-notification-" })
            if (!source.Contains(required, StringComparison.Ordinal)) throw new InvalidOperationException("Review editor probe lost an actual control/input/width boundary: " + required);
        Console.WriteLine("Review editor probes: metadata registration, anonymous delegates and actual-control boundaries passed (no desktop execution).");
    }
}
