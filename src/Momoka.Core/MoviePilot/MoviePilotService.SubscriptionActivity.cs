using System.Globalization;

namespace Momoka.MoviePilot;

public sealed partial class MoviePilotService
{
    /// <summary>向 MoviePilot 分享服务发布这条电视剧订阅的规则；不是复制媒体网页地址。</summary>
    public async Task ShareSubscriptionAsync(MoviePilotSubscription subscription, string comment, string user, CancellationToken cancellationToken)
    {
        if (!subscription.IsSeries) throw new MoviePilotException("只有电视剧订阅可以分享");
        if (string.IsNullOrWhiteSpace(comment) || string.IsNullOrWhiteSpace(user))
            throw new MoviePilotException("请填写分享说明和分享人");
        var identity = RequireResultConnection(subscription.ConnectionStamp);
        var key = SubscriptionOperationKey(subscription);
        var title = subscription.Media.Title + (subscription.Media.Season is { } season ? $" S{season:D2}" : "");
        var body = new { subscribe_id = subscription.Id, share_title = title, share_comment = comment, share_user = user };
        // 分享只有 POST；405 也不能借用搜索/重置的旧 GET 兼容分支。结果不明与其他订阅写操作共享核对限制。
        await WriteAsync(identity, [key], (address, token, client) =>
            client.MutateAsync(address, token, HttpMethod.Post, "subscribe/share", body, cancellationToken), cancellationToken).ConfigureAwait(false);
        lock (_operationGate) _operations.Remove(key);
    }

    public async Task<MoviePilotSubscriptionActivityPage> SubscriptionActivityAsync(MoviePilotSubscription subscription,
        int? days, string? kind, string? before, CancellationToken cancellationToken)
    {
        if (days is < 1 or > 365) throw new MoviePilotException("活动时间范围须为1到365天");
        if (kind is not null and not ("created" or "updated" or "status" or "completed" or "refresh" or "search"))
            throw new MoviePilotException("活动类型无效");
        if (before is not null && !MoviePilotSubscriptionActivity.ValidIdentifier(before))
            throw new MoviePilotException("活动分页标识无效，请重新读取");
        var identity = RequireResultConnection(subscription.ConnectionStamp);
        var query = new List<string> { "history=false", "limit=30" };
        if (days is { } period) query.Add("days=" + period.ToString(CultureInfo.InvariantCulture));
        if (kind is not null) query.Add("kind=" + kind);
        if (before is not null) query.Add("before=" + before);
        var path = "subscribe/" + subscription.Id.ToString(CultureInfo.InvariantCulture) + "/activity?" + string.Join('&', query);
        var data = await CallAsync((address, token, client) => client.GetAsync(address, token, path, cancellationToken),
            cancellationToken, identity).ConfigureAwait(false);
        return MoviePilotSubscriptionActivityPage.Parse(data);
    }

    public async Task<MoviePilotSubscriptionActivity> SubscriptionActivityDetailAsync(MoviePilotSubscription subscription,
        string eventId, CancellationToken cancellationToken)
    {
        if (!MoviePilotSubscriptionActivity.ValidIdentifier(eventId)) throw new MoviePilotException("活动事件标识无效");
        var identity = RequireResultConnection(subscription.ConnectionStamp);
        var path = "subscribe/" + subscription.Id.ToString(CultureInfo.InvariantCulture) + "/activity/" + eventId + "?history=false";
        var data = await CallAsync((address, token, client) => client.GetAsync(address, token, path, cancellationToken),
            cancellationToken, identity).ConfigureAwait(false);
        var activity = MoviePilotSubscriptionActivity.Parse(data);
        if (activity.EventId != eventId) throw new MoviePilotException("活动详情身份不匹配，请重新读取");
        return activity;
    }
}
