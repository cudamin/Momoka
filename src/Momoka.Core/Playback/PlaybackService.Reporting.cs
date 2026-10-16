using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;

namespace Momoka.Playback;

public sealed partial class PlaybackService
{
    private async Task<PlaybackExit> MonitorAsync(EmbySessionScope scope, PlayingAttempt attempt, CancellationToken cancellationToken)
    {
        var handle = attempt.Handle;
        var request = attempt.Request;
        var exitTask = handle.WaitForExitAsync(cancellationToken);
        var interval = TimeSpan.FromSeconds(Math.Clamp(settings.Playback.ProgressReportIntervalSeconds, 1, 60));
        var stopEnabled = scope.Connection.ServerKind == MediaServerKind.Emby && settings.Playback.StopReportEnabled && settings.Playback.ReportProgressToServer;
        var stopPercent = StopReportPercent(request.IsDonghua,
            settings.Playback.MarkWatchedPercent, settings.Playback.DonghuaMarkWatchedPercent);

        void OnPause(bool paused) => _ = ReportPauseAsync(scope, attempt, paused);
        void OnStatus(PlayerStatus status)
        {
            if (!IsCurrent(attempt) || !status.HasPosition || !double.IsFinite(status.Position)) return;
            var ticks = TimeFormat.ToTicks(status.Position);
            if (ShouldReportStop(ticks, request.RunTimeTicks, stopEnabled, stopPercent))
                _ = ReportThresholdAsync(scope, attempt, ticks);
        }
        handle.PauseChanged += OnPause;
        if (handle.HasControlChannel && handle is IPlayerControl control) control.StatusChanged += OnStatus;
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (true)
            {
                var tick = timer.WaitForNextTickAsync(cancellationToken).AsTask();
                if (await Task.WhenAny(exitTask, tick).ConfigureAwait(false) == exitTask) break;
                if (!await tick.ConfigureAwait(false)) break;
                if (!IsCurrent(attempt)) break;
                var position = await handle.GetPositionAsync(cancellationToken).ConfigureAwait(false);
                if (!IsCurrent(attempt) || exitTask.IsCompleted) break;
                if (position is not { } seconds || !double.IsFinite(seconds) || seconds < 0) continue;
                attempt.HasStarted |= seconds > request.StartSeconds + 0.5;
                var ticks = TimeFormat.ToTicks(seconds);
                Raise(ProgressChanged, new PlaybackProgress(ticks, request.RunTimeTicks, handle.IsPaused, request.Title, attempt.Generation));
                if (ShouldReportStop(ticks, request.RunTimeTicks, stopEnabled, stopPercent))
                    await ReportThresholdAsync(scope, attempt, ticks).ConfigureAwait(false);
                await ReportProgressAsync(scope, attempt, ticks, handle.IsPaused, "timeupdate", cancellationToken).ConfigureAwait(false);
            }
            return await exitTask.ConfigureAwait(false);
        }
        finally
        {
            attempt.Ending = true;
            if (handle is IPlayerControl player) player.StatusChanged -= OnStatus;
            handle.PauseChanged -= OnPause;
        }
    }

    private async Task ReportThresholdAsync(EmbySessionScope scope, PlayingAttempt attempt, long ticks)
    {
        if (!IsCurrent(attempt) || Interlocked.CompareExchange(ref attempt.ThresholdPending, 1, 0) != 0) return;
        try
        {
            await attempt.Reports.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsCurrent(attempt) || attempt.StopReported) return;
                attempt.StopReported = await ReportAsync(scope, "到阈值补报停止", (client, token) =>
                    client.ReportPlaybackStoppedAsync(Build(attempt, ticks, false, null), token)).ConfigureAwait(false);
            }
            finally { attempt.Reports.Release(); }
        }
        finally { Volatile.Write(ref attempt.ThresholdPending, 0); }
    }

    /// <summary>
    /// 手动「标记为已看」之后替服务器补报一趟播放停止 —— 「通知接管」（<see cref="PlaybackSettings.StopReportEnabled"/>）
    /// 开着时才做（用户令 2026-10-09：「新增打开通知接管后，点击标记为已看后自动发送通知，发送的播放进度设置
    /// 进度为100%」）。
    /// <para>
    /// 与「进度到阈值补报」是同一个出口（<c>Sessions/Playing/Stopped</c>）：服务器收到就按用户配的通知条目把
    /// <c>playback.stop</c> 转发出去（本机那条是 Webhooks → MoviePilot）。差别只在位置 —— 那一趟报的是真实播放
    /// 位置，这一趟按用户令固定报 <b>100%</b>：用户按下「已看」，就是对服务器说「这一条看完了」，进度条该是满的。
    /// 所以位置取 <see cref="EmbyItem.RunTimeTicks"/>；时长读不到就没有 100% 可言，这一趟不发（宁可少发一趟，
    /// 不可发一个凑出来的数）。
    /// </para>
    /// <para>
    /// <b>整季 / 整部剧的落点是最后一集。</b>用户令 2026-10-11：「开启通知接管后，标记整季已看时要发送最后一集
    /// 的已看通知」；整部剧即最后一季的最后一集。季条目自己没有可靠的时长，通知那头（MoviePilot）要认的也是
    /// 「集」而不是「季」，所以先把落点换成 <see cref="ResolveLastEpisodeAsync"/> 挑出的那一集，再走与单集同一
    /// 条路。换不到就不发，与「时长读不到就不发」同一条规矩。
    /// </para>
    /// <para>
    /// 这一条正在播的话借它那一场的会话号，并当场结账（<c>StopReported</c>）—— 此后本场不再向服务器喂进度与
    /// 暂停，与「到阈值补报」同一条规矩（不然刚说停，下一拍进度又把会话激活了）。
    /// </para>
    /// <para>
    /// <b>没在播时不能只发停止。</b>2026-10-09 实测：只发停止，服务器答 2xx 却不转发通知 —— 它的停止处理要在
    /// 本设备的会话里认出「正在播这一条」才作数，而在浏览界面上点已看时那条会话里什么都没有。所以这一支先补报
    /// 一趟「开始」（位置 0）把这一条放进会话，再补报「停止」（位置报满）。代价是服务器上多一条短暂的播放记录，
    /// 订阅了「开始」的通知也会跟着收到一条。
    /// </para>
    /// </summary>
    public async Task<bool> ReportMarkedWatchedAsync(EmbyItem item, CancellationToken cancellationToken = default)
    {
        if (session.Connection?.ServerKind != MediaServerKind.Emby || !settings.Playback.StopReportEnabled || !settings.Playback.ReportProgressToServer)
        {
            Log.Debug(Category, $"《{item.Name}》标记已看：通知接管没开（或上报已关），不补报");
            return false;
        }

        if (item.Type is EmbyItemType.Season or EmbyItemType.Series)
        {
            if (await ResolveLastEpisodeAsync(item, cancellationToken).ConfigureAwait(false) is not { } episode)
                return false;
            item = episode;
        }

        return await ReportItemWatchedAsync(item, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 落点已是单集（或电影）那一半：时长检查、借会话、开始加停止。从 <see cref="ReportMarkedWatchedAsync"/>
    /// 拆出来只为给整季 / 整部剧让出一个「先换落点」的位置，规矩一条没改。
    /// </summary>
    private async Task<bool> ReportItemWatchedAsync(EmbyItem item, CancellationToken cancellationToken)
    {
        if (item.RunTimeTicks is not > 0)
        {
            Log.Debug(Category, $"《{item.Name}》标记已看：读不到时长，凑不出 100%，不补报");
            return false;
        }

        // 未登录或会话已释放：没有身份可借，不发。调用点那边标记已看刚刚才成功过，走到这一支只说明这中间注销
        // 了 —— 那不是错，是没有收信人。
        EmbySessionScope scope;
        try { scope = session.Capture(item); }
        catch (Exception error)
        {
            Log.Warn(Category, $"《{item.Name}》标记已看：会话不可用，没补报", error);
            return false;
        }

        var ticks = item.RunTimeTicks.Value;
        if (scope.Connection.ServerKind != MediaServerKind.Emby) return false;

        if (_active is { Ending: false } attempt && attempt.Request.ItemId == item.Id && attempt.Scope.Key == scope.Key)
        {
            await attempt.Reports.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // 已经报过一趟（到阈值那一趟）就别再报：服务器上那条通知会转发两次。
                if (attempt.StopReported)
                {
                    Log.Debug(Category, $"《{item.Name}》标记已看：本场已经报过一趟停止，不重复");
                    return true;
                }

                if (!IsCurrent(attempt)) return false;
                attempt.StopReported = await ReportAsync(scope, "标记已看补报停止", (client, token) =>
                    client.ReportPlaybackStoppedAsync(BuildWatched(item, attempt.SessionId), token), cancellationToken)
                    .ConfigureAwait(false);
                Log.Info(Category, $"《{item.Name}》标记已看：借当前播放那一场的会话补报停止，"
                    + $"位置 {TimeFormat.Clock(ticks)}，{(attempt.StopReported ? "服务器收下了" : "没发出去")}");
                return attempt.StopReported;
            }
            finally { attempt.Reports.Release(); }
        }

        // 这一条没在播：**先补报一趟「开始」**，让服务器把这一条放进这场会话的「正在播放」里 —— 服务器的
        // 停止处理要靠它才认得出「这一条播完了」。2026-10-09 实测：只发停止时服务器答 2xx、却不转发通知
        // （会话里没有这一条，那趟上报对不上号）。代价是服务器上会多一条短暂的播放记录，订阅了「开始」的
        // 通知也会收到一条。
        var sessionId = Guid.NewGuid().ToString("N");
        var opening = BuildWatched(item, sessionId);
        opening.PositionTicks = 0;
        var started = await ReportAsync(scope, "标记已看补报开始", (client, token) =>
            client.ReportPlaybackStartAsync(opening, token), cancellationToken).ConfigureAwait(false);
        var sent = started && await ReportAsync(scope, "标记已看补报停止", (client, token) =>
            client.ReportPlaybackStoppedAsync(BuildWatched(item, sessionId), token), cancellationToken).ConfigureAwait(false);
        Log.Info(Category, $"《{item.Name}》标记已看：这一条没在播，先补报开始、再补报停止，"
            + $"位置 {TimeFormat.Clock(ticks)}，{(sent ? "服务器收下了" : "没发出去")}");
        return sent;
    }

    /// <summary>
    /// 整季 / 整部剧标记已看时挑补报落点的那一集（用户令 2026-10-11，见
    /// <see cref="ReportMarkedWatchedAsync"/>）：向服务器问一遍这个容器的剧集
    /// （<see cref="EmbyClient.GetEpisodesAsync"/>，季按 <c>SeasonId</c> 收窄、剧问全剧），挑出
    /// <b>编号最大的、已播出、有时长</b>的那一集。
    /// <para>
    /// 未播出的集不该替「看完了」发通知 —— 追番途中整季标已看是常事，落点若落到还没播的那集上，
    /// MoviePilot 会把没播的集记成看过；判据沿用遗漏集表那一条（<see cref="ItemDetail.MissingIsUnaired"/>，
    /// 首播日期在今天之后）。有时长这一条是「100%」本身立不立得住（见
    /// <see cref="ReportItemWatchedAsync"/>），未播出的虚拟条目在服务器上恰好也没有时长，两个条件一条不多。
    /// 排序把季号放在集号前面：特别篇挂在第 0 季，自然让位给正片；整部剧那一趟问回来的是全剧的集，
    /// 于是自动落到「最后一季的最后一集」。
    /// </para>
    /// <para>
    /// 换不出落点（没挂到剧上、剧集列表没等到、全集未播出或没时长）就不发：通知这一趟是替服务器跑腿，
    /// 宁可少一趟，不发一个指错了条目的。
    /// </para>
    /// </summary>
    private async Task<EmbyItem?> ResolveLastEpisodeAsync(EmbyItem container, CancellationToken cancellationToken)
    {
        // 未登录或会话已释放：与单集那一支同一句话 —— 那不是错，是没有收信人。
        EmbySessionScope scope;
        try { scope = session.Capture(container); }
        catch (Exception error)
        {
            Log.Warn(Category, $"《{container.Name}》标记已看：会话不可用，查不了它的剧集", error);
            return null;
        }

        var isSeason = container.Type == EmbyItemType.Season;
        var seriesId = isSeason ? container.SeriesId : container.Id;
        var seasonId = isSeason ? container.Id : null;
        if (string.IsNullOrEmpty(seriesId))
        {
            Log.Debug(Category, $"《{container.Name}》标记已看：没带所属剧集的 id，找不出最后一集，不补报");
            return null;
        }

        List<EmbyItem> episodes;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            episodes = await scope.ExecuteAsync((client, token) =>
                client.GetEpisodesAsync(seriesId, seasonId, token), deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Log.Debug(Category, $"《{container.Name}》标记已看：剧集列表没等到（超时或取消），不补报");
            return null;
        }
        catch (Exception error)
        {
            Log.Warn(Category, $"《{container.Name}》标记已看：剧集列表查取失败，不补报", error);
            return null;
        }

        var last = episodes
            .Where(one => one.IndexNumber is not null && one.RunTimeTicks is > 0
                && !ItemDetail.MissingIsUnaired(one, DateTimeOffset.Now))
            .OrderByDescending(one => one.ParentIndexNumber ?? 0)
            .ThenByDescending(one => one.IndexNumber)
            .FirstOrDefault();

        if (last is null)
        {
            Log.Debug(Category, $"《{container.Name}》标记已看：{episodes.Count} 集里没有已播出且有时长的，凑不出 100%，不补报");
            return null;
        }

        var code = last.EpisodeCode;
        Log.Info(Category, $"《{container.Name}》标记已看：补报落点换成 "
            + $"{(code.Length > 0 ? code + " " : "")}《{last.Name}》");
        return last;
    }

    /// <summary>
    /// 标记已看那一趟的报文：位置按用户令固定 100%（<see cref="EmbyItem.RunTimeTicks"/>，取不到的调用点根本不会
    /// 走到这儿）。没有轨道与音量可言 —— 用户按的是「已看」，不是「正在看」。
    /// </summary>
    private static PlaybackReport BuildWatched(EmbyItem item, string sessionId) => new()
    {
        ItemId = item.Id,
        MediaSourceId = item.DefaultMediaSource?.Id,
        PlaySessionId = sessionId,
        PositionTicks = item.RunTimeTicks ?? 0,
        IsPaused = false
    };

    private async Task ReportPauseAsync(EmbySessionScope scope, PlayingAttempt attempt, bool paused)
    {
        try
        {
            if (!IsCurrent(attempt)) return;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var position = await attempt.Handle.GetPositionAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
            if (!IsCurrent(attempt) || position is not { } seconds || !double.IsFinite(seconds) || seconds < 0) return;
            var ticks = TimeFormat.ToTicks(seconds);
            Raise(ProgressChanged, new PlaybackProgress(ticks, attempt.Request.RunTimeTicks, paused,
                attempt.Request.Title, attempt.Generation));
            await ReportProgressAsync(scope, attempt, ticks, paused, paused ? "pause" : "unpause", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Log.Warn(Category, "暂停状态读取失败", error); }
    }

    private async Task ReportProgressAsync(EmbySessionScope scope, PlayingAttempt attempt,
        long ticks, bool paused, string eventName, CancellationToken cancellationToken)
    {
        await attempt.Reports.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCurrent(attempt) || attempt.StopReported) return;
            await ReportAsync(scope, "进度", (client, token) => client.ReportPlaybackProgressAsync(
                Build(attempt, ticks, paused, eventName), token), cancellationToken).ConfigureAwait(false);
        }
        finally { attempt.Reports.Release(); }
    }

    private async Task<PlaybackResult> FinishAsync(EmbySessionScope scope, PlayingAttempt attempt, PlaybackExit exit, PlaybackTicket ticket)
    {
        var request = attempt.Request;
        var positionTicks = ResolveFinalPosition(exit, request, ticket);
        attempt.HasStarted |= exit.PositionSeconds is { } seconds && double.IsFinite(seconds) && seconds > request.StartSeconds + 0.5;
        await attempt.Reports.WaitAsync().ConfigureAwait(false);
        try
        {
            var report = Build(attempt, positionTicks, false, null);
            report.Failed = exit.IsFailure;
            var reported = attempt.StopReported || await ReportAsync(scope, "停止", (client, token) =>
                client.ReportPlaybackStoppedAsync(report, token)).ConfigureAwait(false);
            attempt.StopReported = reported;
            var watched = ShouldMarkWatched(exit, request, positionTicks)
                && await ReportAsync(scope, "标记已观看", (client, token) => client.MarkPlayedAsync(request.ItemId, token)).ConfigureAwait(false);
            var result = new PlaybackResult(exit, positionTicks, watched, reported) { HasStarted = attempt.HasStarted };
            Log.Info(Category, $"《{request.Title}》{result.ToChinese()}，位置 {TimeFormat.Clock(positionTicks)}");
            return result;
        }
        finally { attempt.Reports.Release(); }
    }

    private static long ResolveFinalPosition(PlaybackExit exit, PlaybackRequest request, PlaybackTicket ticket)
    {
        if (exit.Reason == PlaybackEndReason.EndOfFile && request.RunTimeTicks > 0) return request.RunTimeTicks;
        if (exit.PositionSeconds is { } seconds && double.IsFinite(seconds) && seconds >= 0) return TimeFormat.ToTicks(seconds);
        return Math.Max(0, ticket.StartTicks);
    }

    private bool ShouldMarkWatched(PlaybackExit exit, PlaybackRequest request, long positionTicks)
    {
        if (!settings.Playback.ReportProgressToServer) return false;
        if (exit.Reason == PlaybackEndReason.EndOfFile) return true;
        if (exit.PositionSeconds is null || request.RunTimeTicks <= 0) return false;
        var percent = StopReportPercent(request.IsDonghua, settings.Playback.MarkWatchedPercent, settings.Playback.DonghuaMarkWatchedPercent);
        return positionTicks >= request.RunTimeTicks * (Math.Clamp(percent, MarkWatchedFloor, 100) / 100d);
    }

    private static PlaybackReport Build(PlayingAttempt attempt, long positionTicks, bool paused, string? eventName) =>
        Build(attempt.Handle, attempt.Request, attempt.SessionId, attempt.TrackState.Selection, positionTicks, paused, eventName);

    /// <summary>Offline fixture seam: builds a report for the current handle without a live attempt.</summary>
    internal PlaybackReport BuildReportForTest(string eventName)
    {
        var request = new PlaybackRequest { MediaUrl = new Uri("http://offline.invalid/a.mkv"), Title = "测试" };
        return Build(_current, request, "test-session",
            (request.AudioStreamIndex, request.SubtitleStreamIndex), 0, false, eventName);
    }

    private static PlaybackReport Build(IPlaybackHandle? handle, PlaybackRequest request, string sessionId,
        (int? Audio, int? Subtitle) selection, long positionTicks, bool paused, string? eventName)
    {
        // 音量还要再问一层 VolumeKnown：通道建立了而内核的第一条真实读数未到时，Volume 仍是缺省 100 ——
        // 起播那条上报会把它当事实发给服务器，远端就显示一个没人设过的数（独立审查 2026-10-04）。未知就报 null，
        // 与「没有通道」同一待遇；静音标志不受这条影响，它没有「默认伪装成读数」的问题。
        var live = handle is IPlayerControl control && handle.HasControlChannel ? control : null;
        var status = live?.Status;
        var volume = status is { VolumeKnown: true, Volume: var known } && double.IsFinite(known)
            ? (int?)Math.Round(Math.Clamp(known, 0, AudioSettings.MaxVolume))
            : null;
        return new PlaybackReport
        {
            FnNasSelection = request.FnNasSelection,
            ItemId = request.ItemId,
            MediaSourceId = request.MediaSourceId,
            PlaySessionId = sessionId,
            PositionTicks = positionTicks,
            IsPaused = paused,
            IsMuted = status?.Muted,
            VolumeLevel = volume,
            EventName = eventName,
            CanSeek = live is not null,
            AudioStreamIndex = selection.Audio,
            SubtitleStreamIndex = selection.Subtitle
        };
    }

    /// <summary>Reports are bounded and retain the scope's cancellation all the way into HTTP.</summary>
    private async Task<bool> ReportAsync(EmbySessionScope scope, string what,
        Func<EmbyClient, CancellationToken, Task> report, CancellationToken cancellationToken = default)
    {
        if (!settings.Playback.ReportProgressToServer) return false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await scope.ExecuteAsync(report, deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception error) { Log.Warn(Category, $"向服务器上报「{what}」失败", error); return false; }
    }
}
