using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Diagnostics;
using Momoka.Emby;

namespace Momoka.Shell.Views;

public sealed partial class SubtitleDialog
{
    private ServerSubtitleFile? _uploadFile;
    private string? _unconfirmedUpload;
    private string? _unconfirmedMutation;
    private bool ContextAvailable => !_closed && (_available?.Invoke() ?? true);
    private bool ReadOnlyRecovery => _unconfirmedDownload is not null || _unconfirmedUpload is not null || _unconfirmedMutation is not null;
    private string? RecoveryMessage => _unconfirmedUpload is { } file
        ? $"服务器未确认「{file}」的上传结果。请核对字幕列表，继续刷新或关闭后重新打开；不要重复上传。"
        : _unconfirmedDownload is not null ? UnconfirmedDownloadMessage
        : _unconfirmedMutation is { } operation
            ? $"服务器未确认{operation}的结果。请核对字幕列表，继续刷新或关闭后重新打开；不要重复提交。" : null;
    private bool CanUpload => ContextAvailable && _target.Upload is not null && _target.CanUpload?.Invoke() == true;
    private bool ReadIsCurrent(int generation) => ContextAvailable && _readGeneration == generation;

    private void FinishRead(int generation)
    {
        if (_readGeneration == generation && !_closed) SetBusy(false);
    }

    internal void RefreshUploadAvailability()
    {
        if (_closed) return;
        UploadSection.Visibility = CanUpload ? Visibility.Visible : Visibility.Collapsed;
        UpdateUploadControls();
    }

    private void UpdateUploadControls()
    {
        var ready = CanUpload && !_busy && !_tracksStale && !ReadOnlyRecovery;
        PickSubtitleButton.IsEnabled = ready;
        UploadSubtitleButton.IsEnabled = ready && _uploadFile is not null;
        UploadLanguageBox.IsEnabled = ready;
        UploadForced.IsEnabled = ready;
        UploadHearingImpaired.IsEnabled = ready;
    }

    private void OnVersionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_changingVersion || _closed || VersionBox.SelectedIndex == _selectedVersion) return;
        if (_mutating || _tracksStale || ReadOnlyRecovery || !ContextAvailable)
        {
            _changingVersion = true;
            VersionBox.SelectedIndex = _selectedVersion;
            _changingVersion = false;
            return;
        }
        if (VersionBox.SelectedItem is not ComboBoxItem { Tag: SubtitleDialogTarget target }) return;
        _readGeneration++;
        CancelDelete();
        _target = target;
        _selectedVersion = VersionBox.SelectedIndex;
        _results.Clear();
        Results.Visibility = Visibility.Collapsed;
        NoSearchResults.Visibility = Visibility.Collapsed;
        ClearUploadDraft();
        UploadLanguageBox.SelectedIndex = 0;
        UploadForced.IsOn = false;
        UploadHearingImpaired.IsOn = false;
        ForcedOnlyRow.Visibility = target.SupportsForcedOnly ? Visibility.Visible : Visibility.Collapsed;
        RefreshTracksButton.Visibility = target.Reload is null ? Visibility.Collapsed : Visibility.Visible;
        FillTracks(target.Source);
        Say("");
        SetBusy(false);
        RefreshUploadAvailability();
    }

    private void ClearUploadDraft()
    {
        _uploadFile = null;
        UploadFileText.Text = "";
        UploadDraft.Visibility = Visibility.Collapsed;
        UpdateUploadControls();
    }

    private async void OnPickUpload(object sender, RoutedEventArgs args)
    {
        if (_busy || !CanUpload || _tracksStale || ReadOnlyRecovery || XamlRoot is not { } root) return;
        var target = _target;
        var generation = ++_readGeneration;
        CancelDelete();
        SetBusy(true);
        try
        {
            var picked = await _pickUpload(root,
                () => ReadIsCurrent(generation) && ReferenceEquals(_target, target) && CanUpload).ConfigureAwait(true);
            if (!ReadIsCurrent(generation) || !ReferenceEquals(_target, target) || !CanUpload || picked is null) return;
            _uploadFile = picked;
            UploadFileText.Text = $"{picked.FileName} · {picked.ByteCount:N0} 字节";
            UploadDraft.Visibility = Visibility.Visible;
            Say("");
        }
        catch (Exception error)
        {
            if (ReadIsCurrent(generation)) Say($"读取字幕文件失败：{Failure.Describe(error)}");
        }
        finally { FinishRead(generation); }
    }

    private async void OnUploadSubtitle(object sender, RoutedEventArgs args) => await UploadSelectedAsync();

    private async Task UploadSelectedAsync()
    {
        if (_busy || !CanUpload || _tracksStale || ReadOnlyRecovery || _uploadFile is not { } file
            || UploadLanguageBox.SelectedItem is not ComboBoxItem { Tag: string language } || _target.Upload is not { } upload) return;
        var draft = new ServerSubtitleUpload(file, language, UploadForced.IsOn, UploadHearingImpaired.IsOn);
        CancelDelete();
        SetBusy(true, mutating: true);
        Say($"正在上传「{file.FileName}」到这个媒体文件…");
        try
        {
            await upload(draft).ConfigureAwait(true);
            Touched = true;
            NeedsRefresh = true;
            ClearUploadDraft();
            await RefreshAfterMutationAsync($"服务器已接收「{file.FileName}」。字幕列表可能稍后更新，请刷新核对。");
        }
        catch (ServerSubtitleUploadUnconfirmedException error)
        {
            _unconfirmedUpload = file.FileName;
            _tracksStale = true;
            NeedsRefresh = true;
            Log.Warn(Category, "字幕上传回执未确认", error);
            Say(RecoveryMessage!);
        }
        catch (OperationCanceledException) { Say("登录身份或页面已改变，请关闭后重新打开字幕管理。"); }
        catch (Exception error)
        {
            Log.Warn(Category, "上传字幕失败", error);
            Say($"上传字幕失败：{Failure.Describe(error)}");
        }
        finally
        {
            SetBusy(false);
            RefreshUploadAvailability();
        }
    }
}
