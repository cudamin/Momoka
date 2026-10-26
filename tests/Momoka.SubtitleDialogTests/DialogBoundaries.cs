using Momoka.Emby;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

// The linked file contains an existing offline probe entry. Never start that probe or App.
internal sealed class App
{
    internal static App? Instance => null;
    internal bool SubtitleProbeActive => false;
}
internal static class ThemeHost
{
    internal static ThemeBoundary Current { get; } = new();
    internal sealed class ThemeBoundary { internal bool IsDark => true; }
}

public sealed partial class SubtitleDialog
{
    internal Button SearchButton { get; } = new();
    internal Button RefreshTracksButton { get; } = new();
    internal Button ConfirmDeleteButton { get; } = new();
    internal Button CancelDeleteButton { get; } = new();
    internal FrameworkElement DeleteConfirmation { get; } = new() { Visibility = Visibility.Collapsed };
    internal TextBlock DeleteConfirmationText { get; } = new();
    internal ComboBox LanguageBox { get; } = new();
    internal ItemsControl Tracks { get; } = new();
    internal ItemsControl Results { get; } = new();
    internal FrameworkElement NoTracks { get; } = new();
    internal FrameworkElement NoSearchResults { get; } = new();
    internal FrameworkElement ForcedOnlyRow { get; } = new();
    internal ToggleSwitch ForcedOnly { get; } = new();
    internal FrameworkElement SubtitleBody { get; } = new();
    internal FrameworkElement SubtitleScroll { get; } = new();
    internal TextBlock SourceFileText { get; } = new();
    internal TextBlock Status { get; } = new();
    internal ProgressRing Busy { get; } = new();
    private void InitializeComponent() { }

    internal bool FixtureBusy => _busy;
    internal bool FixtureStale => _tracksStale;
    internal bool FixtureClosed => _closed;
    internal IReadOnlyList<SubtitleTrack> FixtureTracks => _tracks;
    internal void FixtureDownload(string id = "candidate") => OnDownload(
        new Button { DataContext = new SubtitleCandidate(new RemoteSubtitleInfo { Id = id, Name = id }) }, new());
    internal void FixtureAskDelete() => OnDelete(new Button { DataContext = _tracks[0] }, new());
    internal void FixtureCancelDelete() => OnCancelDelete(CancelDeleteButton, new());
    internal Task FixtureConfirmDelete() => DeleteConfirmedAsync();
    internal void FixtureRefresh() => OnRefreshTracks(RefreshTracksButton, new());
}
