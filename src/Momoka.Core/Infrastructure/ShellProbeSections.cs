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
    public const string Svp = "svp";
    public const string Library = "library";
    public const string Search = "search";
    public const string MoviePilot = "moviepilot";
    public const string AniBt = "anibt";
    public const string Accounts = "accounts";
    public const string Covers = "covers";
    public const string Favorites = "favorites";
    public const string Playback = "playback";
    public const string Metadata = "metadata";
    public const string Performance = "performance";
    public const string Materials = "materials";
    public const string PlexState = "plex-state";
    public const string JellyfinLatest = "jellyfin-latest";
    /// <summary>手动离线试用，不属于自动全量探针，也不计自动检查通过数。</summary>
    public const string SearchPreview = "search-preview";

    public static readonly IReadOnlyList<string> All =
    [
        Shell, Settings, Shortcuts, Shaders, Svp, Library, Search, MoviePilot, AniBt, Accounts, Covers, Favorites, Playback, Metadata, Performance, Materials, PlexState, JellyfinLatest,
    ];

    public static bool IsKnown(string name) =>
        All.Contains(name, StringComparer.OrdinalIgnoreCase)
        || name.Equals(SearchPreview, StringComparison.OrdinalIgnoreCase);

    public static string Usage => string.Join(", ", All) + $", {SearchPreview}（仅独立手动试用）";
}
