using Momoka.Configuration;

namespace Momoka.Translation;

/// <summary>
/// One file's translation loop. Polling continues during HTTP; only one request is in flight, stale
/// cues cancel it, and the bounded cache never leaves memory. No subtitle or key enters diagnostics.
/// </summary>
public static class SubtitleTranslationRuntime
{
    public static async Task RunAsync(ISubtitleTranslationPlaybackSession session,
        SubtitleTranslationSettings settings, string apiKey, string scriptPath,
        Func<SubtitleTranslationSettings, string, string, CancellationToken, Task<string>> translate,
        Action<string> notify, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Lifetime);
        var token = lifetime.Token;
        PendingTranslation? pending = null;
        var cache = new Dictionary<string, string>(StringComparer.Ordinal);
        SubtitleTranslationCue? current = null;
        SubtitleTranslationCue? lastAttempt = null;
        var failures = 0;
        var retryAfter = DateTimeOffset.MinValue;
        try
        {
            if (!settings.Enabled || !session.IsCurrent) return;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                notify("实时字幕翻译未启动：请在设置 → 字幕中保存所选服务的 API 密钥。原字幕不受影响。");
                return;
            }
            if (!await session.EnsureOverlayAsync(scriptPath, token).ConfigureAwait(false))
            {
                if (session.IsCurrent) notify("实时字幕翻译未启动：当前播放器不能加载译文图层。原字幕不受影响。");
                return;
            }
            while (session.IsCurrent && !token.IsCancellationRequested)
            {
                var cue = await session.ReadCueAsync(token).ConfigureAwait(false);
                if (cue != current)
                {
                    current = cue;
                    lastAttempt = null;
                    if (pending is not null && pending.Cue != current) pending.Cancel();
                    await session.ClearAsync(token).ConfigureAwait(false);
                }
                if (pending is { Task.IsCompleted: true } finished)
                {
                    pending = null;
                    try
                    {
                        var translated = await finished.Task.ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(translated))
                        {
                            if (cache.Count >= 256) cache.Remove(cache.Keys.First());
                            cache[finished.Cue.Text] = translated;
                            failures = 0;
                            if (finished.Cue == current && session.IsCurrent)
                                await session.ShowAsync(finished.Cue, translated, settings.FontSize, settings.Position, token)
                                    .ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested || finished.Cue != current) { }
                    catch (Exception)
                    {
                        if (!token.IsCancellationRequested && session.IsCurrent)
                        {
                            if (failures++ == 0)
                                notify("实时字幕翻译暂时失败，已保留原字幕。请检查 API 密钥、网络、区域和服务余额；稍后自动重试。");
                            retryAfter = DateTimeOffset.UtcNow.AddSeconds(failures >= 3 ? 30 : 3);
                        }
                    }
                    finally { finished.Dispose(); }
                }
                if (pending is null && current is { } next && next != lastAttempt)
                {
                    if (cache.TryGetValue(next.Text, out var saved))
                    {
                        lastAttempt = next;
                        await session.ShowAsync(next, saved, settings.FontSize, settings.Position, token).ConfigureAwait(false);
                    }
                    else if (DateTimeOffset.UtcNow >= retryAfter)
                    {
                        lastAttempt = next;
                        var request = CancellationTokenSource.CreateLinkedTokenSource(token);
                        // Enforce a local deadline even if a supplied HttpClient has a longer timeout.
                        request.CancelAfter(TimeSpan.FromSeconds(15));
                        pending = new(next, request, TranslateAsync(next.Text, request.Token));
                    }
                }
                await Task.Delay(200, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || !session.IsCurrent) { }
        catch (Exception)
        {
            if (session.IsCurrent && !token.IsCancellationRequested)
                notify("实时字幕翻译已停止：播放器字幕读数不可用。原字幕不受影响，下次播放会重新尝试。");
        }
        finally
        {
            if (pending is not null)
            {
                pending.Cancel();
                try { await pending.Task.ConfigureAwait(false); } catch (Exception) { }
                pending.Dispose();
            }
            // The session itself rejects a successor file, so shutdown cannot erase next episode's text.
            try { await session.ClearAsync(CancellationToken.None).ConfigureAwait(false); } catch (Exception) { }
            cache.Clear();
        }

        async Task<string> TranslateAsync(string text, CancellationToken requestToken) =>
            await translate(settings, text, apiKey, requestToken).ConfigureAwait(false);
    }

    private sealed record PendingTranslation(SubtitleTranslationCue Cue, CancellationTokenSource Cancellation, Task<string> Task)
        : IDisposable
    {
        public void Cancel()
        {
            try { Cancellation.Cancel(); } catch (ObjectDisposedException) { }
        }
        public void Dispose() => Cancellation.Dispose();
    }
}
