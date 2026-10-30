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

    /// <summary>
    /// ContentDialog 的表面宽度跟着内容走：不先给正文一个宽度，四列星号列会塌成内容宽度，
    /// 弹窗也就停在半宽。这里按窗口可用宽度给正文定宽，再由正文决定弹窗宽度（上限 1000）。
    /// </summary>
    private void OnDialogSizeChanged(object sender, SizeChangedEventArgs args)
    {
        DialogBody.Width = Math.Min(960, Math.Max(260, args.NewSize.Width - 80));
    }

    /// <summary>
    /// 表单按窗口宽度在四列、两列和单列之间重排。四列是为了在 1000 宽的弹窗里一次放下全部选项，
    /// 不再靠滚动条找下半截（上游网页端 720 宽时是滚动的）。
    /// </summary>
    private void OnBodySizeChanged(object sender, SizeChangedEventArgs args)
    {
        var width = args.NewSize.Width;
        ReorganizeTitle.Width = width;
        // 弹窗模板不带可用的滚动区（ContentScrollViewer 关闭滚动），内容高于窗口就会被裁；
        // 这里按屏幕高度给表单封顶，标题和操作区始终留在屏内。
        FormScroll.MaxHeight = Math.Clamp((XamlRoot?.Size.Height ?? 900) - 215, 200, 700);
        _formColumns = width >= 860 ? 4 : width >= 560 ? 2 : 1;
        LayoutForm();
        var compactActions = width < 430;
        Grid.SetRow(ReorganizeButton, compactActions ? 1 : 0);
        Grid.SetColumn(ReorganizeButton, compactActions ? 0 : 3);
        Grid.SetColumnSpan(ReorganizeButton, compactActions ? 4 : 1);
        ReorganizeButton.HorizontalAlignment = compactActions ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
    }

    /// <summary>按当前列数把所有字段重新摆进各自的网格；显隐变化（如复用历史）也要重排。</summary>
    private void LayoutForm()
    {
        // 复用历史时类型与数据源收起，目的那一行只剩两颗：收成两列，不在右边留半排空格。
        var columns = _formColumns;
        Reflow(DestinationGrid, [TargetStorageField, TransferMethodField, TypeField, MediaSourceField],
            FromHistoryToggle.IsOn ? Math.Min(2, columns) : columns);
        Reflow(IdentityGrid, [MediaIdPanel, SeasonField, EpisodeField, EpisodeGroupPanel, EpisodeFormatField, EpisodeOffsetField], columns);
        Reflow(FileOptionsGrid, [PartField, MinimumSizeField, ScrapeField, FromHistoryField], columns);
        Reflow(FolderOptionsGrid, [TypeFolderField, CategoryFolderField], Math.Min(2, columns));
    }

    private static void Reflow(Grid grid, IReadOnlyList<FrameworkElement> fields, int columns)
    {
        for (var index = 0; index < grid.ColumnDefinitions.Count; index++)
            grid.ColumnDefinitions[index].Width = index < columns ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        // 只留真正用到的行：空行本身高度是 0，但 Grid.RowSpacing 照样在空行之间加间距，
        // 四列时会凭空多出三行空白。行数跟着列数重算。
        grid.RowDefinitions.Clear();
        for (var index = 0; index < (fields.Count + columns - 1) / columns; index++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnSpacing = columns > 1 ? 14 : 0;
        for (var index = 0; index < fields.Count; index++)
        {
            Grid.SetColumn(fields[index], index % columns);
            Grid.SetRow(fields[index], index / columns);
            Grid.SetColumnSpan(fields[index], 1);
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
        TypeField.Visibility = MediaSourceField.Visibility = FromHistoryToggle.IsOn ? Visibility.Collapsed : Visibility.Visible;
        LayoutForm();
        IdentityGrid.Visibility = !FromHistoryToggle.IsOn && EditIdentity ? Visibility.Visible : Visibility.Collapsed;
        MediaIdPanel.Visibility = EditIdentity ? Visibility.Visible : Visibility.Collapsed;
        SeasonField.Visibility = EpisodeField.Visibility =
            EpisodeFormatField.Visibility = EpisodeOffsetField.Visibility =
                EditEpisodes ? Visibility.Visible : Visibility.Collapsed;
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
        var text = TargetPathBox.Text;
        // 和上游一样列出全部媒体库目录，不按当前存储筛：选中目录后存储、方式、分类开关都跟着目录配置走。
        TargetPathBox.ItemsSource = new[] { "自动" }.Concat((_options?.Directories ?? [])
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
        if (_busy) return;
        ApplyTargetPath();
    }

    private void OnDirectoryChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_initialized || _updating || _busy) return;
        // 下拉选中与手输走同一条路：文字没变时 TextChanged 不响，选中同一项仍要重新应用目录配置。
        ApplyTargetPath();
    }

    /// <summary>
    /// 目的路径变了就按目录配置改派生项（上游 ReorganizeDialog 监听 target_path 的那一段）：
    /// 命中的目录带来存储、整理方式、刮削与两个分类开关；手填的陌生路径按上游退到本地存储 + 复制、
    /// 三个开关显式关闭；回到「自动」则清空全部显式值，交回后端匹配。
    /// 整理方式只在当前仍是「自动」时采用目录配置，已手选的方式不被后来的路径覆盖。
    /// </summary>
    private void ApplyTargetPath()
    {
        if (AutomaticPath)
        {
            ResetAutomaticTarget();
            _ = RefreshAutomaticTargetAsync();
            return;
        }
        var path = TargetPathBox.Text.Trim();
        var directory = _options?.Directories.FirstOrDefault(item => MoviePilotTransfer.SamePath(item.Path, path));
        _updating = true;
        if (directory is not null)
        {
            SelectStorage(directory.Storage, onlyWhenAutomatic: false);
            if (TransferTypeBox.SelectedIndex <= 0) TransferTypeBox.SelectedIndex = TransferTypeIndex(directory.TransferType);
            PinFolderOptions(directory.Scrape, directory.TypeFolder, directory.CategoryFolder);
        }
        else
        {
            SelectStorage("local", onlyWhenAutomatic: true);
            if (TransferTypeBox.SelectedIndex <= 0) TransferTypeBox.SelectedIndex = TransferTypeIndex("copy");
            PinFolderOptions(false, false, false);
        }
        _updating = false;
        _matchedDirectory = null;
        Edited();
    }

    private void SelectStorage(string type, bool onlyWhenAutomatic)
    {
        var current = (TargetStorageBox.SelectedItem as MoviePilotStorage)?.Type ?? "";
        if (onlyWhenAutomatic && current.Length > 0) return;
        if (current == type) return;
        var items = (TargetStorageBox.ItemsSource as IEnumerable<MoviePilotStorage>)?.ToList() ?? [];
        var storage = items.FirstOrDefault(item => item.Type == type);
        if (storage is null)
        {
            if (type.Length == 0) return;
            storage = new MoviePilotStorage(type == "local" ? "本地" : type, type);
            items.Add(storage);
            TargetStorageBox.ItemsSource = items;
        }
        TargetStorageBox.SelectedItem = storage;
    }

    private static int TransferTypeIndex(string? type) => type switch
    {
        "copy" => 1,
        "move" => 2,
        "link" => 3,
        "softlink" => 4,
        _ => 0
    };

    private void PinFolderOptions(bool scrape, bool typeFolder, bool categoryFolder)
    {
        _scrape = scrape;
        _typeFolder = typeFolder;
        _categoryFolder = categoryFolder;
        ScrapeToggle.IsOn = scrape;
        TypeFolderToggle.IsOn = typeFolder;
        CategoryFolderToggle.IsOn = categoryFolder;
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
        _matchedDirectory = null;
        Edited();
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
