using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.LocalMedia;
using Momoka.MoviePilot;

namespace Momoka.Shell.ViewModels;

public sealed partial class LocalMediaRow(LocalMediaSource source, string libraryId, string libraryName) : ObservableObject
{
    public LocalMediaSource Source { get; } = source;
    public string LibraryId { get; } = libraryId;
    public string LibraryName { get; } = libraryName;
    public string SourcePath => Source.Path;
    public string FileName => Path.GetFileName(Source.Path);
    internal LocalMediaPlan? Plan { get; set; }
    internal string ConnectionStamp { get; set; } = "";
    internal bool Attempted { get; set; }
    [ObservableProperty] public partial bool Selected { get; set; }
    [ObservableProperty] public partial string Recognized { get; set; } = "等待识别";
    [ObservableProperty] public partial string VideoPath { get; set; } = "";
    [ObservableProperty] public partial string Status { get; set; } = "";
    [ObservableProperty] public partial string ResultDetails { get; set; } = "";
    public IReadOnlyList<LocalMediaItemResult> Results { get; internal set; } = [];
    [ObservableProperty] public partial bool Ready { get; set; }
}

public sealed record LocalMediaCandidate(string Label, string Id, bool Series)
{
    internal MoviePilotMedia? MoviePilot { get; init; }
}
