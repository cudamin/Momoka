namespace Momoka.MoviePilot;

public sealed partial class MoviePilotService
{
    private readonly HashSet<string> _pendingSubscriptionSettings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _subscriptionReviews = new(StringComparer.Ordinal);

    private MoviePilotOperationState SubscriptionSettingsState(MoviePilotMedia media, string group)
    {
        lock (_operationGate)
        {
            var key = SubscribeKey(media, group);
            var state = _operations.GetValueOrDefault(key);
            if (_pendingSubscriptionSettings.Contains(key)) return MoviePilotOperationState.Working;
            // 上游 exist_ok 可能按原季合并不同组；结果不明时不能靠换组再次发送。
            return _subscriptionReviews.ContainsKey(SubscriptionReviewKey(media)) ? MoviePilotOperationState.Uncertain : state;
        }
    }

    private void MarkSubscriptionSettingsUncertain(MoviePilotSubscribeChange change)
    {
        var key = SubscribeKey(change.Media, change.EpisodeGroup);
        var scope = SubscriptionReviewKey(change.Media);
        lock (_operationGate)
        {
            _operations[key] = MoviePilotOperationState.Uncertain;
            if (!_subscriptionReviews.TryGetValue(scope, out var keys)) _subscriptionReviews[scope] = keys = new(StringComparer.Ordinal);
            keys.Add(key);
            if (change.Original is { } original) _operations[SubscriptionOperationKey(original)] = MoviePilotOperationState.Uncertain;
        }
    }

    /// <summary>电影按钮直接执行一个已确定方向的新增或取消；重复点击不会被解释成反向操作。</summary>
    public Task ApplyMovieSubscriptionAsync(MoviePilotMedia media, MoviePilotSubscription? existing, CancellationToken cancellationToken)
    {
        if (media.Type != "电影") throw new MoviePilotOperationBlockedException("电视剧必须先选择订阅季");
        var target = media with { Season = null };
        var change = new MoviePilotSubscribeChange(existing is null ? MoviePilotSubscribeChangeKind.Add : MoviePilotSubscribeChangeKind.Delete,
            target, "", MoviePilotSubscribeMode.Normal, existing);
        return ApplySubscriptionChangeAsync(change, cancellationToken);
    }

    /// <summary>按完整身份防重，写前重读，写后核实。失败不重放已完成季，也不把回执当服务器现状。</summary>
    public async Task ApplySubscriptionChangeAsync(MoviePilotSubscribeChange change, CancellationToken cancellationToken)
    {
        MoviePilotSubscriptionPlan.Validate(change);
        var identity = RequireResultConnection(change.Media.ConnectionStamp);
        if (change.Original is { } input && !IsCurrentSubscription(input))
            throw new MoviePilotOperationBlockedException("订阅来自旧连接，请重新读取");
        var key = SubscribeKey(change.Media, change.EpisodeGroup);
        lock (_operationGate)
        {
            if (_pendingSubscriptionSettings.Contains(key) || _subscriptionReviews.ContainsKey(SubscriptionReviewKey(change.Media))
                || _operations.GetValueOrDefault(key) is MoviePilotOperationState.Working or MoviePilotOperationState.Uncertain)
                throw new MoviePilotOperationBlockedException("此订阅正在处理或结果待核对，不能重复发送");
            _pendingSubscriptionSettings.Add(key);
        }
        var acknowledged = false;
        try
        {
            var subscriptions = await SubscriptionsAsync(cancellationToken).ConfigureAwait(false);
            CheckIdentity(identity, cancellationToken);
            if (!Available) throw new MoviePilotOperationBlockedException("MoviePilot 连接不可用，未发送订阅操作");
            var current = MoviePilotSubscriptionPlan.Find(change.Media, change.EpisodeGroup, subscriptions);
            if (change.Kind == MoviePilotSubscribeChangeKind.Add)
            {
                if (current is not null)
                {
                    if (MoviePilotSubscriptionPlan.Mode(current) != change.Mode)
                        throw new MoviePilotOperationBlockedException("服务器上已存在不同模式的订阅，请重新读取后再修改");
                    return;
                }
                if (OperationState(key) == MoviePilotOperationState.Submitted)
                    throw new MoviePilotOperationBlockedException("上一次订阅仍待核对，不能再次新增");
                var result = await WriteAsync(identity, [key], (address, token) =>
                    _client.PostAsync(address, token, "subscribe/", MoviePilotSubscriptionPlan.Body(change), cancellationToken), cancellationToken).ConfigureAwait(false);
                acknowledged = true;
                var id = MoviePilotSubscription.Number(result, "id");
                if (id is not > 0) throw new MoviePilotException("订阅已提交，但服务器未返回订阅编号，请刷新核对");
                var reference = new MoviePilotSubscription
                {
                    Id = id.Value, Media = change.Media, Data = result, ConnectionStamp = change.Media.ConnectionStamp
                };
                var saved = await SubscriptionAsync(reference, cancellationToken).ConfigureAwait(false);
                VerifySaved(change, saved);
            }
            else
            {
                var original = change.Original!;
                if (current is null || current.Id != original.Id || MoviePilotSubscriptionPlan.Mode(current) != MoviePilotSubscriptionPlan.Mode(original))
                    throw new MoviePilotOperationBlockedException("服务器上的订阅已改变，请重新读取，未修改或取消其他订阅");
                current = await SubscriptionAsync(original, cancellationToken).ConfigureAwait(false);
                if (!MoviePilotSubscriptionPlan.Matches(change.Media, change.EpisodeGroup, current)
                    || MoviePilotSubscriptionPlan.Mode(current) != MoviePilotSubscriptionPlan.Mode(original))
                    throw new MoviePilotOperationBlockedException("服务器上的订阅身份或模式已改变，请重新读取");
                CheckIdentity(identity, cancellationToken);
                if (!Available) throw new MoviePilotOperationBlockedException("MoviePilot 连接不可用，未发送订阅操作");
                if (change.Kind == MoviePilotSubscribeChangeKind.Delete)
                    await ManageSubscriptionAsync(current, MoviePilotSubscriptionAction.Delete, cancellationToken).ConfigureAwait(false);
                else
                    await MutateSubscriptionAsync(current, HttpMethod.Put, "subscribe/", MoviePilotSubscriptionPlan.Body(change), cancellationToken).ConfigureAwait(false);
                acknowledged = true;
                if (change.Kind == MoviePilotSubscribeChangeKind.Delete)
                {
                    var saved = await SubscriptionsAsync(cancellationToken).ConfigureAwait(false);
                    if (saved.Any(item => item.Id == original.Id || MoviePilotSubscriptionPlan.Matches(change.Media, change.EpisodeGroup, item)))
                        throw new MoviePilotException("取消请求已提交，但服务器仍存在此订阅，请先核对，未继续取消其他记录");
                }
                else VerifySaved(change, await SubscriptionAsync(current, cancellationToken).ConfigureAwait(false));
            }
            lock (_operationGate) _operations.Remove(key);
        }
        catch
        {
            if (acknowledged || OperationState(key) == MoviePilotOperationState.Uncertain
                || change.Original is { } original && SubscriptionNeedsReview(original))
                MarkSubscriptionSettingsUncertain(change);
            throw;
        }
        finally
        {
            lock (_operationGate) _pendingSubscriptionSettings.Remove(key);
        }
    }

    private static void VerifySaved(MoviePilotSubscribeChange change, MoviePilotSubscription saved)
    {
        if (!MoviePilotSubscriptionPlan.Matches(change.Media, change.EpisodeGroup, saved) || MoviePilotSubscriptionPlan.Mode(saved) != change.Mode)
            throw new MoviePilotException("服务器返回的订阅与所选来源、类型、季、剧集组或模式不同，请刷新核对");
    }
}
