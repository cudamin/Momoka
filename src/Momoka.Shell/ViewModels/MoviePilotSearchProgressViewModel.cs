using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Momoka.MoviePilot;

namespace Momoka.Shell.ViewModels;

/// <summary>两处资源搜索共用的进度卡片；回调只属于创建它的那一趟搜索。</summary>
public sealed partial class MoviePilotSearchProgressViewModel : ObservableObject
{
    private long _generation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Visibility))]
    public partial bool IsActive { get; set; }
    public Visibility Visibility => IsActive ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty] public partial bool IsIndeterminate { get; set; } = true;
    [ObservableProperty] public partial double Value { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "正在搜索资源…";
    [ObservableProperty] public partial string PercentText { get; set; } = "…";
    [ObservableProperty] public partial string TitleText { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(YearVisibility))]
    public partial string YearText { get; set; } = "";
    public Visibility YearVisibility => YearText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    internal IProgress<MoviePilotSearchProgress> Start(string title, int? year, Func<bool> current, bool subtitle = false)
    {
        var generation = ++_generation;
        Value = 0;
        IsIndeterminate = true;
        PercentText = "…";
        Status = subtitle ? "正在搜索字幕…" : "正在搜索资源…";
        TitleText = title;
        YearText = year is { } number ? $"年份：{number}" : "";
        IsActive = true;
        return new Progress<MoviePilotSearchProgress>(progress =>
        {
            if (generation != _generation || !IsActive || !current()) return;
            var known = progress.Value is { } value && double.IsFinite(value);
            IsIndeterminate = !known;
            Value = known ? Math.Clamp(Math.Ceiling(progress.Value!.Value), 0, 100) : 0;
            PercentText = known ? $"{Value:0}%" : "…";
            if (!string.IsNullOrWhiteSpace(progress.Text)) Status = progress.Text;
        });
    }

    internal void Stop()
    {
        ++_generation;
        IsActive = false;
    }

}
