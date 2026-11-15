using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotMediaSeasonsView : UserControl
{
    internal MoviePilotMediaViewModel ViewModel { get; private set; } = new();
    private MoviePilotSubscriptionsViewModel? _files;
    public MoviePilotMediaSeasonsView() => InitializeComponent();
    internal bool HasSubscriptionFiles => ViewModel.Seasons.Any(row => row.Files is not null);

    public static Visibility HeadingVisibility(bool store) => store ? Visibility.Collapsed : Visibility.Visible;
    public static double HeadingSpacing(bool store) => store ? 0 : 16;
    public static Visibility HeadingRowVisibility(bool store, Visibility groups) =>
        store && groups != Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// <paramref name="files"/> 是订阅详情的视图模型：给了它，订阅那一季的折叠栏就不再展开通用集列表，
    /// 改为装文件统计（统计、筛选与逐集播放/搜索，用户令 2026-10-12）。数值不直接绑过去 —— 嵌套 x:Bind
    /// 会被 bind 基线拦住 —— 而是由这里推进行上的显示快照，<see cref="MoviePilotMediaSeasonRow.FilesEpisodes"/>
    /// 则共享同一个集合实例，筛选在原地生效。
    /// </summary>
    internal void Attach(MoviePilotMediaViewModel viewModel, MoviePilotSubscriptionsViewModel? files = null)
    {
        if (!ReferenceEquals(ViewModel, viewModel))
        {
            ViewModel.Seasons.CollectionChanged -= OnSeasonsChanged;
            ViewModel = viewModel;
            ViewModel.Seasons.CollectionChanged += OnSeasonsChanged;
        }
        if (!ReferenceEquals(_files, files))
        {
            if (_files is not null) _files.PropertyChanged -= OnFilesPropertyChanged;
            _files = files;
            if (_files is not null) _files.PropertyChanged += OnFilesPropertyChanged;
        }
        Bindings.Update();
        FlagSubscriptionSeason();
    }

    /// <summary>订阅详情「更多」菜单的「文件统计」：展开订阅季并把它滚进视野；季还没读出来时不动。</summary>
    internal void ShowSubscriptionFiles()
    {
        for (var index = 0; index < ViewModel.Seasons.Count; index++)
        {
            if (ViewModel.Seasons[index].Files is null) continue;
            ViewModel.Seasons[index].Expanded = true;
            if (MediaSeasonList.GetOrCreateElement(index) is FrameworkElement container) container.StartBringIntoView();
            return;
        }
    }

    /// <summary>按订阅季号挂文件统计；保持默认收起及用户当前的展开状态。</summary>
    private void OnSeasonsChanged(object? sender, NotifyCollectionChangedEventArgs args) => FlagSubscriptionSeason();

    private void FlagSubscriptionSeason()
    {
        var card = _files?.Selected;
        var season = card?.Subscription.Media.Season;
        foreach (var row in ViewModel.Seasons)
        {
            var host = _files is not null && card is not null && !_files.Deleted && !row.IsMovie && row.Season.Number == season
                && ViewModel.GroupId == card.Subscription.EpisodeGroup;
            row.Files = host ? _files : null;
            if (!host)
            {
                row.FilesEpisodes = null;
                row.FilesSummary = "";
                row.FilesPlaybackStatus = "";
                row.FilesEmptyNotice = "";
                continue;
            }
            row.FilesSummary = _files!.FileSummary;
            row.FilesPlaybackStatus = _files.PlaybackStatus;
            row.FilesEmptyNotice = _files.FilterNotice;
            row.FilesFilterIndex = _files.FilterIndex;
            row.FilesEpisodes = _files.Episodes;
        }
    }

    private void OnFilesPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_files is null) return;
        if (args.PropertyName is nameof(MoviePilotSubscriptionsViewModel.Deleted) or nameof(MoviePilotSubscriptionsViewModel.Selected) or nameof(MoviePilotSubscriptionsViewModel.SelectedIdentity))
        {
            FlagSubscriptionSeason();
            return;
        }
        var row = ViewModel.Seasons.FirstOrDefault(item => ReferenceEquals(item.Files, _files));
        if (row is null) return;
        switch (args.PropertyName)
        {
            case nameof(MoviePilotSubscriptionsViewModel.FileSummary): row.FilesSummary = _files.FileSummary; break;
            case nameof(MoviePilotSubscriptionsViewModel.PlaybackStatus): row.FilesPlaybackStatus = _files.PlaybackStatus; break;
            case nameof(MoviePilotSubscriptionsViewModel.FilterNotice): row.FilesEmptyNotice = _files.FilterNotice; break;
            case nameof(MoviePilotSubscriptionsViewModel.FilterIndex): row.FilesFilterIndex = _files.FilterIndex; break;
        }
    }

    private void OnFilesFilterChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_files is { } files && sender is ComboBox { Tag: MoviePilotMediaSeasonRow row, SelectedIndex: var index }
            && ReferenceEquals(row.Files, files) && index >= 0 && index != files.FilterIndex)
            files.FilterIndex = index;
    }

    private void OnFilesEpisodePrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        // 刷新/筛选会复用已经 Loaded 的容器；每次准备新条目都要为当前行加载图片。
        if (_files is { } files && ReferenceEquals(sender.ItemsSource, files.Episodes)
            && args.Index >= 0 && args.Index < files.Episodes.Count)
            _ = files.LoadEpisodeArtworkAsync(files.Episodes[args.Index]);
    }

    private void OnFilesEpisodeSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (sender is not Grid grid) return;
        var picture = (Border)grid.Children[0];
        // 窄行缩小缩略图，文字始终在同一行；图片迟到或失败只收放 Auto 列，不留下空的上方行。
        ResizeEpisodeArtwork(grid, picture, args.NewSize.Width);
    }

    private void OnEpisodeSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (sender is not Grid grid) return;
        var picture = (Border)grid.Children[0];
        ResizeEpisodeArtwork(grid, picture, args.NewSize.Width);
    }

    /// <summary>
    /// 图片到达或失败会改变行高，行高又会让列表视口重新量一次；在 SizeChanged 回调里同步改图片尺寸，
    /// 等于把这一串变化原地喂回同一拍，2026-11-14 在「图片从失败恢复成功、两行同时有图」时实测打成
    /// LayoutCycle。改到下一拍执行并跳过相同尺寸，布局环就断了。
    /// </summary>
    private static void ResizeEpisodeArtwork(Grid grid, Border picture, double rowWidth)
    {
        var width = Math.Min(144, Math.Max(0, rowWidth / 3));
        var height = width * 9 / 16;
        if (Math.Abs(picture.Width - width) < 0.5 && Math.Abs(picture.Height - height) < 0.5) return;
        grid.DispatcherQueue.TryEnqueue(() =>
        {
            // 排队期间行宽可能已经变了（换布局模式、窗口缩放），按执行时的实际行宽算；
            // 拿排队那一刻的旧宽度写回去，缩略图会永远停在小尺寸（2026-11-15 交付实跑 760 宽复现）。
            var actual = Math.Min(144, Math.Max(0, grid.ActualWidth / 3));
            if (Math.Abs(picture.Width - actual) < 0.5 && Math.Abs(picture.Height - actual * 9 / 16) < 0.5) return;
            picture.Width = actual;
            picture.Height = actual * 9 / 16;
        });
    }
}
