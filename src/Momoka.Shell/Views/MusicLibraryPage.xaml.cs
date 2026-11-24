using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using Momoka.Emby;
using Momoka.Music;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class MusicLibraryPage : Page, IShellContent
{
    private IShellActions? _actions;
    private LibraryRequest? _request;
    private MusicPlaybackViewModel? _music;
    private bool _released;

    public MusicLibraryViewModel ViewModel { get; } = new();
    public object? NavigationRequest => _request;

    public MusicLibraryPage()
    {
        InitializeComponent();
        Root.SizeChanged += (_, _) => PlaceSearchBox();
    }

    public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    protected override async void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (args.Parameter is not LibraryRequest request) return;
        _request = request;
        // Frame history restores this page, while the shell adopts its destination tag.
        // Music containers keep the request's empty tag and remain drill-down pages.
        Tag = request.Tag;
        _released = false;
        _actions = request.Services.GetRequiredService<IShellActions>();
        _actions.SetTitleStrip(TitleStrip.Plain);
        ViewModel.Attach(request, request.Services.GetRequiredService<EmbySession>(), request.Services.GetRequiredService<EmbyImageStore>());

        // 底部播放栏的视图模型只在应用容器里注册；离线检查台没有它。没有就只是不标「正在放的是哪一首」，
        // 页面其余部分照常 —— 这一行装饰不该让整页解析失败。
        _music = request.Services.GetService(typeof(MusicPlaybackViewModel)) as MusicPlaybackViewModel;
        if (_music is not null)
        {
            _music.PropertyChanged += OnMusicChanged;
            ViewModel.MarkPlaying(_music.CurrentKey);
        }

        PlaceSearchBox();
        await ViewModel.ReloadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs args) { Release(); base.OnNavigatedFrom(args); }

    public void Release()
    {
        _released = true;
        DetachSearchInput();
        DetachWallArtwork();
        SetArtworkRealized(MusicRows, false);
        if (_music is not null) { _music.PropertyChanged -= OnMusicChanged; _music = null; }
        ViewModel.Release();
        _actions = null;
        _request = null;
    }

    /// <summary>
    /// 搜索框的宽度按「药丸占完之后还剩多少」算：宽窗 340 封顶，窄到一行放不下时让它换到下一行、独占整行
    /// （180 的输入框连占位提示都读不全）。写死一个宽度的话，360 宽的窗口里它会被右沿切掉半截。
    /// 中间那颗占位跟着剩余空间走 —— 宽窗里它把搜索框顶到右沿（与页头那颗刷新键对齐）。
    /// </summary>
    private void PlaceSearchBox()
    {
        var available = Root.ActualWidth - Root.Padding.Left - Root.Padding.Right;
        // 页面还没量过（导航那一刻 ActualWidth 是 0）就没有「剩余空间」可言，等第一次 SizeChanged 再说；
        // 给它一个负宽度会当场抛 ArgumentException。
        if (available <= 0) return;
        var room = available - SectionSurface.ActualWidth - Toolbar.Spacing * 2;
        var width = room >= 180 ? Math.Clamp(room, 180, 340) : Math.Clamp(available, 180, 340);
        // Width 没设过是 NaN，而 NaN 参与的比较一律为假 —— 少了 IsFinite 这一句，宽度永远写不进去。
        if (!double.IsFinite(MusicSearch.Width) || Math.Abs(MusicSearch.Width - width) > 0.5) MusicSearch.Width = width;
        var spacer = MusicSearch.Visibility == Visibility.Visible ? Math.Max(0, room - width) : 0;
        if (Math.Abs(ToolbarSpacer.Width - spacer) > 0.5) ToolbarSpacer.Width = spacer;
    }

    private async void OnRefresh(object sender, RoutedEventArgs args) => await ViewModel.ReloadAsync();

    private async void OnMore(object sender, RoutedEventArgs args) => await ViewModel.LoadMoreAsync();

    /// <summary>
    /// 分区互斥由这里保证：<see cref="ToggleButton"/> 只认得自己，点第二颗不会替第一颗取消；
    /// 再点已选中的那一颗则要把它按回去，否则一排里会一颗都不亮。
    /// </summary>
    private async void OnSectionClick(object sender, RoutedEventArgs args)
    {
        if (sender is not ToggleButton { Tag: string tag } || !int.TryParse(tag, out var index)) return;
        var section = (MusicSection)index;
        foreach (var tab in SectionTabs.Children.OfType<ToggleButton>())
            tab.IsChecked = ReferenceEquals(tab, sender);
        if (ViewModel.Section == section) return;

        await ViewModel.SelectAsync(section);
        if (_released) return;
        // 只有用户点分区、且分区确实变了才播这一次短入场；首次加载、刷新和探针直接调 SelectAsync 都不经过这里。
        // 关动画或已离树时由 HomeMotion 立即落定，不阻塞分区切换。
        HomeMotion.Reveal(MusicCatalog.UsesCoverWall(section) ? WallHost : TrackView, 0, 140, 6);
        MusicWall.ScrollView?.ScrollTo(0, 0,
            new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
    }

    private async void OnItemClick(object sender, ItemClickEventArgs args) =>
        await OpenAsync(args.ClickedItem as CardItem);

    private async void OnWallItemInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args) =>
        await OpenAsync(args.InvokedItem as CardItem);

    /// <summary>曲目行和封面格子是同一件事：歌曲开始播放，容器打开下一层。</summary>
    private async Task OpenAsync(CardItem? card)
    {
        if (card is null || _actions is null) return;
        if (MusicCatalog.IsAudio(card.Item.Type))
        {
            if (_actions is ShellPage shell) await shell.PlayMusicAsync(card.Item, ViewModel.Tracks, ViewModel.CaptureQueueLoader());
            else await _actions.PlayAsync(card.Item, episodes: ViewModel.Tracks);
        }
        else if (_actions is ShellPage shell && _request is not null)
            shell.OpenMusicContainer(card.Item, _request.MusicRootId ?? _request.ParentId);
    }

    /// <summary>底部播放栏换了歌：把曲目表里那一行标出来。播放状态归播放服务所有，这里只跟着画。</summary>
    private void OnMusicChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MusicPlaybackViewModel.CurrentKey)) return;
        ViewModel.MarkPlaying(_music?.CurrentKey);
    }

    /// <summary>
    /// 播放栏悬浮压在封面墙上（外壳不再整行预留），滚动区自己留避让：末行／「加载更多」
    /// 能滚到栏上方。clearance 为 0 时回到出厂内边距。墙保留原有的 32 底边。
    /// </summary>
    internal void SetPlayerClearance(double clearance)
    {
        MusicWall.Padding = new Thickness(0, 0, 20, 32 + clearance);
        WallMoreButton.Margin = new Thickness(0, 16, 20, clearance);
        MusicRows.Padding = new Thickness(0, 0, 20, clearance);
    }
}
