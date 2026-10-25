using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.Danmaku;
using Momoka.Mpv;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

public sealed partial class PlayerViewModel
{
    private string _danmakuInputContext = Guid.NewGuid().ToString("N");
    private CancellationTokenSource? _danmakuSend;
    private readonly SemaphoreSlim _danmakuInputGate = new(1, 1);
    private readonly HashSet<string> _danmakuNativeRequests = [];

    [ObservableProperty]
    public partial string DanmakuDraft { get; set; } = "";

    [ObservableProperty]
    public partial string DanmakuSendStatus { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DanmakuSendButtonText))]
    [NotifyPropertyChangedFor(nameof(DanmakuSendEnabled))]
    public partial bool DanmakuSending { get; set; }

    public bool DanmakuInputVisible => DanmakuAvailable && Settings.Danmaku.Show && Settings.Danmaku.ShowInput;
    public Visibility DanmakuInputVisibility => DanmakuInputVisible ? Visibility.Visible : Visibility.Collapsed;
    public bool DanmakuCanSend => DanmakuInputVisible && _danmakuFetched && _danmakuEpisode is not null && _playback.IsPlaying;
    public bool DanmakuSendEnabled => DanmakuCanSend && !DanmakuSending;
    public Visibility DanmakuSendVisibility => DanmakuCanSend ? Visibility.Visible : Visibility.Collapsed;
    public string DanmakuInputPlaceholder => DanmakuCanSend ? "发条弹幕（最多100字）" : "请先选择并加载弹幕库";
    public string DanmakuSendButtonText => DanmakuSending ? "发送中" : "发送";

    private void ResetDanmakuInput()
    {
        _danmakuInputContext = Guid.NewGuid().ToString("N");
        _danmakuNativeRequests.Clear();
        _danmakuSend?.Cancel();
        _danmakuSend?.Dispose();
        _danmakuSend = null;
        DanmakuSending = false;
        DanmakuDraft = "";
        DanmakuSendStatus = "";
        RefreshDanmakuInput();
    }

    private void RefreshDanmakuInput()
    {
        OnPropertyChanged(nameof(DanmakuInputVisible));
        OnPropertyChanged(nameof(DanmakuInputVisibility));
        OnPropertyChanged(nameof(DanmakuCanSend));
        OnPropertyChanged(nameof(DanmakuSendEnabled));
        OnPropertyChanged(nameof(DanmakuSendVisibility));
        OnPropertyChanged(nameof(DanmakuInputPlaceholder));
        // 隐藏只是外观偏好；已经主动提交的请求继续等确认，避免隐藏一瞬丢掉成功回执。
        _ = PushDanmakuInputAsync();
    }

    private async Task<bool> PushDanmakuInputAsync()
    {
        if (!HeadlessPlayback) return false;
        var interaction = CaptureInteraction();
        var context = _danmakuInputContext;
        await _danmakuInputGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (context != _danmakuInputContext || !IsCurrentPlayback(interaction)) return false;
            return await _playback.CommandAsync("script-message", MpvDanmaku.InputState, JsonSerializer.Serialize(new
            { visible = DanmakuInputVisible, canSend = DanmakuCanSend, busy = DanmakuSending, context })).ConfigureAwait(true);
        }
        finally { _danmakuInputGate.Release(); }
    }

    [RelayCommand]
    private async Task SendDanmakuAsync() => await SendDanmakuTextAsync(DanmakuDraft, _danmakuInputContext, null).ConfigureAwait(true);

    private async Task SendNativeDanmakuAsync(string payload)
    {
        var input = MpvDanmaku.ParseSend(payload);
        if (input is null || input.Context != _danmakuInputContext || !_danmakuNativeRequests.Add(input.RequestId)) return;
        await SendDanmakuTextAsync(input.Text, input.Context, input.RequestId).ConfigureAwait(true);
    }

    private async Task SendDanmakuTextAsync(string draft, string inputContext, string? nativeRequest)
    {
        if (inputContext != _danmakuInputContext) return;
        if (!DanmakuSendEnabled || _danmakuEpisode is not { } episode)
        {
            await NativeSendResultAsync(false, "请先选择并加载弹幕库，或等待当前发送结束。").ConfigureAwait(true);
            return;
        }
        var interaction = CaptureInteraction();
        var time = DandanPlayClient.CommentTime(Status.Position, _danmakuDelay);
        var success = false;
        var message = "";
        _danmakuSend?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _danmakuSend = cancellation;
        DanmakuSending = true;
        DanmakuSendStatus = "正在发送…";
        await PushDanmakuInputAsync().ConfigureAwait(true);
        bool Current() => inputContext == _danmakuInputContext && IsCurrentPlayback(interaction);
        try
        {
            var text = DandanPlayClient.ValidateComment(draft, time);
            if (!Current() || !DanmakuInputVisible) return;
            await _dandanPlay.SendCommentAsync(episode.EpisodeId, time, text, cancellation.Token).ConfigureAwait(true);
            if (!Current()) return;
            success = true;
            message = "已发送";
            if (nativeRequest is null && DanmakuDraft == draft) DanmakuDraft = "";
            // 只把服务确认的评论加入当前库；不为刷新本人的一条弹幕再消耗获取额度。
            _danmakuComments = _danmakuComments.Take(DanmakuComments.MaximumCount - 1)
                .Append(new DanmakuComment(time, 1, 0xFFFFFF, text)).OrderBy(comment => comment.Time).ToArray();
            await PushDanmakuAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) { message = "发送已中断，结果未确认；请勿立即重复发送。"; }
        catch (DandanPlayException error) { message = error.Message; }
        finally
        {
            if (inputContext == _danmakuInputContext)
            {
                DanmakuSending = false;
                DanmakuSendStatus = message;
                await PushDanmakuInputAsync().ConfigureAwait(true);
                await NativeSendResultAsync(success, message).ConfigureAwait(true);
            }
        }

        Task<bool> NativeSendResultAsync(bool sent, string resultMessage) => nativeRequest is null || !HeadlessPlayback
            ? Task.FromResult(false)
            : _playback.CommandAsync("script-message", MpvDanmaku.InputResult, JsonSerializer.Serialize(new
            { context = inputContext, requestId = nativeRequest, success = sent, message = resultMessage }));
    }
}
