using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.Diagnostics;
using Momoka.Emby;

namespace Momoka.Shell.Views;

/// <summary>这个文件现在挂着的一条外挂字幕。公开的，因为模板里的 <c>x:Bind</c> 按类型名编译。</summary>
public sealed class SubtitleTrack(MediaStream stream)
{
    public MediaStream Stream { get; } = stream;

    public string Label { get; } = stream.ToDisplayLabel();
}

/// <summary>搜出来的一条候选字幕。</summary>
public sealed class SubtitleCandidate(RemoteSubtitleInfo info)
{
    public RemoteSubtitleInfo Info { get; } = info;

    public string Name { get; } = info.Name is { Length: > 0 } name ? name : "未命名的字幕";

    public string Detail { get; } = info.Describe();
}

/// <summary>
/// 字幕管理只持有固定文件的操作委托。写入及确认后的补读在飞时不允许关闭；
/// 响应不明时保留 <see cref="NeedsRefresh"/>，关闭也把重读义务交给父页，不重放写入。
/// </summary>
public sealed partial class SubtitleDialog : ContentDialog
{
    private const string Category = "ui";

    private static readonly (string Label, string Code)[] Languages =
    [
        ("中文", "chi"),
        ("英语", "eng"),
        ("日语", "jpn"),
        ("韩语", "kor")
    ];

    private readonly Func<ServerSubtitleSearch, Task<List<RemoteSubtitleInfo>>> _search;
    private readonly Func<RemoteSubtitleInfo, Task> _download;
    private readonly Func<MediaStream, Task> _delete;
    private readonly Func<Task<MediaSource>>? _reload;
    private readonly bool _supportsForcedOnly;
    private readonly bool _downloadProvidesConfirmation;
    private XamlRoot? _dialogRoot;
    private string _sourceLabel;
    private SubtitleTrack? _pendingDelete;
    private Button? _deleteButton;
    private bool _deleting;
    private bool _busy;
    private bool _mutating;
    private bool _closed;
    private bool _tracksStale;
    private string? _unconfirmedDownload;

    private readonly ObservableCollection<SubtitleTrack> _tracks = [];
    private readonly ObservableCollection<SubtitleCandidate> _results = [];

    public SubtitleDialog(
        EmbyItem item,
        MediaSource source,
        Func<ServerSubtitleSearch, Task<List<RemoteSubtitleInfo>>> search,
        Func<RemoteSubtitleInfo, Task> download,
        Func<MediaStream, Task> delete,
        Func<Task<MediaSource>>? reload = null,
        bool supportsForcedOnly = true,
        bool downloadProvidesConfirmation = true)
    {
        InitializeComponent();
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        // 标题是这张表自己画的一行（右上角那颗 ✕ 就在它右边）：文字交给行里的 TextBlock，读屏名字另给一份，
        // 因为 Title 这一格现在装的是界面元素而不是字符串。
        var title = $"搜索和修改字幕 — {item.Name}";
        DialogTitleText.Text = title;
        AutomationProperties.SetName(this, title);
        _search = search;
        _download = download;
        _delete = delete;
        _reload = reload;
        _supportsForcedOnly = supportsForcedOnly;
        _downloadProvidesConfirmation = downloadProvidesConfirmation;
        ForcedOnlyRow.Visibility = supportsForcedOnly ? Visibility.Visible : Visibility.Collapsed;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _sourceLabel = ItemDetail.SourceLabel(source);
        RefreshTracksButton.Visibility = reload is null ? Visibility.Collapsed : Visibility.Visible;
        Closing += (_, args) =>
        {
            if (_mutating) args.Cancel = true;
            else
            {
                _closed = true;
                CancelDelete();
            }
        };

        Tracks.ItemsSource = _tracks;
        Results.ItemsSource = _results;
        FillTracks(source);
        foreach (var (label, code) in Languages)
            LanguageBox.Items.Add(new ComboBoxItem { Content = label, Tag = code });
        LanguageBox.SelectedIndex = 0;
    }

    /// <summary>只有可靠写回执才置位；回读成功不能追认一次未知写入或 Jellyfin 的无确认回执。</summary>
    public bool Touched { get; private set; }

    /// <summary>已读新轨道或写响应不明时欠父页一次刷新；关闭和恢复读失败都不能清除。</summary>
    public bool NeedsRefresh { get; private set; }

    private void FillTracks(MediaSource source)
    {
        _sourceLabel = ItemDetail.SourceLabel(source);
        SourceFileText.Text = $"媒体文件：{_sourceLabel}";
        ToolTipService.SetToolTip(SourceFileText, _sourceLabel);
        _tracks.Clear();
        foreach (var stream in source.SubtitleStreams.Where(stream => stream.IsExternal))
            _tracks.Add(new SubtitleTrack(stream));
        // Jellyfin has no remote candidate ID on a read-back track. A changed list is useful data,
        // but cannot confirm this candidate or make another write safe in the same dialog.
        _tracksStale = _unconfirmedDownload is not null;
        ShowTracks();
    }

    private void ShowTracks()
    {
        Tracks.Visibility = _tracks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoTracks.Visibility = _tracks.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Say(string message)
    {
        if (_closed) return;
        Status.Text = message;
        Status.Visibility = message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetBusy(bool busy, bool mutating = false)
    {
        _busy = busy;
        _mutating = busy && mutating;
        SearchButton.IsEnabled = !busy && _unconfirmedDownload is null;
        RefreshTracksButton.IsEnabled = !busy;
        LanguageBox.IsEnabled = !busy && _unconfirmedDownload is null;
        ForcedOnly.IsEnabled = !busy && _unconfirmedDownload is null;
        Results.IsEnabled = !busy && !_tracksStale;
        Tracks.IsEnabled = !busy && !_tracksStale;
        ConfirmDeleteButton.IsEnabled = !busy && !_tracksStale;
        CancelDeleteButton.IsEnabled = !busy;
        // 写入在飞时关不掉（Closing 也会拦），右上角那颗跟着一起暗，不让它看着能按却没反应。
        HeaderCloseButton.IsEnabled = !_mutating;
        Busy.IsActive = busy;
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _dialogRoot = XamlRoot;
        if (_dialogRoot is not null) _dialogRoot.Changed += OnRootChanged;
        FitLayout();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (_dialogRoot is not null) _dialogRoot.Changed -= OnRootChanged;
        _dialogRoot = null;
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => FitLayout();

    private void FitLayout()
    {
        if (_dialogRoot is not { } root) return;
        var width = Math.Max(1, Math.Min(660, root.Size.Width - 96));
        SubtitleBody.MinWidth = Math.Min(260, width);
        SubtitleBody.Width = width;
        // 标题行和正文同宽：✕ 才落在右上角，而不是紧跟标题字（同 MoviePilotDownloadDialog 的 DownloadTitle）。
        DialogTitleRow.Width = width;
        SubtitleScroll.MaxHeight = Math.Max(1, root.Size.Height - 220);
    }

    /// <summary>右上角那颗 ✕：与「关闭」同一条出口（<see cref="ContentDialog.Hide"/>），Closing 里的拦截照样有效。</summary>
    private void OnClose(object sender, RoutedEventArgs e)
    {
        if (_mutating) return;
        Hide();
    }

    private void OnSearchConditionsChanged(object sender, RoutedEventArgs args)
    {
        if (_busy || _closed || _unconfirmedDownload is not null) return;
        _results.Clear();
        Results.Visibility = Visibility.Collapsed;
        NoSearchResults.Visibility = Visibility.Collapsed;
        if (!_tracksStale) Say("");
    }

    private async void OnSearch(object sender, RoutedEventArgs e)
    {
        if (_busy || _closed || _unconfirmedDownload is not null || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string code }) return;
        CancelDelete();
        SetBusy(true);
        Say("正在搜索…");
        _results.Clear();
        Results.Visibility = Visibility.Collapsed;
        NoSearchResults.Visibility = Visibility.Collapsed;
        var search = new ServerSubtitleSearch(code, _supportsForcedOnly && ForcedOnly.IsOn);
        try
        {
            var found = await _search(search).ConfigureAwait(true);
            if (_closed) return;
            foreach (var candidate in found) _results.Add(new SubtitleCandidate(candidate));
            Results.Visibility = _results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoSearchResults.Visibility = _results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Say(_results.Count > 0 ? $"找到 {_results.Count} 条字幕，点击「下载」添加到这个媒体文件。" : "");
        }
        catch (OperationCanceledException) { Say("登录身份已改变，请关闭后重新打开字幕管理。"); }
        catch (Exception error)
        {
            Log.Warn(Category, "搜索字幕失败", error);
            Say($"搜索字幕失败：{Failure.Describe(error)}");
        }
        finally { SetBusy(false); }
    }

    private async void OnDownload(object sender, RoutedEventArgs e)
    {
        if (_busy || _closed || _tracksStale || sender is not Button { DataContext: SubtitleCandidate candidate }) return;
        CancelDelete();
        SetBusy(true, mutating: true);
        Say($"正在下载「{candidate.Name}」，完成后可关闭…");
        try
        {
            await _download(candidate.Info).ConfigureAwait(true);
            if (_downloadProvidesConfirmation)
            {
                Touched = true;
                await RefreshAfterMutationAsync($"已挂上「{candidate.Name}」。关掉这张表之后就能在字幕里选它。");
            }
            else
            {
                _unconfirmedDownload = candidate.Name;
                // Record the parent read obligation before the automatic GET or closing can settle.
                NeedsRefresh = true;
                await RefreshAfterMutationAsync(UnconfirmedDownloadMessage);
            }
        }
        catch (OperationCanceledException) { Say("登录身份已改变，请关闭后重新打开字幕管理。"); }
        catch (Exception error)
        {
            MarkUncertainMutation(error);
            Log.Warn(Category, "下载字幕失败", error);
            Say($"下载字幕未完成确认：{Failure.Describe(error)}。请刷新列表后再操作。");
        }
        finally { SetBusy(false); }
    }

    private void MarkUncertainMutation(Exception error)
    {
        _tracksStale = _reload is not null;
        // Local validation and explicit permission/request rejection can happen before any write.
        if (error is InvalidOperationException or UnauthorizedAccessException
            || error is EmbyApiException { StatusCode: { } status } && (int)status is >= 400 and < 500) return;
        // A lost response may follow an applied write. Record the debt before closing becomes possible;
        // a later failed/closed recovery GET must not erase it, nor turn it into Touched success.
        NeedsRefresh = true;
    }

    private string UnconfirmedDownloadMessage =>
        $"请求已返回，但服务器未确认「{_unconfirmedDownload}」已添加。请核对字幕列表，继续刷新或关闭后重新打开；不要重复下载。";

    private async void OnRefreshTracks(object sender, RoutedEventArgs e)
    {
        if (_busy || _closed || _reload is null) return;
        CancelDelete();
        SetBusy(true);
        try
        {
            await ReloadTracksAsync();
            Say(_unconfirmedDownload is null ? "字幕列表已刷新。" : UnconfirmedDownloadMessage);
        }
        catch (OperationCanceledException) { Say("登录身份已改变，请关闭后重新打开字幕管理。"); }
        catch (Exception error)
        {
            _tracksStale = true;
            Log.Warn(Category, "刷新字幕列表失败", error);
            Say((_unconfirmedDownload is null ? "" : UnconfirmedDownloadMessage + " ")
                + $"刷新字幕列表失败：{Failure.Describe(error)}。请重试刷新。");
        }
        finally { SetBusy(false); }
    }

    private async Task ReloadTracksAsync()
    {
        if (_reload is null) return;
        var source = await _reload().ConfigureAwait(true);
        if (!_closed)
        {
            NeedsRefresh = true;
            FillTracks(source);
        }
    }

    private async Task RefreshAfterMutationAsync(string message)
    {
        _tracksStale = _reload is not null;
        try
        {
            await ReloadTracksAsync();
            Say(message);
        }
        catch (Exception error)
        {
            Log.Warn(Category, "字幕操作完成后刷新失败", error);
            Say(message + " 列表未刷新，请先点「刷新字幕列表」；不要重复提交刚才的操作。");
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SubtitleTrack track } button) BeginDelete(track, button);
    }

    private void BeginDelete(SubtitleTrack track, Button? button = null)
    {
        if (_busy || _closed || _tracksStale || !_tracks.Contains(track)) return;
        CancelDelete();
        _pendingDelete = track;
        _deleteButton = button;
        if (button is not null) button.IsEnabled = false;
        // ContentDialog cannot open another ContentDialog on its UI thread.
        DeleteConfirmationText.Text = $"字幕：{track.Label}\n媒体文件：{_sourceLabel}\n"
            + "将永久删除服务器上的字幕文件，影响此媒体库中的其他用户；不是仅从本次播放移除，无法撤销。";
        DeleteConfirmation.Visibility = Visibility.Visible;
        CancelDeleteButton.Focus(FocusState.Programmatic);
    }

    private void OnCancelDelete(object sender, RoutedEventArgs e) => CancelDelete();

    private void CancelDelete()
    {
        if (_deleting) return;
        _pendingDelete = null;
        DeleteConfirmation.Visibility = Visibility.Collapsed;
        if (_deleteButton is { } button)
        {
            button.IsEnabled = !_busy && !_tracksStale;
            if (!_closed && button.XamlRoot is not null) button.Focus(FocusState.Programmatic);
        }
        _deleteButton = null;
    }

    private async void OnConfirmDelete(object sender, RoutedEventArgs e) => await DeleteConfirmedAsync();

    internal void ShowDeleteConfirmationForProbe()
    {
        if (App.Instance?.SubtitleProbeActive == true && _tracks.Count > 0) BeginDelete(_tracks[0]);
    }

    internal static async Task<(bool Ok, string Detail)> ProbeDeleteConfirmationAsync()
    {
        var stream = new MediaStream { Index = 1, Type = "Subtitle", Codec = "srt", IsExternal = true, Title = "离线字幕" };
        var source = new MediaSource { Id = "fixture", Name = "离线文件", MediaStreams = [stream] };
        var calls = 0;
        var fail = false;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new SubtitleDialog(new EmbyItem { Id = "fixture", Name = "离线夹具" }, source,
            _ => Task.FromResult(new List<RemoteSubtitleInfo>()), _ => Task.CompletedTask,
            _ => { calls++; return fail ? Task.FromException(new IOException("模拟失败")) : completion.Task; });
        var track = dialog._tracks[0];
        dialog.BeginDelete(track);
        var asked = calls == 0 && !dialog.Touched && dialog.DeleteConfirmation.Visibility == Visibility.Visible
            && dialog.DeleteConfirmationText.Text.Contains("无法撤销");
        dialog.CancelDelete();
        await dialog.DeleteConfirmedAsync();
        var cancelled = calls == 0 && dialog._tracks.Count == 1 && !dialog.Touched;
        fail = true;
        dialog.BeginDelete(track);
        await dialog.DeleteConfirmedAsync();
        var failed = calls == 1 && dialog._tracks.Count == 1 && !dialog.Touched && !dialog._deleting;
        fail = false;
        dialog.BeginDelete(track);
        var pending = dialog.DeleteConfirmedAsync();
        await dialog.DeleteConfirmedAsync();
        var once = calls == 2 && dialog._deleting;
        completion.SetResult();
        await pending;
        var deleted = dialog._tracks.Count == 0 && dialog.Touched && !dialog._deleting;
        return (asked && cancelled && failed && once && deleted,
            $"假委托：确认前零请求={asked}、取消={cancelled}、失败保留={failed}、重复点击只发一次={once}、确认后删除={deleted}");
    }

    private async Task DeleteConfirmedAsync()
    {
        if (_busy || _closed || _tracksStale || _pendingDelete is not { } track || !_tracks.Contains(track)) return;
        _deleting = true;
        SetBusy(true, mutating: true);
        try
        {
            await _delete(track.Stream).ConfigureAwait(true);
            _tracks.Remove(track);
            ShowTracks();
            Touched = true;
            await RefreshAfterMutationAsync($"已删除服务器字幕「{track.Label}」。");
        }
        catch (OperationCanceledException) { Say("登录身份已改变，请关闭后重新打开字幕管理。"); }
        catch (Exception error)
        {
            MarkUncertainMutation(error);
            Log.Warn(Category, "删除字幕失败", error);
            Say($"删除字幕未完成确认：{Failure.Describe(error)}。请刷新列表后重新确认。");
        }
        finally
        {
            _deleting = false;
            SetBusy(false);
            CancelDelete();
        }
    }
}
