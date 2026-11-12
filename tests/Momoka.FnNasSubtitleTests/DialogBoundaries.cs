using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.FnNas;

namespace Momoka.Shell.Views;

// Only generated fields and terminal presentation/file-picker boundaries are substituted.
// All dialog decisions and async event handlers compile directly from the production file.
public sealed partial class FnNasSubtitleDialog
{
    internal FrameworkElement DialogTitleRow { get; } = new();
    internal TextBlock DialogTitleText { get; } = new();
    internal Button HeaderCloseButton { get; } = new();
    internal FrameworkElement SubtitleBody { get; } = new();
    internal FrameworkElement SubtitleScroll { get; } = new();
    internal ComboBox VersionBox { get; } = new();
    internal ComboBox LanguageBox { get; } = new();
    internal TextBlock SourceFileText { get; } = new();
    internal Button RefreshTracksButton { get; } = new();
    internal ItemsControl Tracks { get; } = new();
    internal ItemsControl Results { get; } = new();
    internal FrameworkElement NoTracks { get; } = new();
    internal FrameworkElement NoSearchResults { get; } = new();
    internal FrameworkElement ManagementArea { get; } = new();
    internal FrameworkElement SearchArea { get; } = new();
    internal FrameworkElement ServerFileArea { get; } = new();
    internal Button SearchButton { get; } = new();
    internal Button UploadButton { get; } = new();
    internal TextBox ServerPathBox { get; } = new();
    internal Button MarkServerFileButton { get; } = new();
    internal FrameworkElement DeleteConfirmation { get; } = new();
    internal TextBlock DeleteConfirmationText { get; } = new();
    internal Button ConfirmDeleteButton { get; } = new();
    internal Button CancelDeleteButton { get; } = new();
    internal TextBlock Status { get; } = new();
    internal ProgressRing Busy { get; } = new();
    private void InitializeComponent() { }

    internal bool FixtureBusy => _busy;
    internal bool FixtureClosed => _closed;
    internal IReadOnlyList<FnNasSubtitleRow> FixtureTracks => _tracks;
    internal IReadOnlyList<FnNasSubtitleResultRow> FixtureResults => _results;
    internal FnNasSubtitles FixtureService => _service!;
    internal void FixtureRefresh() => OnRefreshTracks(RefreshTracksButton, new());
    internal void FixtureSearch() => OnSearch(SearchButton, new());
    internal void FixtureUpload() => OnUpload(UploadButton, new());
    internal void FixtureDownload() => OnDownload(new Button { DataContext = _results[0] }, new());
    internal void FixtureDelete() => OnDelete(new Button { DataContext = _tracks.First(row => row.Track.CanDelete) }, new());
    internal void FixtureCancelDelete() => OnCancelDelete(CancelDeleteButton, new());
    internal void FixtureConfirmDelete() => OnConfirmDelete(ConfirmDeleteButton, new());
    internal void FixtureVersion(int index)
    {
        VersionBox.SelectedIndex = index;
        OnVersionChanged(VersionBox, new());
    }
}

internal static class FnNasSubtitleFile
{
    internal static Task<FnNasSubtitleUpload?> PickAsync(XamlRoot root, Func<bool> isCurrent) =>
        throw new InvalidOperationException("匿名专项不得打开系统文件选择器");
}
