using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotMediaSeasonsView : UserControl
{
    internal MoviePilotMediaViewModel ViewModel { get; private set; } = new();
    private MoviePilotSubscriptionsViewModel? _files;
    public MoviePilotMediaSeasonsView() => InitializeComponent();
    internal bool HasSubscriptionFiles => ViewModel.Seasons.Any(row => row.Files is not null);

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

    /// <summary>季列表每次重读都会整批重建，行一上场就按订阅季号挂文件统计并展开，文件情况开页即见。</summary>
    private void OnSeasonsChanged(object? sender, NotifyCollectionChangedEventArgs args) => FlagSubscriptionSeason();

    private void FlagSubscriptionSeason()
    {
        if (_files?.Selected is not { } card) return;
        var season = card.Subscription.Media.Season;
        foreach (var row in ViewModel.Seasons)
        {
            var host = !_files.Deleted && !row.IsMovie && row.Season.Number == season
                && ViewModel.GroupId == card.Subscription.EpisodeGroup;
            row.Files = host ? _files : null;
            if (!host) continue;
            row.FilesSummary = _files.FileSummary;
            row.FilesPlaybackStatus = _files.PlaybackStatus;
            row.FilesFilterIndex = _files.FilterIndex;
            row.FilesEpisodes = _files.Episodes;
            row.Expanded = true;
        }
    }

    private void OnFilesPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_files is null) return;
        if (args.PropertyName is nameof(MoviePilotSubscriptionsViewModel.Deleted) or nameof(MoviePilotSubscriptionsViewModel.Selected))
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
            case nameof(MoviePilotSubscriptionsViewModel.FilterIndex): row.FilesFilterIndex = _files.FilterIndex; break;
        }
    }

    private void OnFilesFilterChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_files is { } files && sender is ComboBox { SelectedIndex: var index } && index >= 0 && index != files.FilterIndex)
            files.FilterIndex = index;
    }

    private void OnFilesEpisodeLoaded(object sender, RoutedEventArgs args)
    {
        if (_files is { } files && sender is FrameworkElement { Tag: MoviePilotSubscriptionEpisodeRow row }) _ = files.LoadEpisodeArtworkAsync(row);
    }

    private void OnFilesEpisodeSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (sender is not Grid grid) return;
        var compact = args.NewSize.Width < 560;
        var picture = (Border)grid.Children[0];
        var content = (StackPanel)grid.Children[1];
        var actions = (StackPanel)grid.Children[2];
        grid.RowSpacing = compact ? 8 : 0;
        Grid.SetColumnSpan(picture, compact ? 3 : 1);
        Grid.SetRow(content, compact ? 1 : 0);
        Grid.SetColumn(content, compact ? 0 : 1);
        Grid.SetColumnSpan(content, compact ? 2 : 1);
        Grid.SetRow(actions, compact ? 1 : 0);
        picture.Width = Math.Min(Momoka.Infrastructure.CardSize.WideWidth, Math.Max(0, args.NewSize.Width));
        picture.Height = picture.Width * 9 / 16;
    }

    private void OnEpisodeSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (sender is not Grid grid) return;
        var compact = args.NewSize.Width < 440;
        var picture = (Border)grid.Children[0];
        var content = (StackPanel)grid.Children[1];
        Grid.SetColumn(content, compact ? 0 : 1);
        Grid.SetRow(content, compact ? 1 : 0);
        Grid.SetColumnSpan(content, compact ? 2 : 1);
        picture.Width = compact ? Math.Min(240, Math.Max(0, args.NewSize.Width)) : 180;
        picture.Height = picture.Width * 9 / 16;
    }
}
