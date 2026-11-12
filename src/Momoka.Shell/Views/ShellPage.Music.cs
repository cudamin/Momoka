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
    private SurfaceMotion? _musicBarMotion;
    private bool _musicBarShown;
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
        var changed = visible != _musicBarShown;
        _musicBarShown = visible;
        var motion = _musicBarMotion ??= new SurfaceMotion(MusicBar);

        if (visible)
        {
            MusicBar.Visibility = Visibility.Visible;
            MusicBar.IsHitTestVisible = true;
            PlaceMusicBarMargins(true);
            if (changed) motion.Enter();
            return;
        }

        if (changed)
        {
            // 收起：栏先淡出，内容区等它落定再收回 —— 先跳一下再看着栏消失更别扭。
            MusicBar.IsHitTestVisible = false;
            motion.Exit(landed: () =>
            {
                MusicBar.Visibility = Visibility.Collapsed;
                PlaceMusicBarMargins(false);
            });
            return;
        }

        // 正在淡出：让动画自己收尾，别让一次尺寸变化把栏当场抹掉。
        if (motion.IsAnimating) return;

        motion.Collapse();
        MusicBar.Visibility = Visibility.Collapsed;
        PlaceMusicBarMargins(false);
    }

    /// <summary>栏与内容区各让多少位：栏自己永远悬浮，内容区按当前页决定要不要留底。</summary>
    private void PlaceMusicBarMargins(bool visible)
    {
        var left = ContentHost.Margin.Left;
        MusicBar.Margin = new Thickness(left + 16, 0, 16, 16);
        if (ContentFrame.Content is MusicLibraryPage musicPage)
        {
            // 胶囊直接压在封面墙上：内容区不整行让位，否则亚克力底下永远是纯色纸面。
            // 末行的可达性改由滚动区自己的避让保证（SetPlayerClearance），停播后归还。
            ContentFrame.Margin = new Thickness(0, ContentFrame.Margin.Top, 0, 0);
            musicPage.SetPlayerClearance(visible ? MusicBar.ActualHeight + 32 : 0);
        }
        else
        {
            ContentFrame.Margin = new Thickness(0, ContentFrame.Margin.Top, 0, visible ? Math.Max(92, MusicBar.ActualHeight + 28) : 0);
        }
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
