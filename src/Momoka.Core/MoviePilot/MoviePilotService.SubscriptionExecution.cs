using System.Globalization;
using System.Text.Json;
using Momoka.Diagnostics;

namespace Momoka.MoviePilot;

/// <summary>批量操作里一条订阅的结局；结果不明与占用中都必须单独成档，不能混进「失败」一键重试。</summary>
public enum MoviePilotSubscriptionBatchOutcome { Success, Failed, Uncertain, Blocked }

public sealed record MoviePilotSubscriptionBatchItemResult(int Id, MoviePilotSubscriptionBatchOutcome Outcome, string? Error);

/// <summary>批量操作的分项回执；失败项（含结果不明与占用中）由调用方保留在选择集里供核对后重试。</summary>
public sealed record MoviePilotSubscriptionBatchResult(IReadOnlyList<MoviePilotSubscriptionBatchItemResult> Items)
{
    public int SuccessCount => Items.Count(item => item.Outcome == MoviePilotSubscriptionBatchOutcome.Success);
    public int FailedCount => Items.Count - SuccessCount;
    public int UncertainCount => Items.Count(item => item.Outcome == MoviePilotSubscriptionBatchOutcome.Uncertain);
    public IReadOnlyList<int> FailedIds => Items.Where(item => item.Outcome != MoviePilotSubscriptionBatchOutcome.Success)
        .Select(item => item.Id).ToArray();
}

public sealed partial class MoviePilotService
{
    /// <summary>
    /// 最近的订阅搜索批次（<c>subscribe/execution/batches?limit=10</c>，只读）。订阅墙按网页端节奏轮询它：
    /// 高频只读这份轻量进度，签名变了才补刷完整订阅列表。读取失败由调用方保留上次快照，不判订阅列表失败。
    /// </summary>
    public async Task<IReadOnlyList<MoviePilotSubscriptionBatch>> SubscriptionExecutionBatchesAsync(CancellationToken cancellationToken)
    {
        var identity = CaptureIdentity();
        var data = await CallAsync((address, token, client) => client.GetAsync(address, token,
            "subscribe/execution/batches?limit=10", cancellationToken), cancellationToken, identity).ConfigureAwait(false);
        var stamp = TransferConnectionStamp(identity);
        return MoviePilotSubscriptionBatch.ParseList(data).Select(batch => batch with { ConnectionStamp = stamp }).ToArray();
    }

    /// <summary>
    /// 停止横幅所代表的那次完整搜索（<c>PUT subscribe/execution/batches/{id}/cancel</c>）。
    /// 只停止这次批次，不影响后台记录或后续搜索；「已经结束，暂时无法停止」是明确回执，
    /// 按服务器原话抛出、不锁核对（同整理流程：信封本身就是确定答案）。
    /// </summary>
    public async Task CancelSubscriptionExecutionBatchAsync(MoviePilotSubscriptionBatch batch, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(batch.ConnectionStamp);
        var key = OperationKey(batch.ConnectionStamp, "subscription-execution-batch", batch.BatchId);
        var reply = await WriteAsync(identity, [key], (address, token, client) =>
            client.MutateReplyAsync(address, token, HttpMethod.Put,
                $"subscribe/execution/batches/{Uri.EscapeDataString(batch.BatchId)}/cancel", null, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        lock (_operationGate) _operations.Remove(key);
        if (!reply.Success) throw new MoviePilotException(ReplyMessage(reply));
    }

    /// <summary>
    /// 订阅墙的排序偏好（纯界面偏好，网页端存 localStorage）。读不到就是 null，由调用方按默认排序起步；
    /// 不要求连接代次 —— 它不是服务器数据，换账号也沿用同一份显示习惯。
    /// </summary>
    public string? SubscriptionSortPreference(string mediaType) =>
        _settings.MoviePilot.SubscriptionSorts.GetValueOrDefault(mediaType);

    /// <summary>
    /// 落盘排序偏好。<paramref name="sortKey"/> 为空表示清除（回到「未显式选择」）。写盘失败只记日志、
    /// 不抛出也不回滚内存值 —— 与网页端 localStorage 写入失败仅告警一致，界面照常按新排序显示。
    /// </summary>
    public void SaveSubscriptionSortPreference(string mediaType, string? sortKey)
    {
        var sorts = _settings.MoviePilot.SubscriptionSorts;
        if (string.IsNullOrEmpty(sortKey))
        {
            if (!sorts.Remove(mediaType)) return;
        }
        else
        {
            if (sorts.GetValueOrDefault(mediaType) == sortKey) return;
            sorts[mediaType] = sortKey;
        }
        try { _saveSettings?.Invoke(); }
        catch (Exception error) { Log.Info(Category, $"保存订阅排序偏好失败：{error.Message}"); }
    }

    /// <summary>
    /// 订阅墙的自定义顺序（<c>GET user/config/{SubscribeMovieOrder|SubscribeTvOrder}</c>）。
    /// 没存过时服务器回 <c>{"value":null}</c>，读作空表；传输失败照常抛出，由调用方按空表兜底。
    /// </summary>
    public async Task<IReadOnlyList<int>> SubscriptionOrderAsync(bool isSeries, CancellationToken cancellationToken)
    {
        var identity = CaptureIdentity();
        var data = await CallAsync((address, token, client) => client.GetAsync(address, token,
            "user/config/" + MoviePilotSubscriptionWall.OrderConfigKey(isSeries), cancellationToken),
            cancellationToken, identity).ConfigureAwait(false);
        var value = MoviePilotSubscription.Member(data, "value");
        if (value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray().Select(item => MoviePilotSubscription.Number(item, "id") ?? 0)
            .Where(id => id > 0).Distinct().ToArray();
    }

    /// <summary>
    /// 落盘自定义顺序（<c>POST user/config/{key}</c>，正文就是 <c>[{"id":…}]</c> 数组本身）。
    /// 失败抛出，调用方负责回滚到落盘前的顺序 —— 屏上顺序不能和服务器存的对不上。
    /// 超出大小上限是明确拒绝（success:false），不锁核对。
    /// </summary>
    public async Task SaveSubscriptionOrderAsync(bool isSeries, IReadOnlyList<int> order, CancellationToken cancellationToken)
    {
        var identity = CaptureIdentity();
        var configKey = MoviePilotSubscriptionWall.OrderConfigKey(isSeries);
        var key = OperationKey(TransferConnectionStamp(identity), "subscription-order", configKey);
        var body = order.Select(id => new { id }).ToArray();
        var reply = await WriteAsync(identity, [key], (address, token, client) =>
            client.MutateReplyAsync(address, token, HttpMethod.Post, "user/config/" + configKey, body, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        lock (_operationGate) _operations.Remove(key);
        if (!reply.Success) throw new MoviePilotException(ReplyMessage(reply));
    }

    /// <summary>
    /// 批量暂停/启用/删除选中的订阅，逐条走单条写入同一套操作闸：结果不明的条目锁住待核对，
    /// 占用中的条目不重复发送。删除以服务器回执 <c>status=="deleted"</c> 为准（网页端 batchDelete 同判据），
    /// 明确删除成功才清掉新增订阅的防重状态。中途取消照页面生命周期往外抛，不再发剩余条目。
    /// </summary>
    public async Task<MoviePilotSubscriptionBatchResult> ManageSubscriptionBatchAsync(
        IReadOnlyList<MoviePilotSubscription> subscriptions, MoviePilotSubscriptionAction action, CancellationToken cancellationToken)
    {
        if (action is not (MoviePilotSubscriptionAction.Pause or MoviePilotSubscriptionAction.Resume or MoviePilotSubscriptionAction.Delete))
            throw new ArgumentOutOfRangeException(nameof(action), "批量操作只支持暂停、启用与删除");
        var results = new List<MoviePilotSubscriptionBatchItemResult>();
        foreach (var subscription in subscriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (action == MoviePilotSubscriptionAction.Delete) await DeleteSubscriptionConfirmedAsync(subscription, cancellationToken).ConfigureAwait(false);
                else await ManageSubscriptionAsync(subscription, action, cancellationToken).ConfigureAwait(false);
                results.Add(new(subscription.Id, MoviePilotSubscriptionBatchOutcome.Success, null));
            }
            catch (MoviePilotOperationUncertainException error) { results.Add(new(subscription.Id, MoviePilotSubscriptionBatchOutcome.Uncertain, error.Message)); }
            catch (MoviePilotOperationBlockedException error) { results.Add(new(subscription.Id, MoviePilotSubscriptionBatchOutcome.Blocked, error.Message)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { results.Add(new(subscription.Id, MoviePilotSubscriptionBatchOutcome.Failed, error.Message)); }
        }
        return new(results);
    }

    private async Task DeleteSubscriptionConfirmedAsync(MoviePilotSubscription subscription, CancellationToken cancellationToken)
    {
        var id = subscription.Id.ToString(CultureInfo.InvariantCulture);
        var data = await MutateSubscriptionAsync(subscription, HttpMethod.Delete, $"subscribe/{id}", null, cancellationToken).ConfigureAwait(false);
        if (MoviePilotSubscription.Text(data, "status") != "deleted")
            throw new MoviePilotException("MoviePilot 未确认删除这条订阅，请核对后重试");
        lock (_operationGate) _operations.Remove(SubscribeKey(subscription.Media, subscription.EpisodeGroup));
    }

    /// <summary>
    /// 搜索当前媒体类型的全部订阅（<c>POST subscribe/search?type=</c>）。可能触发成批下载，调用方须先二次确认。
    /// 旧后端成功但不带数据时回 null；没有需要搜索的订阅时服务器以 success:false 原话拒绝。
    /// </summary>
    public Task<MoviePilotSubscriptionSearchSubmission?> SearchAllSubscriptionsAsync(bool isSeries, CancellationToken cancellationToken) =>
        MaintenanceSubscriptionAsync(isSeries, "search", cancellationToken,
            data => MoviePilotSubscriptionSearchSubmission.Parse(data));

    /// <summary>刷新当前媒体类型订阅的资源（<c>POST subscribe/refresh?type=</c>，管理员入口）。</summary>
    public Task RefreshSubscriptionsAsync(bool isSeries, CancellationToken cancellationToken) =>
        MaintenanceSubscriptionAsync(isSeries, "refresh", cancellationToken, _ => (object?)null);

    /// <summary>更新当前媒体类型订阅的元数据（<c>POST subscribe/check?type=</c>，管理员入口）。</summary>
    public Task RefreshSubscriptionMetadataAsync(bool isSeries, CancellationToken cancellationToken) =>
        MaintenanceSubscriptionAsync(isSeries, "check", cancellationToken, _ => (object?)null);

    private async Task<T> MaintenanceSubscriptionAsync<T>(bool isSeries, string action, CancellationToken cancellationToken,
        Func<JsonElement, T> parse)
    {
        var identity = CaptureIdentity();
        var type = isSeries ? "电视剧" : "电影";
        var path = $"subscribe/{action}?type={Uri.EscapeDataString(type)}";
        var key = OperationKey(TransferConnectionStamp(identity), "subscription-maintenance", action, type);
        var reply = await WriteAsync(identity, [key], async (address, token, client) =>
        {
            try { return await client.MutateReplyAsync(address, token, HttpMethod.Post, path, null, cancellationToken).ConfigureAwait(false); }
            // 三个维护入口都有官方旧版 GET 兼容口。只有明确 405 拒绝 POST 才退回；超时等不重放。
            catch (MoviePilotException error) when (error.Status == System.Net.HttpStatusCode.MethodNotAllowed)
            { return await client.MutateReplyAsync(address, token, HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false); }
        }, cancellationToken).ConfigureAwait(false);
        lock (_operationGate) _operations.Remove(key);
        // 「没有需要搜索的订阅」这类 success:false 是明确拒绝：什么都没安排，不锁核对。
        if (!reply.Success) throw new MoviePilotException(ReplyMessage(reply));
        Log.Info(Category, $"MoviePilot 订阅维护：{action}（{type}）");
        return parse(reply.Data);
    }

    private static string ReplyMessage(MoviePilotReply reply) =>
        string.IsNullOrWhiteSpace(reply.Message) ? "MoviePilot 报告了一个错误" : reply.Message;
}