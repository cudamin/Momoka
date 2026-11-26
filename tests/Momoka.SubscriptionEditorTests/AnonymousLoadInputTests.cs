using Momoka.Shell;

namespace Momoka.Tests;

internal static class AnonymousLoadInputTests
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "momoka-editor-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var output = Path.Combine(root, "artifacts", "subscription-editor-load", "new-run");
        if (output != AnonymousLoadInput.Validate(root, ["--anonymous-editor-load", output]))
            throw new InvalidOperationException("Anonymous output changed");
        foreach (var bad in new[] { "relative", "https://fixture.invalid", @"\\host\share", @"C:relative", Path.Combine(root, "settings") })
            Reject(() => AnonymousLoadInput.Validate(root, ["--anonymous-editor-load", bad]));
        foreach (var args in new[] { Array.Empty<string>(), new[] { "unknown", output }, new[] { "--anonymous-editor-load" }, new[] { "--anonymous-editor-load", output, "extra" } })
            Reject(() => AnonymousLoadInput.Validate(root, args));
        Directory.CreateDirectory(output);
        Reject(() => AnonymousLoadInput.Validate(root, ["--anonymous-editor-load", output]));
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Invalid anonymous input was accepted");
    }
}
