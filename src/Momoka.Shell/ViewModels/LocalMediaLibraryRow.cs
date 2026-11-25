using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.Configuration;

namespace Momoka.Shell.ViewModels;

/// <summary>目录列表里的一行：一个目录就是一个本地媒体库，保存时才要求名称和目录同时有效。</summary>
public sealed partial class LocalMediaLibraryRow : ObservableObject
{
    public LocalMediaLibraryRow(LocalMediaLibrarySettings settings, string status, bool persisted)
    {
        Id = settings.Id;
        Name = settings.Name;
        Directory = settings.SourceDirectory;
        Recursive = settings.Recursive;
        MonitorEnabled = settings.MonitorEnabled;
        Status = status;
        Saved = persisted;
    }

    public string Id { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string Directory { get; set; } = "";

    [ObservableProperty]
    public partial bool Recursive { get; set; } = true;

    [ObservableProperty]
    public partial bool MonitorEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyPropertyChangedFor(nameof(SaveLabel))]
    public partial bool Saved { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyPropertyChangedFor(nameof(Editable))]
    public partial bool Busy { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    public bool Editable => !Busy;
    public bool CanSave => !Busy && !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Directory);
    public bool CanRemove => !Busy;
    public string SaveLabel => Saved ? "保存" : "添加";

    public LocalMediaLibrarySettings ToSettings() => new()
    {
        Id = Id,
        Name = Name.Trim(),
        SourceDirectory = Directory.Trim(),
        Recursive = Recursive,
        MonitorEnabled = MonitorEnabled
    };

    public void MarkSaved(string status)
    {
        Saved = true;
        Status = status;
    }
}
