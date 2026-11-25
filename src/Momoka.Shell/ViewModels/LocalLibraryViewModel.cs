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
    private Func<LocalLibraryGroup, Task>? _open;
    private EventHandler? _changed;
    private string _libraryId = "";
    private bool _active;
    private long _visit;

    /// <summary>墙上的作品卡：同一部作品的已入库文件合到一张。</summary>
    public ObservableCollection<LocalLibraryGroup> Groups { get; } = [];

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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RetryVisibility))]
    public partial bool HasIncomplete { get; set; }
    public Visibility EmptyVisibility => Empty ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RetryVisibility => HasIncomplete && Available ? Visibility.Visible : Visibility.Collapsed;

    internal void Attach(LocalLibraryCoordinator service, string id, ISystemLauncher launcher,
        Func<Action, bool> dispatch, Func<LocalMediaLibraryEntry, Task>? play = null,
        Func<LocalLibraryGroup, Task>? open = null)
    {
        var visit = _visit + 1;
        Cancel();
        if (_visit != visit) return;
        _service = service;
        _launcher = launcher;
        _libraryId = id;
        _play = play;
        _open = open;
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
            if (!ClearGroups(Current)) return;
            Title = "本地媒体库";
            if (!Current()) return;
            SourceDirectory = "";
            if (!Current()) return;
            Status = "这个本地媒体库已移除。";
            if (!Current()) return;
            CountText = "0 项";
            Empty = true;
            HasIncomplete = false;
            IsReady = true;
            return;
        }

        Title = library.Name;
        if (!Current()) return;
        SourceDirectory = library.SourceDirectory;
        if (!Current()) return;
        Status = service.StatusFor(id);
        if (!Current()) return;
        var entries = service.EntriesFor(id).Where(entry => entry.IsInLibrary)
            .OrderByDescending(entry => entry.AddedAt).ThenBy(entry => entry.Id, StringComparer.Ordinal).ToArray();
        if (!Current()) return;
        var wanted = LocalLibraryGrouping.Build(entries);
        var keys = wanted.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        for (var index = Groups.Count - 1; index >= 0; index--)
        {
            var current = Groups[index];
            if (keys.Contains(current.Id) && wanted.First(group => group.Id == current.Id).Stamp == current.Stamp) continue;
            current.ReleasePoster();
            if (!Current()) return;
            Groups.RemoveAt(index);
            if (!Current()) return;
        }
        for (var index = 0; index < wanted.Count; index++)
        {
            var group = wanted[index];
            var existing = Groups.FirstOrDefault(current => current.Id == group.Id);
            LocalLibraryGroup item;
            if (existing is not null && existing.Stamp == group.Stamp)
            {
                existing.RefreshEntries(group.Entries);
                item = existing;
            }
            else item = group;
            item.CanPlay = CanPlay;
            if (!Current()) return;
            var position = Groups.IndexOf(item);
            if (position < 0) Groups.Insert(index, item);
            else if (position != index) Groups.Move(position, index);
            if (!Current()) return;
        }
        CountText = $"{entries.Length} 项";
        if (!Current()) return;
        Empty = Groups.Count == 0;
        if (!Current()) return;
        HasIncomplete = entries.Any(entry => !entry.Complete);
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

    /// <summary>重试本库未完成的附属文件：只补缺失项，已写好的同作品元数据复用不覆盖。</summary>
    public async Task RetryIncompleteAsync()
    {
        if (!_active || Busy || !Available || _service is not { } service) return;
        var id = _libraryId;
        var visit = _visit;
        var token = BeginLoad();
        if (!Current()) return;
        try
        {
            await service.RetryIncompleteAsync(id).WaitAsync(token);
            if (Current()) Sync();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (Current()) Report("无法重试未完成项", error); }
        finally { if (Current()) EndLoad(token); }
        bool Current() => _active && visit == _visit && ReferenceEquals(service, _service) && IsCurrent(token);
    }

    /// <summary>卡上的已看：按作品分组落进本机索引，剧集里每一集一起改，不向任何服务器上报。</summary>
    internal Task ToggleWatchedAsync(LocalLibraryGroup group)
    {
        if (!_active || !Available || _service is not { } service || !Groups.Contains(group)) return Task.CompletedTask;
        var watched = !group.Watched;
        try
        {
            if (!service.SetLocalUserState(_libraryId, LocalMediaEntryArtwork.GroupKey(group.Entries[0]), watched, null))
                return Task.CompletedTask;
            group.SetUserState(watched, null);
        }
        catch (Exception error) { Report("无法保存观看状态", error); }
        return Task.CompletedTask;
    }

    /// <summary>卡上的收藏：同上，一件作品一条状态。</summary>
    internal Task ToggleFavoriteAsync(LocalLibraryGroup group)
    {
        if (!_active || !Available || _service is not { } service || !Groups.Contains(group)) return Task.CompletedTask;
        var favorite = !group.Favorite;
        try
        {
            if (!service.SetLocalUserState(_libraryId, LocalMediaEntryArtwork.GroupKey(group.Entries[0]), null, favorite))
                return Task.CompletedTask;
            group.SetUserState(null, favorite);
        }
        catch (Exception error) { Report("无法保存收藏状态", error); }
        return Task.CompletedTask;
    }

    internal void OpenSourceDirectory()
    {
        if (!_active || !Available || _launcher is null) return;
        OpenFolder(SourceDirectory);
    }

    internal void OpenLocation(LocalLibraryGroup group)
    {
        if (!_active || !Available || _launcher is null || !Groups.Contains(group)) return;
        try { OpenFolder(Path.GetDirectoryName(group.Entries[0].TargetPath) ?? ""); }
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

    /// <summary>点开一张作品卡：交给外壳走现有的详情页，不在本页另造一套详情。</summary>
    internal async Task OpenAsync(LocalLibraryGroup group)
    {
        if (!_active || !Available || _open is null || !Groups.Contains(group)) return;
        var visit = _visit;
        try { await _open(group); }
        catch (Exception error) { if (_active && visit == _visit) Report("无法打开媒体详情", error); }
    }

    /// <summary>直接播放这张作品卡的第一集/电影（详情页不可用时的兜底入口）。</summary>
    internal async Task PlayAsync(LocalLibraryGroup group)
    {
        if (!_active || !Available || _play is null || !Groups.Contains(group)) return;
        var visit = _visit;
        try { await _play(group.Entries[0]); }
        catch (Exception error) { if (_active && visit == _visit) Report("无法播放本地文件", error); }
    }

    private bool ClearGroups(Func<bool> current)
    {
        foreach (var group in Groups.ToArray())
        {
            group.ReleasePoster();
            if (!current()) return false;
        }
        Groups.Clear();
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
        _open = null;
        CanPlay = false;
        if (visit != _visit) return;
        foreach (var group in Groups.ToArray())
        {
            group.CanPlay = false;
            if (visit != _visit) return;
            group.ReleasePoster();
            if (visit != _visit) return;
        }
        base.Cancel();
    }

    public override void Dispose() { Cancel(); base.Dispose(); }
}
