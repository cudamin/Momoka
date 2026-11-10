using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.Music;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private long _shellPlayIntent;
    private MusicPlaybackViewModel? _music;
    private MusicPlaybackViewModel Music => _music ??= _services!.GetRequiredService<MusicPlaybackViewModel>();

    internal Task PlayMusicAsync(EmbyItem item, IReadOnlyList<EmbyItem> items,
        Func<CancellationToken, Task<IReadOnlyList<EmbyItem>>> loadQueue)
    {
        if (_services is null || _shutdownTask is not null) return Task.CompletedTask;
        ++_shellPlayIntent;
        return Music.PlayAsync(item, items, loadQueue);
    }

    private void AttachMusic()
    {
        MusicBar.Attach(Music);
        Music.BeforeStart = () => Player.ViewModel.StopAsync();
        Music.Noticed += Notify;
        Music.PropertyChanged += OnMusicChanged;
        MusicBar.SizeChanged += (_, _) => PlaceMusicBar();
        PlaceMusicBar();
    }

    private void OnMusicChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MusicPlaybackViewModel.Visible)) PlaceMusicBar();
    }

    private void PlaceMusicBar()
    {
        if (_music is null) return;
        var visible = _music.Visible;
        MusicBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        var left = ContentHost.Margin.Left;
        MusicBar.Margin = new Thickness(left + 16, 0, 16, 16);
        ContentFrame.Margin = new Thickness(0, 0, 0, visible ? Math.Max(92, MusicBar.ActualHeight + 28) : 0);
    }

    internal void OpenMusicContainer(EmbyItem item, string? rootId)
    {
        if (_services is null || item.Source is { IsAvailable: false }) return;
        RememberTrail();
        var library = MusicCatalog.IsLibrary(item.CollectionType);
        var request = new LibraryRequest
        {
            Services = _services,
            Title = item.Name,
            Source = item.Source,
            CollectionType = "music",
            ParentId = item.Id,
            MusicRootId = library ? item.Id : rootId,
            MusicContainerType = library ? null : item.Type
        };
        ContentFrame.Navigate(typeof(MusicLibraryPage), request, BrowseTransition());
        _trail.Add(new Crumb(item.Name, ""));
        _current = null;
        SyncChrome();
    }
}
