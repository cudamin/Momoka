using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.Configuration;
using Momoka.Shell.Services;

namespace Momoka.Shell.ViewModels;

public sealed partial class LocalMediaViewModel
{
    private LocalLibraryCoordinator? _coordinator;
    private bool _selectingLibrary;
    public ObservableCollection<LocalMediaLibrarySettings> Libraries { get; } = [];
    [ObservableProperty] public partial LocalMediaLibrarySettings? SelectedLibrary { get; set; }
    [ObservableProperty] public partial string LibraryName { get; set; } = "本地媒体库";
    [ObservableProperty] public partial bool MonitorEnabled { get; set; }
    [ObservableProperty] public partial string LibraryStatus { get; set; } = "保存媒体库后可开启自动监控";
    public bool CanSaveLibrary => _coordinator is not null && !Busy && !string.IsNullOrWhiteSpace(LibraryName) && !string.IsNullOrWhiteSpace(SourceDirectory);
    public bool CanRemoveLibrary => _coordinator is not null && !Busy && SelectedLibrary is { } selected && _coordinator.Libraries.Any(value => value.Id == selected.Id);
    public bool HasLibraries => Libraries.Count > 0;
    public bool HasSavedRule => _coordinator is null || SelectedLibrary is { } selected && _coordinator.Libraries.Any(value =>
        value.Id == selected.Id && value.SourceDirectory == SourceDirectory && value.TargetDirectory == TargetDirectory
        && value.Provider == (IsMoviePilot ? "moviepilot" : "momoka")
        && value.Method == (MethodIndex switch { 1 => "symboliclink", 2 => "rename", _ => "hardlink" }) && value.Recursive == Recursive);

    internal void UseLibraries(LocalLibraryCoordinator coordinator)
    {
        _coordinator = coordinator;
        coordinator.Changed += LibrariesChanged;
        ReloadLibraries(_settings?.Settings.LocalMedia.SelectedLibraryId);
    }

    private void ReloadLibraries(string? selectedId)
    {
        if (_coordinator is null) return;
        _selectingLibrary = true;
        Libraries.Clear();
        foreach (var configuration in _coordinator.Libraries) Libraries.Add(configuration);
        SelectedLibrary = Libraries.FirstOrDefault(value => value.Id == selectedId) ?? Libraries.FirstOrDefault();
        _selectingLibrary = false;
        if (SelectedLibrary is { } selected) LoadLibrary(selected);
        else
        {
            _selectingLibrary = true;
            SelectedLibrary = new();
            Libraries.Add(SelectedLibrary);
            _selectingLibrary = false;
            LoadLibrary(SelectedLibrary);
        }
        LibrariesStateChanged();
    }

    public void NewLibrary()
    {
        if (Busy) return;
        _selectingLibrary = true;
        SelectedLibrary = new();
        Libraries.Add(SelectedLibrary);
        _selectingLibrary = false;
        LoadLibrary(SelectedLibrary);
        Notice = "填写独立目录和媒体库名称，保存后将在首页显示。";
    }

    private void LoadLibrary(LocalMediaLibrarySettings selected)
    {
        _selectingLibrary = true;
        Stop();
        ClearRows();
        LibraryName = selected.Name;
        SourceDirectory = selected.SourceDirectory;
        TargetDirectory = selected.TargetDirectory;
        MethodIndex = selected.Method switch { "symboliclink" => 1, "rename" => 2, _ => 0 };
        ProviderIndex = selected.Provider == "moviepilot" ? 1 : 0;
        Recursive = selected.Recursive;
        MonitorEnabled = selected.MonitorEnabled;
        _selectingLibrary = false;
        if (_settings is not null) _settings.Settings.LocalMedia.SelectedLibraryId = selected.Id;
        LibraryStatus = _coordinator?.StatusFor(selected.Id) ?? "自动监控未开启";
        Invalidate(save: false);
        LibrariesStateChanged();
    }

    public async Task SaveLibraryAsync()
    {
        if (!CanSaveLibrary || _coordinator is null || SelectedLibrary is null) return;
        var configuration = new LocalMediaLibrarySettings
        {
            Id = SelectedLibrary.Id,
            Name = LibraryName.Trim(),
            SourceDirectory = SourceDirectory.Trim(),
            TargetDirectory = TargetDirectory.Trim(),
            Method = MethodIndex switch { 1 => "symboliclink", 2 => "rename", _ => "hardlink" },
            Provider = IsMoviePilot ? "moviepilot" : "momoka",
            Recursive = Recursive,
            MonitorEnabled = MonitorEnabled
        };
        using var operation = Begin();
        try
        {
            if (configuration.MonitorEnabled)
            {
                if (Confirm is null || !await Confirm($"开启“{configuration.Name}”的自动监控？\n\n源目录：{configuration.SourceDirectory}\n"
                    + (configuration.Method == "rename" ? "直接修改源文件名。" : $"目标目录：{configuration.TargetDirectory}\n{MethodNote}")
                    + "\n\n已有和新增文件都会自动识别并整理，生成 NFO 和图片。文件写入稳定后才处理，识别不明确的项目等待校正。关闭设置页后继续，退出 Momoka 后停止。")) return;
            }
            operation.Token.ThrowIfCancellationRequested();
            await _coordinator.SaveAsync(configuration, operation.Token);
            ReloadLibraries(configuration.Id);
            Notice = configuration.MonitorEnabled ? "媒体库已保存，已有和新增文件将自动识别整理。" : "媒体库已保存，可手动识别整理。";
        }
        catch (OperationCanceledException) { Notice = "已取消保存。"; }
        catch (Exception error) { Notice = ErrorText(error); }
        finally { End(operation); }
    }

    public async Task RemoveLibraryAsync()
    {
        if (!CanRemoveLibrary || SelectedLibrary is not { } selected || _coordinator is null || Confirm is null) return;
        using var operation = Begin();
        try
        {
            if (!await Confirm($"移除媒体库“{selected.Name}”？\n\n将停止该库的监控，并从首页隐藏。源文件、链接和刮削文件都保留。")) return;
            operation.Token.ThrowIfCancellationRequested();
            await _coordinator.RemoveAsync(selected.Id, operation.Token);
            ReloadLibraries(null);
            Notice = "媒体库配置已移除，文件已保留。";
        }
        catch (OperationCanceledException) { Notice = "已取消移除。"; }
        catch (Exception error) { Notice = ErrorText(error); }
        finally { End(operation); }
    }

    private void LibrariesChanged(object? sender, EventArgs args)
    {
        if (!_attached || SelectedLibrary is null) return;
        LibraryStatus = _coordinator!.StatusFor(SelectedLibrary.Id);
        LibrariesStateChanged();
    }
    private void LibrariesStateChanged()
    {
        OnPropertyChanged(nameof(CanSaveLibrary));
        OnPropertyChanged(nameof(CanRemoveLibrary));
        OnPropertyChanged(nameof(HasLibraries));
        OnPropertyChanged(nameof(HasSavedRule));
    }
    partial void OnSelectedLibraryChanged(LocalMediaLibrarySettings? value) { if (!_selectingLibrary && value is not null && !Busy) LoadLibrary(value); }
    partial void OnLibraryNameChanged(string value) => LibrariesStateChanged();
}
