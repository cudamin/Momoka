using System.Collections.ObjectModel;
using Momoka.Diagnostics;
using Momoka.MoviePilot;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

/// <summary>原记录、表单、只读预览和确认在同一张对话框中，避免嵌套 ContentDialog 吞掉确认。</summary>
public sealed partial class MoviePilotReorganizeDialog : ContentDialog
{
    private readonly MoviePilotService _service;
    private readonly MoviePilotTransferContext _context;
    private readonly IReadOnlyList<MoviePilotTransferHistory>? _initialHistories;
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<MoviePilotTransferHistory> _histories = [];
    private IReadOnlyList<MoviePilotTransferHistory> _relatedHistories = [];
    private string? _submissionProblem;
    private MoviePilotTransferOptions? _options;
    private MoviePilotDirectory? _matchedDirectory;
    private bool? _typeFolder;
    private bool? _categoryFolder;
    private bool? _scrape;
    private int _groupRevision;
    private CancellationTokenSource? _groupLookup;
    private MoviePilotTransferPreview? _preview;
    private int _formColumns = 4;
    private bool _initialized;
    private bool _updating;
    private bool _busy;
    private bool _submitting;
    private bool _submitted;
    private int _revision;

    public ObservableCollection<MoviePilotTransferLine> Lines { get; } = [];

    internal MoviePilotReorganizeDialog(MoviePilotService service, MoviePilotTransferContext context,
        IReadOnlyList<MoviePilotTransferHistory>? initialHistories = null)
    {
        _service = service;
        _context = context;
        _initialHistories = initialHistories is null ? null : Array.AsReadOnly(initialHistories.ToArray());
        InitializeComponent();
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        ReorganizeSubtitle.Text = $"{context.Title} · 共 {context.Files.Count} 项";
        SourcePathBox.Text = string.Join("\n", context.Files.Select(file => file.Path));
        HistoryQuery.Text = context.Title;
        // 打开时全部是自动（对齐上游 ReorganizeDialog 的初始 transferForm）：类型、数据源、编号和季集都
        // 不带当前条目的值，由后端按文件名识别；要覆盖识别结果时再由用户手选。
        TypeBox.SelectedIndex = 0;
        TransferTypeBox.SelectedIndex = 0;
        TargetPathBox.RegisterPropertyChangedCallback(ComboBox.TextProperty, (_, _) => OnTargetTextChanged());
        EpisodeGroupBox.RegisterPropertyChangedCallback(ComboBox.TextProperty, (_, _) => Edited());
        Opened += async (_, _) => await LoadAsync().ConfigureAwait(true);
        Closing += (_, args) => { if (_submitting) args.Cancel = true; };
        Closed += (_, _) => { _lifetime.Cancel(); _groupLookup?.Cancel(); };
        _initialized = true;
        if (_initialHistories is not null)
        {
            Title = $"批量重新整理 — {context.Title}（{_initialHistories.Count} 条）";
            HistoryLookupPanel.Visibility = Visibility.Collapsed;
            BatchSelectionSummary.Visibility = Visibility.Visible;
            BatchSelectionSummary.Text = $"已选 {_initialHistories.Count} 条整理记录。季号与集数留空时，按每份资源的文件名识别。";
        }
        UpdateIdentityVisibility();
        SetBusy(false);
        UpdateSubmit();
    }

    private string SelectedType => (TypeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    private string SelectedSource => (SourceBox.SelectedItem as MoviePilotMediaSource)?.Id is { Length: > 0 } source
        ? source : _options?.DefaultMediaSource ?? "";
    private bool EditIdentity => !FromHistoryToggle.IsOn && SelectedType.Length > 0;
    private bool EditEpisodes => EditIdentity && SelectedType == MoviePilotTransferRequest.TypeSeries;
    private bool EditGroup => EditEpisodes && SelectedSource == "themoviedb" &&
        long.TryParse(MediaIdBox.Text.Trim(), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var id) && id > 0;
    private bool AutomaticPath => string.IsNullOrWhiteSpace(TargetPathBox.Text) || TargetPathBox.Text.Trim() == "自动";
    private string SelectedGroup => EpisodeGroupBox.SelectedItem is MoviePilotEpisodeGroup group && EpisodeGroupBox.Text == group.Label
        ? group.Id : EpisodeGroupBox.Text.Trim() is "默认" or "不指定剧集组" ? "" : EpisodeGroupBox.Text.Trim();

    private MoviePilotTransferRequest Request => new()
    {
        Files = _context.Files,
        Histories = _histories,
        TargetStorage = (TargetStorageBox.SelectedItem as MoviePilotStorage)?.Type ?? "",
        TargetPath = AutomaticPath ? "" : TargetPathBox.Text.Trim(),
        TransferType = (TransferTypeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "",
        Type = FromHistoryToggle.IsOn ? "" : SelectedType,
        MediaSource = SelectedSource,
        MediaId = EditIdentity ? MediaIdBox.Text.Trim() : "",
        Season = EditEpisodes ? SeasonBox.Text.Trim() : "",
        Episode = EditEpisodes ? EpisodeBox.Text.Trim() : "",
        EpisodeFormat = EditEpisodes ? EpisodeFormatBox.Text.Trim() : "",
        EpisodeOffset = EditEpisodes ? EpisodeOffsetBox.Text.Trim() : "",
        EpisodeGroup = EditGroup ? SelectedGroup : "",
        Part = PartBox.Text.Trim(),
        MinimumSize = MinSizeBox.Text.Trim(),
        TypeFolder = _typeFolder,
        CategoryFolder = _categoryFolder,
        Scrape = _scrape,
        FromHistory = FromHistoryToggle.IsOn,
        Reorganize = true
    };

    private async Task LoadAsync()
    {
        SetBusy(true);
        try
        {
            _options = await _service.TransferOptionsAsync(_lifetime.Token).ConfigureAwait(true);
            _updating = true;
            TargetStorageBox.ItemsSource = new[] { new MoviePilotStorage("自动", "") }.Concat(_options.Storages).ToList();
            TargetStorageBox.SelectedIndex = 0;
            SourceBox.ItemsSource = new[] { new MoviePilotMediaSource("自动", "", ["电影", "电视剧"]) }.Concat(_options.MediaSources).ToList();
            // The current library ID is TMDB, not the backend's possibly different default source.
            SourceBox.SelectedItem = MediaIdBox.Text.Length > 0
                ? _options.MediaSources.FirstOrDefault(source => source.Id == "themoviedb") : null;
            if (SourceBox.SelectedItem is null) { SourceBox.SelectedIndex = 0; MediaIdBox.Text = ""; }
            UpdatePathChoices();
            _updating = false;
            UpdateIdentityVisibility();
            if (_initialHistories is null)
            {
                await FindHistoryAsync().ConfigureAwait(true);
                QueueGroupLookup();
            }
            else
            {
                MoviePilotSeriesTransfer.CreateContext(_context with { BrowseSeriesHistory = true }, _initialHistories);
                _histories = _initialHistories;
                _relatedHistories = _initialHistories;
                await ApplyHistoryAsync().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Say(Failure.Describe(error)); }
        finally { _updating = false; SetBusy(false); }
    }

    private async Task FindHistoryAsync()
    {
        InvalidatePreview();
        _histories = [];
        _matchedDirectory = null;
        _submissionProblem = null;
        UpdateMatchVisibility();
        _updating = true;
        HistoryBox.ItemsSource = null;
        OriginalSourceBox.Text = "";
        HistorySummary.Text = "正在查找原整理记录…";
        _updating = false;
        var found = await _service.FindTransferHistoriesAsync(HistoryQuery.Text, _lifetime.Token).ConfigureAwait(true);
        _relatedHistories = found;
        if (_context.Files.Count == 0) throw new MoviePilotException("此条目没有可整理的单个文件");
        if (_context.Files.Count == 1)
        {
            var candidates = MoviePilotTransferHistoryMatch.Candidates(found, _context.Files[0].Path);
            _updating = true;
            HistoryBox.ItemsSource = candidates;
            HistoryBox.SelectedIndex = candidates.Count == 1 ? 0 : -1;
            _updating = false;
            if (candidates.Count != 1)
            {
                AdvancedOptions.IsExpanded = true;
                HistorySummary.Text = candidates.Count == 0 ? "没有找到路径匹配的原记录" : "找到多条匹配记录，请选择原整理记录";
                if (candidates.Count == 0)
                    Say("请更换原片名或文件名查找，或在 MoviePilot 核对路径映射；不会把当前媒体文件重新当作下载源。");
                return;
            }
            _histories = [candidates[0]];
        }
        else
        {
            _histories = MoviePilotTransferHistoryMatch.RequireMatches(_context, found);
            _updating = true;
            HistoryBox.ItemsSource = _histories;
            HistoryBox.SelectedIndex = 0;
            HistoryBox.IsEnabled = false;
            _updating = false;
        }
        await ApplyHistoryAsync().ConfigureAwait(true);
    }

    private async Task ApplyHistoryAsync()
    {
        OriginalSourceBox.Text = string.Join("\n", _histories.Select(history => history.TransferPath));
        HistorySummary.Text = string.Join("\n", _histories.Select(history => history.Display));
        var dependents = MoviePilotTransferHistoryMatch.Dependents(_histories, _relatedHistories);
        _submissionProblem = dependents.Count == 0 ? null : "旧目标被其他软链接记录引用（" +
            string.Join("、", dependents.Select(history => $"#{history.Id}")) +
            "），重新整理会让它们失效。可预览，但请先在 MoviePilot 处理引用关系后再提交。";
        await RefreshTargetMatchAsync().ConfigureAwait(true);
        Say(_submissionProblem, InfoBarSeverity.Warning);
    }

    private void ApplyDirectory(MoviePilotDirectory directory)
    {
        _updating = true;
        SelectStorage(directory.Storage, onlyWhenAutomatic: false);
        TargetPathBox.Text = directory.Path;
        TransferTypeBox.SelectedIndex = TransferTypeIndex(directory.TransferType);
        PinFolderOptions(directory.Scrape, directory.TypeFolder, directory.CategoryFolder);
        _updating = false;
        _matchedDirectory = null;
        InvalidatePreview();
        UpdateMatchVisibility();
    }

    private async void OnHistoryQuery(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_busy) return;
        SetBusy(true);
        try { await FindHistoryAsync().ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception error) { Say(Failure.Describe(error)); }
        finally { SetBusy(false); }
    }

    private async void OnHistoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updating || _context.Files.Count != 1 || HistoryBox.SelectedItem is not MoviePilotTransferHistory history) return;
        InvalidatePreview();
        _histories = [history];
        SetBusy(true);
        try { await ApplyHistoryAsync().ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception error) { Say(Failure.Describe(error)); }
        finally { SetBusy(false); }
    }

    private async void OnPreview(object sender, RoutedEventArgs e)
    {
        if (_busy || _submitted) return;
        InvalidatePreview();
        var request = Request;
        if (_histories.Count == 0) { Say("请先找到并选择原整理记录"); return; }
        if (request.Problem is { } problem) { Say(problem); return; }
        var revision = _revision;
        SetBusy(true);
        Say(null);
        try
        {
            var files = await _service.TransferHistoryFilesAsync(request, _lifetime.Token).ConfigureAwait(true);
            var preview = await _service.TransferPreviewAsync(request, files, _lifetime.Token).ConfigureAwait(true);
            if (_lifetime.IsCancellationRequested || revision != _revision || request != Request) return;
            Fill(preview.Result);
            if (!preview.CanSubmit) { Say(preview.Result.Message.Length > 0 ? preview.Result.Message : "预览不完整、有失败或没有目标文件，请修正后重试"); return; }
            _preview = preview;
            if (_submissionProblem is not null) { Say(_submissionProblem, InfoBarSeverity.Warning); return; }
            ConfirmationText.Text = MoviePilotTransfer.Confirmation(request, preview.Result, background: false);
            ConfirmationPanel.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Say(Failure.Describe(error)); }
        finally { SetBusy(false); }
    }

    private async void OnReorganizeSubmit(object sender, RoutedEventArgs args)
    {
        await SubmitAsync(background: false).ConfigureAwait(true);
    }

    private async void OnQueueSubmit(object sender, RoutedEventArgs args)
    {
        await SubmitAsync(background: true).ConfigureAwait(true);
    }

    private async Task SubmitAsync(bool background)
    {
        if (_busy || _submitted || _submissionProblem is not null || ConfirmChanges.IsChecked != true || _preview is not { CanSubmit: true } preview) return;
        if (!preview.Matches(Request)) { InvalidatePreview(); Say("表单已改变，请重新预览"); return; }
        _submitting = true;
        _submitted = true;
        _preview = null;
        SetBusy(true);
        ConfirmationPanel.Visibility = Visibility.Collapsed;
        try
        {
            var result = await _service.TransferSubmitAsync(preview, background, _lifetime.Token).ConfigureAwait(true);
            Fill(result);
            Say(result.Summary + (result.Message.Length > 0 ? $"。{result.Message}" : ""),
                result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        }
        catch (MoviePilotTransferBlockedException error)
        {
            _submitted = false;
            Say($"{error.Message}。本次没有提交整理请求。");
        }
        catch (OperationCanceledException)
        {
            // An actual sent write cancellation is translated to uncertain by WriteAsync. A plain
            // cancellation therefore belongs to preparation and must not retain the form lock.
            _submitted = false;
            Say("提交前操作已取消。本次没有提交整理请求。");
        }
        catch (MoviePilotOperationBlockedException error)
        {
            Say(error.Message);
        }
        catch (Exception error)
        {
            Say($"{Failure.Describe(error)}。请求可能已到达 MoviePilot，请先核对整理历史，不要重复提交。");
        }
        finally
        {
            _submitting = false;
            SetBusy(false);
        }
    }

    private void Fill(MoviePilotTransferResult result)
    {
        Lines.Clear();
        foreach (var line in result.Items) Lines.Add(line);
        PreviewList.Visibility = Lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PreviewSummary.Text = result.Summary;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_lifetime.IsCancellationRequested) return;
            FormScroll.UpdateLayout();
            FormScroll.ChangeView(null, FormScroll.ScrollableHeight, null, disableAnimation: true);
        });
    }

    private void InvalidatePreview()
    {
        _revision++;
        _preview = null;
        Lines.Clear();
        PreviewList.Visibility = Visibility.Collapsed;
        PreviewSummary.Text = "";
        ConfirmationPanel.Visibility = Visibility.Collapsed;
        ConfirmChanges.IsChecked = false;
        UpdateSubmit();
    }

    private void Edited()
    {
        if (!_initialized || _updating) return;
        InvalidatePreview();
        UpdateMatchVisibility();
        if (Request.Problem is { } problem) Say(problem);
        else Say(_submissionProblem, InfoBarSeverity.Warning);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        PreviewRing.IsActive = busy;
        PreviewRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        FormPanel.IsEnabled = !busy && !_submitted;
        PreviewButton.IsEnabled = !busy && !_submitted && _histories.Count > 0;
        UpdateSubmit();
    }

    private void UpdateSubmit()
    {
        var enabled = !_busy && _submissionProblem is null && _preview is { CanSubmit: true } && ConfirmChanges.IsChecked == true;
        QueueButton.IsEnabled = ReorganizeButton.IsEnabled = enabled;
        DismissReorganize.IsEnabled = !_submitting;
    }

    private void OnEdited(object sender, RoutedEventArgs e) => Edited();
    private void OnSelectionEdited(object sender, SelectionChangedEventArgs e) => Edited();
    private void OnConfirmed(object sender, RoutedEventArgs e) => UpdateSubmit();
    private void OnMediaIdChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_initialized || _updating) return;
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            sender.ItemsSource = null;
            _updating = true;
            EpisodeGroupBox.SelectedItem = null;
            EpisodeGroupBox.Text = "";
            EpisodeGroupBox.ItemsSource = null;
            _updating = false;
        }
        UpdateIdentityVisibility();
        QueueGroupLookup();
        Edited();
    }

    private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updating) return;
        _updating = true;
        MediaIdBox.ItemsSource = null;
        MediaIdBox.Text = "";
        SourceBox.SelectedIndex = 0;
        EpisodeGroupBox.Text = "";
        EpisodeGroupBox.SelectedItem = null;
        EpisodeGroupBox.ItemsSource = null;
        _updating = false;
        UpdateIdentityVisibility();
        QueueGroupLookup();
        Edited();
    }

    private void OnSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updating) return;
        _updating = true;
        MediaIdBox.Text = "";
        MediaIdBox.ItemsSource = null;
        EpisodeGroupBox.Text = "";
        EpisodeGroupBox.SelectedItem = null;
        EpisodeGroupBox.ItemsSource = null;
        _updating = false;
        UpdateIdentityVisibility();
        QueueGroupLookup();
        Edited();
    }

    private async void OnMediaIdQuery(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_busy || args.ChosenSuggestion is not null || string.IsNullOrWhiteSpace(args.QueryText)) return;
        SetBusy(true);
        try
        {
            var revision = _revision;
            var source = SelectedSource;
            var type = SelectedType;
            var found = await _service.SearchAsync(args.QueryText.Trim(), _lifetime.Token,
                source.Length > 0 ? [source] : null).ConfigureAwait(true);
            if (_lifetime.IsCancellationRequested || revision != _revision || source != SelectedSource || type != SelectedType) return;
            var matching = found.Where(media => media.CanSubscribe && media.Type == type &&
                (source.Length == 0 || media.MediaSource == source)).ToList();
            sender.ItemsSource = matching
                .Select(media => new MoviePilotMediaSuggestion(media)).ToList();
            if (matching.Count == 0) Say("没有找到符合当前类型和数据源的媒体，请填写正确的原生编号");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Say(Failure.Describe(error)); }
        finally { SetBusy(false); }
    }

    private void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is not MoviePilotMediaSuggestion suggestion) return;
        if (suggestion.Media.Type != Request.Type || !suggestion.Media.CanSubscribe ||
            _options?.MediaSources.FirstOrDefault(source => source.Id == suggestion.Media.MediaSource) is not { } source)
        {
            sender.ItemsSource = null;
            Say("这条建议与当前媒体类型或可用来源不一致，请重新查找");
            return;
        }
        _updating = true;
        SourceBox.SelectedItem = source;
        MediaIdBox.Text = suggestion.Media.MediaId ?? "";
        EpisodeGroupBox.SelectedItem = null;
        EpisodeGroupBox.Text = "";
        EpisodeGroupBox.ItemsSource = null;
        _updating = false;
        UpdateIdentityVisibility();
        QueueGroupLookup();
        Edited();
    }

    private void Say(string? message, InfoBarSeverity severity = InfoBarSeverity.Error)
    {
        Problem.Severity = severity;
        Problem.Message = message ?? "";
        Problem.IsOpen = message is not null;
    }

    private sealed record MoviePilotMediaSuggestion(MoviePilotMedia Media)
    {
        public override string ToString() => $"{Media.Display} — {Media.MediaId}";
    }
}
