using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;

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
    internal ComboBox VersionBox { get; } = new();
    internal FrameworkElement VersionRow { get; } = new() { Visibility = Visibility.Collapsed };
    internal FrameworkElement UploadSection { get; } = new() { Visibility = Visibility.Collapsed };
    internal Button PickSubtitleButton { get; } = new();
    internal Button UploadSubtitleButton { get; } = new();
    internal ComboBox UploadLanguageBox { get; } = new();
    internal ToggleSwitch UploadForced { get; } = new();
    internal ToggleSwitch UploadHearingImpaired { get; } = new();
    internal TextBlock UploadFileText { get; } = new();
    internal FrameworkElement UploadDraft { get; } = new() { Visibility = Visibility.Collapsed };
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
    // 标题行（文字 + 右上角关闭钮）是 2026-10-29 那批搬进 XAML 的；夹具只编译 .xaml.cs，字段在这里补上。
    internal FrameworkElement DialogTitleRow { get; } = new();
    internal TextBlock DialogTitleText { get; } = new();
    internal Button HeaderCloseButton { get; } = new();
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
    internal void FixtureSearch() => OnSearch(SearchButton, new());
    internal IReadOnlyList<SubtitleCandidate> FixtureCandidates => _results;
    internal void FixtureVersion(int index)
    {
        VersionBox.SelectedIndex = index;
        InvokeFixture("OnVersionChanged", VersionBox, new SelectionChangedEventArgs());
    }
    internal void FixturePick() => InvokeFixture("OnPickUpload", PickSubtitleButton, new RoutedEventArgs());
    internal Task FixtureUpload() => (Task)InvokeFixture("UploadSelectedAsync")!;
    private object? InvokeFixture(string name, params object[] args)
    {
        var method = typeof(SubtitleDialog).GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Momoka.Tests.Assert.True(method is not null, "Missing production " + name);
        try { return method!.Invoke(this, args); }
        catch (System.Reflection.TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException!).Throw(); throw; }
    }
}
