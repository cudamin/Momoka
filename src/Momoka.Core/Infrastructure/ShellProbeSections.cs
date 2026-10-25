namespace Momoka.Infrastructure;

/// <summary>
/// The areas selectable with <c>--probe-shell --probe-section</c>. Names are part of the CLI
/// contract and are matched case-insensitively; the probe maps every case to one of them.
/// </summary>
public static class ShellProbeSections
{
    public const string Shell = "shell";
    public const string Settings = "settings";
    public const string Shortcuts = "shortcuts";
    public const string Shaders = "shaders";
    public const string Library = "library";
    public const string Search = "search";
    public const string MoviePilot = "moviepilot";
    public const string AniBt = "anibt";
    public const string Accounts = "accounts";
    public const string Covers = "covers";
    public const string Favorites = "favorites";
    public const string Playback = "playback";

    public static readonly IReadOnlyList<string> All =
    [
        Shell, Settings, Shortcuts, Shaders, Library, Search, MoviePilot, AniBt, Accounts, Covers, Favorites, Playback,
    ];

    public static bool IsKnown(string name) =>
        All.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static string Usage => string.Join(", ", All);
}
