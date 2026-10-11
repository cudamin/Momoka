using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.MoviePilot;

namespace Momoka.Shell.ViewModels;

public sealed partial class MoviePilotDownloadViewModel : PageViewModel
{
    public const string DefaultDownloader = "使用 MoviePilot 默认下载器";
    private MoviePilotService? _service;
    private MoviePilotResource? _resource;
    public ObservableCollection<string> Downloaders { get; } = [DefaultDownloader];
    public ObservableCollection<string> Paths { get; } = [];
    [ObservableProperty] public partial string Downloader { get; set; } = DefaultDownloader;
    [ObservableProperty] public partial string SavePath { get; set; } = "";
    public bool CanSubmit => !Busy;
    public string Confirmation => _resource?.Confirmation ?? "";
    internal MoviePilotDownloadOptions Options => new(Downloader == DefaultDownloader ? null : Downloader,
        string.IsNullOrWhiteSpace(SavePath) ? null : SavePath.Trim());

    internal void Attach(MoviePilotService service, MoviePilotResource resource)
    {
        _service = service;
        _resource = resource;
        OnPropertyChanged(nameof(Confirmation));
    }

    public override async Task ReloadAsync()
    {
        if (_service is null || _resource is null) return;
        var token = BeginLoad();
        try
        {
            var choices = await _service.DownloadChoicesAsync(_resource, token).ConfigureAwait(true);
            if (!IsCurrent(token)) return;
            var previous = Downloader;
            Downloaders.Clear();
            Downloaders.Add(DefaultDownloader);
            foreach (var name in choices.Downloaders) Downloaders.Add(name);
            Downloader = Downloaders.Contains(previous) ? previous : DefaultDownloader;
            Paths.Clear();
            foreach (var path in choices.Paths) Paths.Add(path);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (IsCurrent(token)) Report("读取下载选项失败，可刷新重试或使用默认设置", error);
        }
        finally { EndLoad(token); }
    }

    protected override void BusyChanged() => OnPropertyChanged(nameof(CanSubmit));
    [RelayCommand] private Task Refresh() => ReloadAsync();
}
