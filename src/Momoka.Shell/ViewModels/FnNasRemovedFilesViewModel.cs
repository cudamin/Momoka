using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Shell.Services;

namespace Momoka.Shell.ViewModels;

/// <summary>精简恢复目录：只展示可进入的目录和已移除项，确认写入与只读刷新各自完成。</summary>
public sealed partial class FnNasRemovedFilesViewModel : FnNasAdminViewModel
{
    private FnNasLibraryRemovalService? _service;
    private string _libraryGuid = "";
    private readonly Stack<(string? Guid, string Path)> _parents = new();
    private readonly HashSet<string> _restoring = new(StringComparer.Ordinal);
    public ObservableCollection<FnNasRemovedFile> Entries { get; } = [];
    public string LibraryName { get; private set; } = "";
    public string? FolderGuid { get; private set; }
    [ObservableProperty] public partial string CurrentPath { get; set; } = "媒体库根目录";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestore))]
    public partial FnNasRemovedFile? Selected { get; set; }
    public bool CanRestore => CanUse && Selected is { CanRestore: true };
    public bool CanGoBack => CanUse && _parents.Count > 0;
    public Visibility BackVisibility => Show(_parents.Count > 0);
    public Visibility EmptyVisibility => Show(IsReady && Entries.Count == 0);
    public string EmptyText => "此目录没有已移除项目。打开文件夹可查看里面的已移除文件。";

    internal void AttachRemoval(EmbySession session, string sourceKey, FnNasLibraryRemovalService service, string libraryGuid, string libraryName)
    {
        Attach(session, sourceKey);
        _service = service;
        _libraryGuid = libraryGuid;
        LibraryName = libraryName;
    }

    protected override void ResetEditor()
    {
        Entries.Clear();
        Selected = null;
        FolderGuid = null;
        _parents.Clear();
        _restoring.Clear();
    }

    public override Task ReloadAsync() => RunAsync("读取已移除文件失败", ReadCurrentAsync);

    private async Task ReadCurrentAsync(EmbySessionScope scope, CancellationToken token)
    {
        if (_service is null) return;
        var result = await _service.ReadAsync(scope, _libraryGuid, FolderGuid, token).ConfigureAwait(true);
        if (!Current(token)) return;
        ApplyDirectory(result);
    }

    private void ApplyDirectory(FnNasRemovedFileDirectory result)
    {
        // A successful restore may be followed by stale server rows while a background scan runs.
        // Never offer an already-confirmed path as the same retryable command again.
        Entries.Clear();
        foreach (var entry in result.Entries) Entries.Add(entry);
        CurrentPath = result.Path.Length > 0 ? result.Path : "媒体库根目录";
        Selected = null;
        OnPropertyChanged(nameof(EmptyVisibility));
        BusyChanged();
    }

    internal Task OpenAsync(FnNasRemovedFile entry)
    {
        if (!CanUse || !Entries.Contains(entry) || !entry.CanBrowse) return Task.CompletedTask;
        return RunAsync("读取文件夹失败", async (scope, token) =>
        {
            if (_service is null) return;
            var result = await _service.ReadAsync(scope, _libraryGuid, entry.Guid, token).ConfigureAwait(true);
            if (!Current(token)) return;
            _parents.Push((FolderGuid, CurrentPath));
            FolderGuid = entry.Guid;
            ApplyDirectory(result);
            OnPropertyChanged(nameof(BackVisibility));
        });
    }

    internal Task BackAsync()
    {
        if (!CanGoBack || !_parents.TryPeek(out var parent)) return Task.CompletedTask;
        return RunAsync("读取上级文件夹失败", async (scope, token) =>
        {
            if (_service is null) return;
            var result = await _service.ReadAsync(scope, _libraryGuid, parent.Guid, token).ConfigureAwait(true);
            if (!Current(token)) return;
            _parents.Pop();
            FolderGuid = parent.Guid;
            ApplyDirectory(result);
            OnPropertyChanged(nameof(BackVisibility));
        });
    }

    internal Task RestoreSelectedAsync(bool confirmed)
    {
        if (!confirmed || !CanRestore || Selected is not { } selected || !Entries.Contains(selected)) return Task.CompletedTask;
        return RunAsync("恢复已移除文件失败", async (scope, token) =>
        {
            if (_service is null || !_restoring.Add(selected.Path)) return;
            try
            {
                await _service.RestoreAsync(scope, _libraryGuid, selected.Path, token).ConfigureAwait(true);
                Entries.Remove(selected);
                Selected = null;
                OnPropertyChanged(nameof(EmptyVisibility));
                if (!Current(token)) return;
                try
                {
                    await ReadCurrentAsync(scope, token).ConfigureAwait(true);
                    if (Current(token)) Notify("已提交恢复", "已恢复，请从媒体库重新打开；索引更新可能稍有延迟。", InfoBarSeverity.Success);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    if (Current(token)) Notify("已提交恢复，但刷新失败", Momoka.Diagnostics.Failure.Describe(error)
                        + "。可点刷新重新读取；恢复请求不会重复发送。", InfoBarSeverity.Warning);
                }
            }
            finally
            {
                _restoring.Remove(selected.Path);
                OnPropertyChanged(nameof(CanRestore));
                OnPropertyChanged(nameof(EmptyVisibility));
            }
        });
    }

    protected override void BusyChanged()
    {
        base.BusyChanged();
        OnPropertyChanged(nameof(CanRestore));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(EmptyVisibility));
    }
}
