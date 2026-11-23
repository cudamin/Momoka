using System.Collections.Concurrent;
using System.Globalization;
using System.Net;

namespace Momoka.MoviePilot;

public sealed partial class MoviePilotService
{
    /// <summary>
    /// 上一次成功读取留下的同一窗口缓存；连接身份（服务地址 + 用户名）不符、没有缓存或缓存不可读时返回 null。
    /// 只用于先把内容摆上屏，调用方随后仍应调用 <see cref="CalendarAsync"/> 拿最新安排。
    /// </summary>
    public IReadOnlyList<MoviePilotCalendarEntry>? CachedCalendar(DateOnly start, DateOnly endExclusive)
    {
        if (_calendarCache is null || endExclusive <= start || !_settings.MoviePilot.CalendarEnabled) return null;
        if (!MoviePilotAddress.TryNormalize(_settings.MoviePilot.Url, out var apiBase, out _) || apiBase is null) return null;
        return _calendarCache.Read(apiBase.AbsoluteUri + "\n" + (_settings.MoviePilot.Username?.Trim() ?? ""), start, endExclusive);
    }

    /// <summary>
    /// 日历缓存的连接身份：规范化服务地址 + 用户名。和 <see cref="ConnectionStamp"/> 不同，它不派生自密码或
    /// 令牌，所以可以安全地作为磁盘缓存的匹配键；换服务器、换账号后旧缓存自然失效。
    /// </summary>
    private static string CalendarCacheConnection(SessionIdentity identity) =>
        identity.ApiBase.AbsoluteUri + "\n" + identity.Username;

    /// <summary>读取左闭右开的真实播出日窗口。关闭入口、取消或连接换代后不交付旧结果。</summary>
    public async Task<IReadOnlyList<MoviePilotCalendarEntry>> CalendarAsync(
        DateOnly start, DateOnly endExclusive, CancellationToken cancellationToken = default)
    {
        if (endExclusive.DayNumber - start.DayNumber is not (> 0 and <= 93))
            throw new ArgumentOutOfRangeException(nameof(endExclusive), "日历日期窗口必须大于 0 且不超过 93 天");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        void OnCalendarChanged(object? sender, EventArgs args)
        {
            try { lifetime.Cancel(); }
            catch (ObjectDisposedException) { /* 并发设置事件可能已捕获刚退订的处理器。 */ }
        }
        _settings.MoviePilot.CalendarChanged += OnCalendarChanged;
        try
        {
            if (!_settings.MoviePilot.CalendarEnabled) throw new OperationCanceledException("MoviePilot 日历已关闭", lifetime.Token);
            var token = lifetime.Token;
            var identity = CaptureIdentity();
            var stamp = TransferConnectionStamp(identity);
            var entries = new Dictionary<(DateOnly Date, int Subscription), MoviePilotCalendarEntry>();
            const int count = 200;
            for (var page = 1; ; page++)
            {
                // 与官方日期窗口契约一致；分页上限防止失常服务器无限返回重复页面。
                if (page > 500) throw new MoviePilotException("MoviePilot 日历条目过多，请缩小日期范围后重试");
                var path = string.Create(CultureInfo.InvariantCulture,
                    $"subscribe/calendar?start={start:yyyy-MM-dd}&end={endExclusive:yyyy-MM-dd}&page={page}&count={count}");
                var data = await CallAsync((address, session, client) => client.GetAsync(address, session, path, token), token, identity)
                    .ConfigureAwait(false);
                var batch = MoviePilotCalendar.ParsePage(data, start, endExclusive, stamp);
                var previous = entries.Count;
                foreach (var entry in batch.Entries) entries[(entry.Date, entry.SubscriptionId)] = entry;
                if (batch.Count == 0 || (long)page * count >= batch.Total) break;
                if (entries.Count == previous)
                    throw new MoviePilotException("MoviePilot 日历分页没有前进，请刷新重试");
            }

            // lack_episode 是下载进度；只有文件统计中的 library 才能确认某一集已入库。
            var library = new ConcurrentDictionary<int, MoviePilotCalendarLibrarySnapshot>();
            var candidates = entries.Values.Where(entry => entry.Type == "电视剧" && entry.Episodes.Count > 0)
                .DistinctBy(entry => entry.SubscriptionId).ToArray();
            await Parallel.ForEachAsync(candidates, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (entry, readToken) =>
            {
                try
                {
                    var path = "subscribe/files/" + entry.SubscriptionId.ToString(CultureInfo.InvariantCulture);
                    var data = await CallAsync((address, session, client) => client.GetAsync(address, session, path, readToken), readToken, identity)
                        .ConfigureAwait(false);
                    library[entry.SubscriptionId] = MoviePilotCalendar.LibraryEpisodes(data, entry);
                }
                catch (OperationCanceledException) { throw; }
                catch (MoviePilotException error) when (!ConnectionFailed(error)) { /* 可选统计失败时保留未知，不猜入库集。 */ }
            }).ConfigureAwait(false);
            CheckIdentity(identity, token);
            var result = entries.Values.Select(entry => library.TryGetValue(entry.SubscriptionId, out var snapshot)
                    ? entry with
                    {
                        Episodes = entry.Episodes.Select(episode => episode with { IsInLibrary = snapshot.Episodes.Contains(episode.Number) }).ToArray(),
                        LibraryEpisodeCount = snapshot.Episodes.Count,
                        LibraryTotalEpisodes = snapshot.TotalEpisodes,
                        LastUpdate = snapshot.LastUpdate.Length > 0 ? snapshot.LastUpdate : entry.LastUpdate
                    }
                    : entry)
                .OrderBy(entry => entry.Date).ThenBy(entry => entry.Title, StringComparer.Ordinal).ThenBy(entry => entry.SubscriptionId).ToArray();

            // 读成功后立刻落本地：翻月、退出应用之后下次进来先显示这一份，再向 MoviePilot 要最新的。
            _calendarCache?.Write(CalendarCacheConnection(identity), start, endExclusive, result);
            return result;
        }
        catch (MoviePilotException error) when (MoviePilotCalendar.IsLegacyEndpoint(error))
        {
            throw new MoviePilotException("当前 MoviePilot 版本不支持日历日期窗口，请升级到 3.2.3 或更新版本", error.Status);
        }
        catch (MoviePilotException error) when (error.Status is { } status)
        {
            var message = status == HttpStatusCode.Forbidden ? "当前 MoviePilot 账号无权读取日历，请在网页端核对权限"
                : status == HttpStatusCode.Unauthorized ? "MoviePilot 登录已失效，请重新测试连接"
                : $"MoviePilot 日历读取失败（{(int)status}），请稍后刷新";
            throw new MoviePilotException(message, status);
        }
        catch (MoviePilotException)
        {
            // 业务信封和失败正文也可能回显敏感内容，日历页只给本地恢复指引。
            throw new MoviePilotException("MoviePilot 日历读取失败，请稍后刷新或重新测试连接");
        }
        finally { _settings.MoviePilot.CalendarChanged -= OnCalendarChanged; }
    }
}
