using System.Globalization;
using System.Text.Json;

namespace Momoka.MoviePilot;

public sealed partial class MoviePilotService
{
    public async Task<IReadOnlyList<MoviePilotSubscription>> SubscriptionsAsync(CancellationToken cancellationToken)
    {
        var identity = CaptureIdentity();
        // v3 的 page/count 均省略时返回全量，避免主页只显示默认分页的第一批。
        var data = await CallAsync((address, token) => _client.GetAsync(address, token, "subscribe/", cancellationToken),
            cancellationToken, identity).ConfigureAwait(false);
        var stamp = TransferConnectionStamp(identity);
        return MoviePilotSubscription.ParseList(data).Select(item => item with
        {
            ConnectionStamp = stamp,
            Media = item.Media with { ConnectionStamp = stamp }
        }).ToArray();
    }

    public bool IsCurrentSubscription(MoviePilotSubscription subscription) => IsCurrentConnection(subscription.ConnectionStamp);

    public bool SubscriptionNeedsReview(MoviePilotSubscription subscription) =>
        OperationState(SubscriptionOperationKey(subscription)) == MoviePilotOperationState.Uncertain;

    /// <summary>调用方必须先由用户核对服务器状态并确认；只解除客户端限制，不发送远程请求。</summary>
    public void ConfirmSubscriptionReviewed(MoviePilotSubscription subscription)
    {
        RequireResultConnection(subscription.ConnectionStamp);
        var key = SubscriptionOperationKey(subscription);
        lock (_operationGate)
            if (_operations.GetValueOrDefault(key) == MoviePilotOperationState.Uncertain) _operations.Remove(key);
    }

    private static string SubscriptionOperationKey(MoviePilotSubscription subscription) => OperationKey(
        subscription.ConnectionStamp, "subscription-management", subscription.Id.ToString(CultureInfo.InvariantCulture));

    public async Task<MoviePilotSubscription> SubscriptionAsync(MoviePilotSubscription subscription, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(subscription.ConnectionStamp);
        var data = await CallAsync((address, token) => _client.GetAsync(address, token,
            "subscribe/" + subscription.Id.ToString(CultureInfo.InvariantCulture), cancellationToken), cancellationToken, identity).ConfigureAwait(false);
        var current = MoviePilotSubscription.Parse(data);
        if (current is null || current.Id != subscription.Id) throw new MoviePilotException("订阅已不存在，请返回主页刷新");
        return current with
        {
            ConnectionStamp = subscription.ConnectionStamp,
            Media = current.Media with { ConnectionStamp = subscription.ConnectionStamp }
        };
    }

    public async Task<MoviePilotSubscriptionFiles> SubscriptionFilesAsync(MoviePilotSubscription subscription, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(subscription.ConnectionStamp);
        var data = await CallAsync((address, token) => _client.GetAsync(address, token,
            "subscribe/files/" + subscription.Id.ToString(CultureInfo.InvariantCulture), cancellationToken), cancellationToken, identity).ConfigureAwait(false);
        return MoviePilotSubscriptionFiles.Parse(data, subscription);
    }

    /// <summary>
    /// 取一条订阅每集的播出日期（集号 → 日期）。走 TMDB 季集接口 <c>tmdb/{tmdbid}/{season}</c> —— 订阅的文件统计
    /// 不带播出日期（见 <see cref="MoviePilotEpisodeAirDates"/>）。
    /// <para>
    /// 只有**来源是 TMDB、媒体编号是数字、季号明确**的电视剧才发这一趟；其他来源（豆瓣／IMDb 等）回空表，屏上就
    /// 不显示播出日期、也不判「已播出」。这是一趟附加读取：失败由调用方按「没有日期」处理，不影响订阅详情本身。
    /// </para>
    /// </summary>
    public async Task<IReadOnlyDictionary<int, DateTimeOffset>> SubscriptionAirDatesAsync(
        MoviePilotSubscription subscription, CancellationToken cancellationToken)
    {
        var media = subscription.Media;
        if (!subscription.IsSeries || media.Season is not { } season
            || !string.Equals(media.MediaSource, "themoviedb", StringComparison.OrdinalIgnoreCase)
            || media.MediaId is not { Length: > 0 } id || !id.All(char.IsAsciiDigit))
            return new Dictionary<int, DateTimeOffset>();

        var identity = RequireResultConnection(subscription.ConnectionStamp);
        var path = $"tmdb/{id}/{season.ToString(CultureInfo.InvariantCulture)}";
        // 订阅带剧集组时，服务器按剧集组算集号；查询不带它就取到另一套编号，对不上屏上那几集。
        if (subscription.EpisodeGroup.Length > 0) path += $"?episode_group={Uri.EscapeDataString(subscription.EpisodeGroup)}";
        var data = await CallAsync((address, token) => _client.GetAsync(address, token, path, cancellationToken),
            cancellationToken, identity).ConfigureAwait(false);
        return MoviePilotEpisodeAirDates.Parse(data);
    }

    /// <summary>
    /// 取这条订阅所属媒体可选的那些剧集组（<c>media/groups/{tmdbid}</c>），拼成屏上那份选项 —— 第一项固定是
    /// 「默认」（见 <see cref="MoviePilotEpisodeGroups"/>）。
    /// <para>
    /// 和播出日期同一个边界：只有**来源是 TMDB、媒体编号是数字**的电视剧才发这一趟，其他来源回空表、屏上那一节
    /// 就不出现。第二趟 <c>media/seasons</c> 只为算「默认」那一项的季数/集数，读不到就少一行计数（当附加读取，
    /// 不往外抛）；连接改变那一类取消照旧往外抛。
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<MoviePilotEpisodeGroup>> SubscriptionEpisodeGroupsAsync(
        MoviePilotSubscription subscription, CancellationToken cancellationToken)
    {
        var media = subscription.Media;
        if (!subscription.IsSeries
            || !string.Equals(media.MediaSource, "themoviedb", StringComparison.OrdinalIgnoreCase)
            || media.MediaId is not { Length: > 0 } id || !id.All(char.IsAsciiDigit))
            return [];

        var identity = RequireResultConnection(subscription.ConnectionStamp);
        var groups = await CallAsync((address, token) => _client.GetAsync(address, token,
            $"media/groups/{id}", cancellationToken), cancellationToken, identity).ConfigureAwait(false);

        var seasons = default(JsonElement);
        try
        {
            seasons = await CallAsync((address, token) => _client.GetAsync(address, token,
                $"media/seasons?media_source=themoviedb&media_id={id}", cancellationToken), cancellationToken, identity)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { /* 「默认」那一项少一行计数而已，不影响剧集组本身。 */ }

        return MoviePilotEpisodeGroups.Build(groups, seasons);
    }

    public async Task UpdateSubscriptionAsync(MoviePilotSubscription subscription, MoviePilotSubscriptionEdit edit, CancellationToken cancellationToken)
    {
        var body = edit.Changes(subscription);
        if (body.Count == 1) return;
        await MutateSubscriptionAsync(subscription, HttpMethod.Put, "subscribe/", body, cancellationToken).ConfigureAwait(false);
    }

    public async Task ManageSubscriptionAsync(MoviePilotSubscription subscription, MoviePilotSubscriptionAction action, CancellationToken cancellationToken)
    {
        var id = subscription.Id.ToString(CultureInfo.InvariantCulture);
        var (method, path) = action switch
        {
            MoviePilotSubscriptionAction.Pause => (HttpMethod.Put, $"subscribe/status/{id}?state=S"),
            MoviePilotSubscriptionAction.Resume => (HttpMethod.Put, $"subscribe/status/{id}?state=R"),
            MoviePilotSubscriptionAction.Search => (HttpMethod.Post, $"subscribe/search/{id}"),
            MoviePilotSubscriptionAction.Reset => (HttpMethod.Post, $"subscribe/reset/{id}"),
            MoviePilotSubscriptionAction.Delete => (HttpMethod.Delete, $"subscribe/{id}"),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        await MutateSubscriptionAsync(subscription, method, path, null, cancellationToken).ConfigureAwait(false);
        // 明确删除成功后才允许再次订阅；超时与失败不会清掉新增订阅的防重状态。
        if (action == MoviePilotSubscriptionAction.Delete)
            lock (_operationGate) _operations.Remove(SubscribeKey(subscription.Media, subscription.EpisodeGroup));
    }

    private async Task MutateSubscriptionAsync(MoviePilotSubscription subscription, HttpMethod method, string path,
        object? body, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(subscription.ConnectionStamp);
        var key = SubscriptionOperationKey(subscription);
        await WriteAsync(identity, [key], async (address, token) =>
        {
            try { return await _client.MutateAsync(address, token, method, path, body, cancellationToken).ConfigureAwait(false); }
            // 官方公开文档的旧版搜索/重置仍用 GET。只有明确 405 拒绝 POST 才退回；超时等不重放。
            catch (MoviePilotException error) when (error.Status == System.Net.HttpStatusCode.MethodNotAllowed && method == HttpMethod.Post)
            { return await _client.MutateAsync(address, token, HttpMethod.Get, path, body, cancellationToken).ConfigureAwait(false); }
        }, cancellationToken).ConfigureAwait(false);
        // 明确成功后可继续暂停/恢复/编辑；结果不明仍由 WriteAsync 锁住，禁止自动重放。
        lock (_operationGate) _operations.Remove(key);
    }
}
