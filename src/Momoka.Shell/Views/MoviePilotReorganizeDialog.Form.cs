using Momoka.Diagnostics;
using Momoka.MoviePilot;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotReorganizeDialog
{
    private void OnDismiss(object sender, RoutedEventArgs args)
    {
        if (!_submitting) Hide();
    }

    private void OnBodySizeChanged(object sender, SizeChangedEventArgs args)
    {
        var narrow = args.NewSize.Width < 480;
        ReorganizeTitle.Width = args.NewSize.Width;
        FormScroll.MaxHeight = Math.Max(120, (XamlRoot?.Size.Height ?? 900) - 280);
        Place(TransferMethodField, DestinationGrid);
        Place(MediaSourceField, IdentityGrid);
        Place(EpisodeBox, SeasonGrid);
        Place(EpisodeOffsetField, EpisodeFormatGrid);
        Place(MinimumSizeField, FileOptionsGrid);
        Place(CategoryFolderField, FolderOptionsGrid);
        Place(FromHistoryField, ScrapeOptionsGrid);
        var compactActions = args.NewSize.Width < 430;
        Grid.SetRow(ReorganizeButton, compactActions ? 1 : 0);
        Grid.SetColumn(ReorganizeButton, compactActions ? 0 : 3);
        Grid.SetColumnSpan(ReorganizeButton, compactActions ? 4 : 1);
        ReorganizeButton.HorizontalAlignment = compactActions ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;

        void Place(FrameworkElement field, Grid grid)
        {
            grid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            grid.ColumnSpacing = narrow ? 0 : 20;
            Grid.SetColumn(field, narrow ? 0 : 1);
            Grid.SetRow(field, narrow ? 1 : 0);
        }
    }

    private void UpdateIdentityVisibility()
    {
        MediaIdBox.Header = SelectedSource switch
        {
            "themoviedb" => "TheMovieDb 编号",
            "douban" => "豆瓣编号",
            "bangumi" => "Bangumi 编号",
            "anilist" => "AniList 编号",
            _ => "媒体编号"
        };
        IdentityPanel.Visibility = FromHistoryToggle.IsOn ? Visibility.Collapsed : Visibility.Visible;
        MediaIdPanel.Visibility = EditIdentity ? Visibility.Visible : Visibility.Collapsed;
        EpisodeRow.Visibility = EditEpisodes ? Visibility.Visible : Visibility.Collapsed;
        EpisodeGroupPanel.Visibility = EditGroup ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnFromHistoryChanged(object sender, RoutedEventArgs args)
    {
        if (!_initialized || _updating) return;
        UpdateIdentityVisibility();
        QueueGroupLookup();
        Edited();
    }

    private void OnFolderOptionChanged(object sender, RoutedEventArgs args)
    {
        if (!_initialized || _updating) return;
        if (ReferenceEquals(sender, TypeFolderToggle)) _typeFolder = TypeFolderToggle.IsOn;
        if (ReferenceEquals(sender, CategoryFolderToggle)) _categoryFolder = CategoryFolderToggle.IsOn;
        if (ReferenceEquals(sender, ScrapeToggle)) _scrape = ScrapeToggle.IsOn;
        Edited();
    }

    private void UpdatePathChoices()
    {
        var storage = (TargetStorageBox.SelectedItem as MoviePilotStorage)?.Type ?? "";
        var text = TargetPathBox.Text;
        TargetPathBox.ItemsSource = new[] { "自动" }.Concat((_options?.Directories ?? [])
            .Where(directory => storage.Length == 0 || directory.Storage == storage)
            .Select(directory => directory.Path).Distinct(StringComparer.Ordinal)).ToList();
        TargetPathBox.Text = text;
    }

    private void ResetAutomaticTarget()
    {
        _updating = true;
        TargetPathBox.Text = "";
        TargetStorageBox.SelectedIndex = 0;
        TransferTypeBox.SelectedIndex = 0;
        _typeFolder = _categoryFolder = _scrape = null;
        _matchedDirectory = null;
        TypeFolderToggle.IsOn = CategoryFolderToggle.IsOn = ScrapeToggle.IsOn = false;
        UpdatePathChoices();
        _updating = false;
        InvalidatePreview();
        UpdateMatchVisibility();
    }

    private void OnTargetTextChanged()
    {
        if (!_initialized || _updating) return;
        if (AutomaticPath)
        {
            ResetAutomaticTarget();
            _ = RefreshAutomaticTargetAsync();
        }
        else Edited();
    }

    private async Task RefreshAutomaticTargetAsync()
    {
        if (_busy || _submitted || _histories.Count == 0) return;
        SetBusy(true);
        try { await RefreshTargetMatchAsync().ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception error) { Say(Failure.Describe(error)); }
        finally { SetBusy(false); }
    }

    private async void OnStorageChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_initialized || _updating || _busy) return;
        Edited();
        _matchedDirectory = null;
        _updating = true;
        UpdatePathChoices();
        _updating = false;
        UpdateMatchVisibility();
        if (!AutomaticPath || _histories.Count == 0) return;
        SetBusy(true);
        try { await RefreshTargetMatchAsync().ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception error) { Say(Failure.Describe(error)); }
        finally { SetBusy(false); }
    }

    private async Task RefreshTargetMatchAsync()
    {
        if (!AutomaticPath || _histories.Count == 0) { UpdateMatchVisibility(); return; }
        var histories = _histories;
        var storage = (TargetStorageBox.SelectedItem as MoviePilotStorage)?.Type ?? "";
        MatchedPathPanel.Visibility = Visibility.Visible;
        MatchedPathText.Text = "正在匹配目的路径…";
        UseMatchedPathButton.Visibility = Visibility.Collapsed;
        var matched = await _service.TransferHistoryTargetAsync(histories, _lifetime.Token, storage).ConfigureAwait(true);
        if (_lifetime.IsCancellationRequested || !ReferenceEquals(histories, _histories) || !AutomaticPath ||
            storage != (TargetStorageBox.SelectedItem as MoviePilotStorage)?.Type) return;
        _matchedDirectory = matched.Directories is [var directory] ? directory : null;
        _updating = true;
        // Nullable flags mean inheritance, not false. Show the matched effective switches without
        // turning them into overrides; only a user toggle or explicit directory pins the values.
        if (_typeFolder is null) TypeFolderToggle.IsOn = _matchedDirectory?.TypeFolder ?? false;
        if (_categoryFolder is null) CategoryFolderToggle.IsOn = _matchedDirectory?.CategoryFolder ?? false;
        if (_scrape is null) ScrapeToggle.IsOn = _matchedDirectory?.Scrape ?? false;
        _updating = false;
        UpdateMatchVisibility();
    }

    private void UpdateMatchVisibility()
    {
        MatchedPathPanel.Visibility = AutomaticPath && _histories.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UseMatchedPathButton.Visibility = _matchedDirectory is not null ? Visibility.Visible : Visibility.Collapsed;
        if (_matchedDirectory is not { } directory)
        {
            MatchedPathText.Text = "没有匹配到目的目录，可手动选择；预览会显示实际目标。";
            return;
        }
        var storage = _options?.Storages.FirstOrDefault(item => item.Type == directory.Storage)?.Name ?? directory.Storage;
        MatchedPathText.Text = $"自动匹配到 {storage} · {directory.Path}";
    }

    private void OnUseMatchedPath(object sender, RoutedEventArgs args)
    {
        if (_busy || _submitted || _matchedDirectory is not { } directory) return;
        ApplyDirectory(directory);
    }

    private void QueueGroupLookup()
    {
        _groupRevision++;
        _groupLookup?.Cancel();
        _groupLookup?.Dispose();
        _groupLookup = null;
        if (!_initialized || _options is null || !EditGroup) return;
        _groupLookup = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _ = LoadGroupsAsync(MediaIdBox.Text.Trim(), _groupRevision, _groupLookup.Token);
    }

    private async Task LoadGroupsAsync(string id, int revision, CancellationToken token)
    {
        try
        {
            await Task.Delay(350, token).ConfigureAwait(true);
            if (!Current()) return;
            EpisodeGroupHint.Text = "正在查询 TMDB 剧集组…";
            var groups = await _service.TransferEpisodeGroupsAsync(id, token).ConfigureAwait(true);
            if (!Current()) return;
            var selected = SelectedGroup;
            var text = EpisodeGroupBox.Text;
            _updating = true;
            EpisodeGroupBox.ItemsSource = groups;
            EpisodeGroupBox.Text = text;
            if (groups.FirstOrDefault(group => group.Id == selected) is { } choice)
            {
                EpisodeGroupBox.SelectedItem = choice;
                EpisodeGroupBox.Text = choice.Label;
            }
            _updating = false;
            EpisodeGroupHint.Text = groups.Count > 1 ? "可选择剧集组，也可填写编号；默认使用 TMDB 季集排序。" : "没有额外剧集组，可保留默认排序或手动填写编号。";
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!Current()) return;
            EpisodeGroupHint.Text = "剧集组查询失败，重新编辑媒体编号可重试，也可手动填写编号。";
            Say(Failure.Describe(error), InfoBarSeverity.Warning);
        }
        bool Current() => !token.IsCancellationRequested && !_lifetime.IsCancellationRequested &&
            revision == _groupRevision && EditGroup && MediaIdBox.Text.Trim() == id;
    }
}
