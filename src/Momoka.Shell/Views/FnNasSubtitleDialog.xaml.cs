using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.FnNas;

namespace Momoka.Shell.Views;

public sealed class FnNasSubtitleRow(FnNasSubtitleTrack track)
{
    public FnNasSubtitleTrack Track { get; } = track;
    public string Label => Track.Label;
    public string Detail => Track.Format.ToUpperInvariant() + (Track.Format.ToLowerInvariant() is "sup" or "sub" or "dvb" or "dvd_subtitle" or "hdmv_pgs_subtitle"
        ? " · 仅管理；当前播放器不加载这个外部格式" : "");
    public Visibility DeleteVisibility => Track.CanDelete ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class FnNasSubtitleResultRow(FnNasSubtitleCandidate candidate, bool canDownload)
{
    public FnNasSubtitleCandidate Candidate { get; } = candidate;
    public string Name => Candidate.FileName.Length > 0 ? Candidate.FileName : "未命名字幕";
    public string Detail => Candidate.Description;
    public Visibility DownloadVisibility => canDownload ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Every media version retains one service, snapshot and receipt; changing the picker cannot rebind a write.</summary>
public sealed partial class FnNasSubtitleDialog : ContentDialog
{
    private readonly IReadOnlyList<FnNasSubtitles> _versions;
    private readonly Func<bool> _isCurrent;
    private readonly Func<XamlRoot, Func<bool>, Task<FnNasSubtitleUpload?>> _pick;
    private readonly ObservableCollection<FnNasSubtitleRow> _tracks = [];
    private readonly ObservableCollection<FnNasSubtitleResultRow> _results = [];
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _read;
    private FnNasSubtitles? _service;
    private FnNasSubtitleTrack? _pendingDelete;
    private XamlRoot? _dialogRoot;
    private long _generation;
    private bool _busy;
    private bool _mutating;
    private bool _closed;
    private bool _ready;

    public FnNasSubtitleDialog(string itemName, IReadOnlyList<FnNasSubtitles> versions, Func<bool>? isCurrent = null)
        : this(itemName, versions, isCurrent, FnNasSubtitleFile.PickAsync) { }

    internal FnNasSubtitleDialog(string itemName, IReadOnlyList<FnNasSubtitles> versions, Func<bool>? isCurrent,
        Func<XamlRoot, Func<bool>, Task<FnNasSubtitleUpload?>> pick)
    {
        if (versions.Count == 0) throw new InvalidOperationException("没有可管理字幕的媒体版本");
        InitializeComponent();
        _versions = versions;
        _isCurrent = isCurrent ?? (() => true);
        _pick = pick;
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        DialogTitleText.Text = "管理飞牛字幕 — " + itemName;
        AutomationProperties.SetName(this, DialogTitleText.Text);
        Tracks.ItemsSource = _tracks;
        Results.ItemsSource = _results;
        foreach (var service in versions)
            VersionBox.Items.Add(new ComboBoxItem { Content = service.Path, Tag = service });
        foreach (var (label, code) in new[] { ("简体中文", "zh-CN"), ("繁体中文", "zh-TW"), ("英语", "en"), ("日语", "ja"), ("韩语", "ko") })
            LanguageBox.Items.Add(new ComboBoxItem { Content = label, Tag = code });
        LanguageBox.SelectedIndex = 0;
        _service = versions[0];
        VersionBox.SelectedIndex = 0;
        SourceFileText.Text = "媒体文件：" + _service.Path;
        Opened += OnOpened;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Closing += (_, args) =>
        {
            if (_mutating) { args.Cancel = true; return; }
            _closed = true;
            _generation++;
            _read?.Cancel();
            _lifetime.Cancel();
            CancelDelete();
        };
        Closed += (_, _) => { _read?.Dispose(); _lifetime.Dispose(); };
        _ready = true;
        SyncControls();
        if (_service?.CanManage != true) Say("当前飞牛账号没有字幕操作权限。权限恢复后可刷新列表。");
    }

    public bool Touched => _versions.Any(service => service.Touched);
    public bool NeedsRefresh => _versions.Any(service => service.NeedsRefresh);

    internal void RefreshCapabilities()
    {
        if (_closed) return;
        if (!_isCurrent() || _service?.IsCurrent != true)
        {
            if (!_mutating) Hide();
            return;
        }
        SyncControls();
    }

    private bool Current(FnNasSubtitles service, long generation) => !_closed && _isCurrent()
        && service.IsCurrent && ReferenceEquals(_service, service) && generation == _generation;

    private void Say(string message)
    {
        if (_closed) return;
        Status.Text = message;
        Status.Visibility = message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SyncControls()
    {
        var canManage = _service?.CanManage == true;
        var unconfirmed = _service?.IsUnconfirmed == true;
        var canWrite = _service?.CanWrite == true;
        ManagementArea.Visibility = canManage && !unconfirmed ? Visibility.Visible : Visibility.Collapsed;
        SearchArea.Visibility = canManage && !unconfirmed ? Visibility.Visible : Visibility.Collapsed;
        ServerFileArea.Visibility = _service?.CanMarkServerFile == true && !unconfirmed ? Visibility.Visible : Visibility.Collapsed;
        VersionBox.IsEnabled = !_mutating;
        RefreshTracksButton.IsEnabled = !_busy && canManage;
        LanguageBox.IsEnabled = SearchButton.IsEnabled = !_busy && canManage && !unconfirmed;
        UploadButton.IsEnabled = MarkServerFileButton.IsEnabled = !_busy && canWrite;
        ServerPathBox.IsEnabled = !_busy;
        Tracks.IsEnabled = Results.IsEnabled = !_busy && canWrite;
        // Recovery must show the read-back snapshot even when an unknown write keeps commands blocked.
        Tracks.Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;
        NoTracks.Visibility = canManage && _tracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!canManage || unconfirmed) CancelDelete();
        ConfirmDeleteButton.IsEnabled = !_busy && canWrite;
        CancelDeleteButton.IsEnabled = !_busy;
        HeaderCloseButton.IsEnabled = !_mutating;
        Busy.IsActive = _busy;
        Busy.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetBusy(bool busy, bool mutating = false)
    {
        _busy = busy;
        _mutating = busy && mutating;
        SyncControls();
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
        SubtitleBody.Width = DialogTitleRow.Width = width;
        SubtitleScroll.MaxHeight = Math.Max(1, root.Size.Height - 220);
    }

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        try { await ReloadAsync(); }
        catch (Exception error) { Say(Failure.Describe(error)); }
    }

    private async void OnVersionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_ready || _closed || VersionBox.SelectedItem is not ComboBoxItem { Tag: FnNasSubtitles chosen }) return;
        if (_mutating)
        {
            VersionBox.SelectedIndex = _versions.ToList().IndexOf(_service!);
            return;
        }
        if (ReferenceEquals(chosen, _service)) return;
        _read?.Cancel();
        _service = chosen;
        _generation++;
        _busy = false;
        _tracks.Clear();
        _results.Clear();
        NoSearchResults.Visibility = Visibility.Collapsed;
        CancelDelete();
        SourceFileText.Text = "媒体文件：" + chosen.Path;
        Say("");
        SyncControls();
        try { await ReloadAsync(); }
        catch (Exception error) { if (!_closed) Say(Failure.Describe(error)); }
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_ready || _busy || _closed) return;
        _generation++;
        _results.Clear();
        NoSearchResults.Visibility = Visibility.Collapsed;
    }

    private void OnClose(object sender, RoutedEventArgs args)
    {
        if (!_mutating) Hide();
    }

    private async void OnRefreshTracks(object sender, RoutedEventArgs args)
    {
        try { await ReloadAsync(); }
        catch (Exception error) { Say(Failure.Describe(error)); }
    }

    private void FillTracks(FnNasSubtitleSnapshot snapshot)
    {
        _tracks.Clear();
        foreach (var track in snapshot.ExternalTracks) _tracks.Add(new(track));
        NoTracks.Visibility = _tracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RebuildResults();
    }

    private void RebuildResults()
    {
        var candidates = _results.Select(row => row.Candidate).ToArray();
        _results.Clear();
        foreach (var candidate in candidates) _results.Add(new(candidate, _service?.CanDownload(candidate) == true));
    }

    private async Task ReloadAsync()
    {
        if (_busy || _closed || _service is not { CanManage: true } service) return;
        var generation = ++_generation;
        _read?.Dispose();
        _read = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _read.Token;
        CancelDelete();
        SetBusy(true);
        try
        {
            var snapshot = await service.ReloadAsync(token);
            if (!Current(service, generation)) return;
            FillTracks(snapshot);
            Say(service.IsUnconfirmed ? "上次写入结果未确认。已刷新列表；请核对后关闭并重新打开，系统不会再次发送。" : "");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (Current(service, generation)) Say("刷新字幕失败：" + Failure.Describe(error)); }
        finally { if (Current(service, generation)) SetBusy(false); }
    }

    private async void OnSearch(object sender, RoutedEventArgs args)
    {
        if (_busy || _closed || _service is not { CanManage: true, IsUnconfirmed: false } service
            || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string code }) return;
        var generation = ++_generation;
        _read?.Dispose();
        _read = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _read.Token;
        _results.Clear();
        NoSearchResults.Visibility = Visibility.Collapsed;
        CancelDelete();
        SetBusy(true);
        Say("");
        try
        {
            var candidates = await service.SearchAsync(code, token);
            if (!Current(service, generation)) return;
            foreach (var candidate in candidates) _results.Add(new(candidate, service.CanDownload(candidate)));
            NoSearchResults.Visibility = _results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (Current(service, generation)) Say("搜索字幕失败：" + Failure.Describe(error)); }
        finally { if (Current(service, generation)) SetBusy(false); }
    }

    private async Task MutateAsync(Func<FnNasSubtitles, long, Task<bool>> write)
    {
        if (_busy || _closed || _service is not { CanWrite: true } service) return;
        var generation = ++_generation;
        CancelDelete();
        SetBusy(true, mutating: true);
        Say("");
        try
        {
            if (!await write(service, generation)) return;
            if (!Current(service, generation)) return;
            Say("字幕修改已保存。");
            try
            {
                var snapshot = await service.ReloadAsync(_lifetime.Token);
                if (Current(service, generation)) FillTracks(snapshot);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (Current(service, generation)) Say("字幕修改已保存，但刷新列表失败。请点击刷新列表；不会重复写入：" + Failure.Describe(error));
            }
        }
        catch (FnNasSubtitleWriteUnconfirmedException)
        {
            if (Current(service, generation)) Say("写入结果未确认，请刷新列表核对后关闭并重新打开。系统不会再次发送。");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (Current(service, generation)) Say("字幕操作失败：" + Failure.Describe(error)); }
        finally
        {
            _mutating = false;
            if (Current(service, generation)) SetBusy(false);
            else if (!_closed) Hide();
        }
    }

    private async void OnDownload(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: FnNasSubtitleResultRow row } || _service?.CanDownload(row.Candidate) != true) return;
        await MutateAsync(async (service, _) => { await service.DownloadAsync(row.Candidate, _lifetime.Token); return true; });
    }

    private async void OnUpload(object sender, RoutedEventArgs args)
    {
        await MutateAsync(async (service, generation) =>
        {
            if (XamlRoot is not { } root) return false;
            var upload = await _pick(root, () => Current(service, generation));
            if (upload is null || !Current(service, generation)) return false;
            await service.UploadAsync(upload, _lifetime.Token);
            return true;
        });
    }

    private async void OnMarkServerFile(object sender, RoutedEventArgs args)
    {
        if (_service?.CanMarkServerFile != true) return;
        var path = ServerPathBox.Text;
        await MutateAsync(async (service, _) => { await service.MarkServerFileAsync(path, _lifetime.Token); return true; });
    }

    private void OnDelete(object sender, RoutedEventArgs args)
    {
        if (_busy || _closed || _service?.CanWrite != true
            || sender is not Button { DataContext: FnNasSubtitleRow { Track: { CanDelete: true } track } }) return;
        _pendingDelete = track;
        DeleteConfirmationText.Text = $"媒体文件：{_service.Path}\n字幕：{track.Label}\n将删除服务器上的这个字幕文件，此操作无法撤销。";
        DeleteConfirmation.Visibility = Visibility.Visible;
        CancelDeleteButton.Focus(FocusState.Programmatic);
    }

    private void CancelDelete()
    {
        _pendingDelete = null;
        DeleteConfirmation.Visibility = Visibility.Collapsed;
    }
    private void OnCancelDelete(object sender, RoutedEventArgs args) => CancelDelete();

    private async void OnConfirmDelete(object sender, RoutedEventArgs args)
    {
        if (_pendingDelete is not { } track) return;
        await MutateAsync(async (service, _) => { await service.DeleteAsync(track, confirmedByUser: true, _lifetime.Token); return true; });
    }
}
