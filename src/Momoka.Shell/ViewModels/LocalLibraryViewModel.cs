using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Momoka.LocalMedia;
using Momoka.Shell.Platform;
using Momoka.Shell.Services;

namespace Momoka.Shell.ViewModels;

public sealed partial class LocalLibraryViewModel : PageViewModel
{
    private LocalLibraryCoordinator? _service;
    private ISystemLauncher? _launcher;
    private Func<LocalMediaLibraryEntry, Task>? _play;
    private EventHandler? _changed;
    private string _libraryId = "";
    private bool _active;
    private long _visit;

    public ObservableCollection<LocalLibraryRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = "本地媒体库";
    [ObservableProperty]
    public partial string SourceDirectory { get; set; } = "";
    [ObservableProperty]
    public partial string Status { get; set; } = "";
    [ObservableProperty]
    public partial string CountText { get; set; } = "0 项";
    [ObservableProperty]
    public partial bool Available { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    public partial bool Empty { get; set; } = true;
    [ObservableProperty]
    public partial bool CanPlay { get; set; }
    public Visibility EmptyVisibility => Empty ? Visibility.Visible : Visibility.Collapsed;

    internal void Attach(LocalLibraryCoordinator service, string id, ISystemLauncher launcher,
        Func<Action, bool> dispatch, Func<LocalMediaLibraryEntry, Task>? play = null)
    {
        var visit = _visit + 1;
        Cancel();
        if (_visit != visit) return;
        _service = service;
        _launcher = launcher;
        _libraryId = id;
        _play = play;
        _active = true;
        _changed = (_, _) => dispatch(() =>
        {
            if (_active && visit == _visit && ReferenceEquals(service, _service)) Sync();
        });
        service.Changed += _changed;
        Sync();
    }

    private void Sync()
    {
        if (!_active || _service is not { } service) return;
        var visit = _visit;
        var id = _libraryId;
        var library = service.Libraries.FirstOrDefault(library => library.Id == id && !string.IsNullOrWhiteSpace(library.SourceDirectory));
        if (!Current()) return;
        Available = library is not null;
        if (!Current()) return;
        CanPlay = Available && _play is not null;
        if (!Current()) return;
        if (library is null)
        {
            if (!ClearRows(Current)) return;
            Title = "本地媒体库";
            if (!Current()) return;
            SourceDirectory = "";
            if (!Current()) return;
            Status = "这个本地媒体库已移除。";
            if (!Current()) return;
        }
        else
        {
            Title = library.Name;
            if (!Current()) return;
            SourceDirectory = library.SourceDirectory;
            if (!Current()) return;
            Status = service.StatusFor(id);
            if (!Current()) return;
            var entries = service.EntriesFor(id).Where(entry => entry.IsInLibrary)
                .OrderByDescending(entry => entry.AddedAt).ThenBy(entry => entry.Id, StringComparer.Ordinal).ToArray();
            if (!Current()) return;
            var ids = entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
            for (var index = Rows.Count - 1; index >= 0; index--)
            {
                if (ids.Contains(Rows[index].Id)) continue;
                Rows[index].ReleasePoster();
                if (!Current()) return;
                Rows.RemoveAt(index);
                if (!Current()) return;
            }
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                var current = Rows.FirstOrDefault(row => row.Id == entry.Id);
                if (current is not null && !Equals(current.Entry, entry))
                {
                    current.ReleasePoster();
                    if (!Current()) return;
                    Rows.Remove(current);
                    if (!Current()) return;
                    current = null;
                }
                current ??= new LocalLibraryRow(entry);
                current.CanPlay = CanPlay;
                if (!Current()) return;
                var position = Rows.IndexOf(current);
                if (position < 0) Rows.Insert(index, current);
                else if (position != index) Rows.Move(position, index);
                if (!Current()) return;
            }
        }
        CountText = $"{Rows.Count} 项";
        if (!Current()) return;
        Empty = Rows.Count == 0;
        if (!Current()) return;
        IsReady = true;
        bool Current() => _active && visit == _visit && ReferenceEquals(service, _service) && id == _libraryId;
    }

    public override async Task ReloadAsync()
    {
        if (!_active || Busy || !Available || _service is not { } service) return;
        var id = _libraryId;
        var visit = _visit;
        var token = BeginLoad();
        if (!Current()) return;
        try
        {
            await service.RefreshAsync(id).WaitAsync(token);
            if (Current()) Sync();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (Current()) Report("无法刷新本地媒体库", error); }
        finally { if (Current()) EndLoad(token); }
        bool Current() => _active && visit == _visit && ReferenceEquals(service, _service) && IsCurrent(token);
    }

    internal void OpenSourceDirectory()
    {
        if (!_active || !Available || _launcher is null) return;
        OpenFolder(SourceDirectory);
    }

    internal void OpenLocation(LocalLibraryRow row)
    {
        if (!_active || !Available || _launcher is null || !Rows.Contains(row)) return;
        try { OpenFolder(Path.GetDirectoryName(row.TargetPath) ?? ""); }
        catch (Exception error) { Report("无法打开文件夹", error); }
    }

    private void OpenFolder(string path)
    {
        try
        {
            // launcher 会创建目录；媒体库浏览不能创建已丢失或被移除的路径。
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("文件夹已不存在，请刷新媒体库。");
            _launcher!.OpenFolder(path);
        }
        catch (Exception error) { Report("无法打开文件夹", error); }
    }

    internal async Task PlayAsync(LocalLibraryRow row)
    {
        if (!_active || !Available || _play is null || !Rows.Contains(row)) return;
        var visit = _visit;
        try { await _play(row.Entry); }
        catch (Exception error) { if (_active && visit == _visit) Report("无法播放本地文件", error); }
    }

    private bool ClearRows(Func<bool> current)
    {
        foreach (var row in Rows.ToArray())
        {
            row.ReleasePoster();
            if (!current()) return false;
        }
        Rows.Clear();
        return current();
    }

    public override void Cancel()
    {
        _active = false;
        var visit = ++_visit;
        if (_service is { } service && _changed is not null) service.Changed -= _changed;
        _changed = null;
        _service = null;
        _launcher = null;
        _play = null;
        CanPlay = false;
        if (visit != _visit) return;
        foreach (var row in Rows.ToArray())
        {
            row.CanPlay = false;
            if (visit != _visit) return;
            row.ReleasePoster();
            if (visit != _visit) return;
        }
        base.Cancel();
    }

    public override void Dispose() { Cancel(); base.Dispose(); }
}
