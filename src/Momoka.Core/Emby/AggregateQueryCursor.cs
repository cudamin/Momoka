namespace Momoka.Emby;

/// <summary>
/// 多来源查询的分页位置。每个来源独立计数，不能把聚合结果的条数当成任一服务器的 StartIndex。
/// 一次读取并发请求各来源并轮流排列结果；离线来源只影响自己，刷新时由新的游标重试。
/// </summary>
public sealed class AggregateQueryCursor(IEnumerable<string> sourceKeys)
{
    private readonly Source[] _sources = sourceKeys.Distinct(StringComparer.Ordinal).Select(key => new Source(key)).ToArray();

    public bool HasMore => _sources.Any(source => !source.Complete);

    /// <summary>已消费的条目从服务器清单移除后，只回退它所属来源的偏移。</summary>
    public void RemoveConsumed(string sourceKey)
    {
        if (_sources.FirstOrDefault(source => source.Key == sourceKey) is not { Offset: > 0 } source) return;
        source.Offset--;
        source.Total = Math.Max(source.Offset, source.Total - 1);
    }

    public async Task<AggregateQueryPage> ReadAsync(
        Func<string, int, CancellationToken, Task<ItemsResult>> read,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = _sources.Where(source => !source.Complete).ToArray();
        var replies = await Task.WhenAll(pending.Select(async source =>
        {
            try
            {
                return (Source: source, Result: await read(source.Key, source.Offset, cancellationToken).ConfigureAwait(false), Error: (Exception?)null);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                return (Source: source, Result: (ItemsResult?)null, Error: error);
            }
        })).ConfigureAwait(false);

        // 迟到的响应不能消费游标；调用方切换关键字时会取消旧查询并换一份游标。
        cancellationToken.ThrowIfCancellationRequested();
        var failures = new Dictionary<string, Exception>(StringComparer.Ordinal);
        foreach (var (source, result, error) in replies)
        {
            if (result is null)
            {
                source.Complete = true;
                source.Total = source.Offset;
                if (error is not null) failures[source.Key] = error;
                continue;
            }

            source.Offset += result.Items.Count;
            source.Total = Math.Max(source.Offset, result.TotalRecordCount);
            source.Complete = result.Items.Count == 0 || source.Offset >= source.Total;
            if (source.Complete) source.Total = source.Offset;
        }

        var items = new List<EmbyItem>();
        var rows = replies.Where(reply => reply.Result is not null).Select(reply => reply.Result!.Items).ToArray();
        for (var index = 0; rows.Any(row => row.Count > index); index++)
            foreach (var row in rows)
                if (index < row.Count) items.Add(row[index]);

        return new AggregateQueryPage(new ItemsResult
        {
            Items = items,
            TotalRecordCount = (int)Math.Min(int.MaxValue, _sources.Sum(source => (long)source.Total))
        }, failures);
    }

    private sealed class Source(string key)
    {
        public string Key { get; } = key;
        public int Offset { get; set; }
        public int Total { get; set; }
        public bool Complete { get; set; }
    }
}

public sealed record AggregateQueryPage(ItemsResult Result, IReadOnlyDictionary<string, Exception> Failures);
