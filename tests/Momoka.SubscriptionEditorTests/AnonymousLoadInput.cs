namespace Momoka.Shell;

// Shared by the test-only apphost and ordinary offline input tests; no UI or profile access.
internal static class AnonymousLoadInput
{
    internal static string Validate(string root, string[] args)
    {
        if (args is not ["--anonymous-editor-load", var output])
            throw new ArgumentException("Explicit anonymous editor-load output required");
        if (output.Length < 3 || !char.IsAsciiLetter(output[0]) || output[1] != ':' || output[2] is not ('\\' or '/')
            || output.IndexOf(':', 2) >= 0 || !Path.IsPathFullyQualified(output))
            throw new ArgumentException("Absolute local path required");
        output = Path.GetFullPath(output);
        var prefix = Path.Combine(Path.GetFullPath(root), "artifacts", "subscription-editor-load") + Path.DirectorySeparatorChar;
        if (!output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || Path.Exists(output))
            throw new ArgumentException("New worktree anonymous directory required");
        if (new DriveInfo(Path.GetPathRoot(output)!).DriveType != DriveType.Fixed)
            throw new ArgumentException("Fixed local drive required");
        for (var current = output; current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Reparse paths prohibited");
        return output;
    }
}
