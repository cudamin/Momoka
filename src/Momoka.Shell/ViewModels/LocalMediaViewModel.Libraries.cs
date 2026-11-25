using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.Shell.Services;

namespace Momoka.Shell.ViewModels;

public sealed partial class LocalMediaViewModel
{
    private LocalLibraryCoordinator? _coordinator;

    public ObservableCollection<LocalMediaLibraryRow> Libraries { get; } = [];

    /// <summary>界面上的元数据目录；留空时实际使用 <see cref="DefaultMetadataDirectory"/>。</summary>
    [ObservableProperty]
    public partial string MetadataDirectory { get; set; } = "";

    [ObservableProperty]
    public partial string MetadataStatus { get; set; } = "";

    public string DefaultMetadataDirectory => _coordinator?.DefaultMetadataDirectory ?? "";

    public bool HasLibraries => Libraries.Count > 0;
    public bool HasSavedLibrary => Libraries.Any(row => row.Saved);

    internal void UseLibraries(LocalLibraryCoordinator coordinator)
    {
        if (!ReferenceEquals(_coordinator, coordinator))
        {
            if (_coordinator is not null) _coordinator.Changed -= LibrariesChanged;
            _coordinator = coordinator;
            coordinator.Changed += LibrariesChanged;
        }
        OnPropertyChanged(nameof(DefaultMetadataDirectory));
        ReloadLibraries();
    }

    private void ReloadLibraries()
    {
        if (_coordinator is null) return;
        foreach (var row in Libraries) row.PropertyChanged -= LibraryRowChanged;
        Libraries.Clear();
        foreach (var library in _coordinator.Libraries)
        {
            var row = new LocalMediaLibraryRow(library, _coordinator.StatusFor(library.Id), persisted: true);
            row.PropertyChanged += LibraryRowChanged;
            Libraries.Add(row);
        }
        var saved = _settings?.Settings.LocalMedia.MetadataDirectory ?? "";
        MetadataDirectory = saved.Length > 0 ? saved : DefaultMetadataDirectory;
        MetadataStatus = saved.Length > 0 ? "自定义目录" : "默认目录";
        LibrariesStateChanged();
    }

    public void NewLibrary()
    {
        if (Busy) return;
        var row = new LocalMediaLibraryRow(new(), "填写媒体库名称和目录后保存", persisted: false);
        row.PropertyChanged += LibraryRowChanged;
        Libraries.Add(row);
        Notice = "填写媒体库名称和目录，保存后开始识别与入库。";
        LibrariesStateChanged();
    }

    public async Task SaveLibraryAsync(LocalMediaLibraryRow row)
    {
        if (!row.CanSave || _coordinator is null || !Libraries.Contains(row) || Busy) return;
        var configuration = row.ToSettings();
        using var operation = Begin();
        row.Busy = true;
        try
        {
            await _coordinator.SaveAsync(configuration, operation.Token);
            row.MarkSaved(_coordinator.StatusFor(configuration.Id));
            Invalidate("媒体库已保存；文件保持原位，元数据写入元数据目录。", save: false);
            Notice = row.MonitorEnabled
                ? "媒体库已保存，已有和新增文件将自动识别入库。"
                : "媒体库已保存，可手动扫描入库。";
            LibrariesStateChanged();
        }
        catch (OperationCanceledException) { Notice = "已取消保存。"; }
        catch (Exception error) { row.Status = ErrorText(error); Notice = row.Status; }
        finally { row.Busy = false; End(operation); }
    }

    public async Task RemoveLibraryAsync(LocalMediaLibraryRow row)
    {
        if (!row.CanRemove || _coordinator is null || !Libraries.Contains(row) || Busy) return;
        if (!row.Saved)
        {
            RemoveRow(row);
            Notice = "已移除未保存的目录。";
            return;
        }
        if (Confirm is null) return;
        using var operation = Begin();
        row.Busy = true;
        try
        {
            if (!await Confirm($"移除媒体库“{row.Name}”？\n\n将停止该库的自动监控并从首页隐藏。源文件与已刮削的元数据都保留。")) return;
            operation.Token.ThrowIfCancellationRequested();
            await _coordinator.RemoveAsync(row.Id, operation.Token);
            RemoveRow(row);
            Invalidate("媒体库已移除，文件与元数据保留。", save: false);
            Notice = "媒体库已移除，文件与元数据保留。";
        }
        catch (OperationCanceledException) { Notice = "已取消移除。"; }
        catch (Exception error) { row.Status = ErrorText(error); Notice = row.Status; }
        finally { row.Busy = false; End(operation); }
    }

    public async Task SaveMetadataDirectoryAsync()
    {
        if (!_attached || Busy || _settings is null) return;
        var value = MetadataDirectory.Trim();
        if (value.Length > 0)
        {
            try { value = Path.GetFullPath(value); }
            catch (Exception) { Notice = "元数据目录路径无效，请检查后重试。"; return; }
            if (string.Equals(value, DefaultMetadataDirectory, StringComparison.OrdinalIgnoreCase)) value = "";
        }
        using var operation = Begin();
        try
        {
            _settings.Settings.LocalMedia.MetadataDirectory = value;
            if (!_settings.TrySave()) throw new IOException();
            MetadataDirectory = value.Length > 0 ? value : DefaultMetadataDirectory;
            MetadataStatus = value.Length > 0 ? "自定义目录" : "默认目录";
            Invalidate("元数据目录已保存，请重新扫描。", save: false);
            if (_coordinator is not null) await _coordinator.RecognitionChangedAsync();
            Notice = value.Length > 0 ? "元数据目录已保存。" : $"已恢复默认元数据目录：{DefaultMetadataDirectory}";
        }
        catch (OperationCanceledException) { Notice = "已取消保存元数据目录。"; }
        catch (Exception) { Notice = "无法保存元数据目录，请检查本机设置文件权限后重试。"; }
        finally { End(operation); }
    }

    public async Task ResetMetadataDirectoryAsync()
    {
        MetadataDirectory = DefaultMetadataDirectory;
        await SaveMetadataDirectoryAsync();
    }

    private void RemoveRow(LocalMediaLibraryRow row)
    {
        row.PropertyChanged -= LibraryRowChanged;
        Libraries.Remove(row);
        LibrariesStateChanged();
    }

    private void LibrariesChanged(object? sender, EventArgs args)
    {
        if (!_attached || _coordinator is null) return;
        foreach (var row in Libraries.Where(row => row.Saved))
            if (_coordinator.Libraries.Any(library => library.Id == row.Id))
                row.Status = _coordinator.StatusFor(row.Id);
        LibrariesStateChanged();
    }

    private void LibraryRowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not LocalMediaLibraryRow row) return;
        if (row.Saved && args.PropertyName is nameof(LocalMediaLibraryRow.Name) or nameof(LocalMediaLibraryRow.Directory)
            or nameof(LocalMediaLibraryRow.Recursive) or nameof(LocalMediaLibraryRow.MonitorEnabled))
            row.Status = "有未保存的更改";
        LibrariesStateChanged();
    }

    private void LibrariesStateChanged()
    {
        OnPropertyChanged(nameof(HasLibraries));
        OnPropertyChanged(nameof(HasSavedLibrary));
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(CanApply));
    }
}
