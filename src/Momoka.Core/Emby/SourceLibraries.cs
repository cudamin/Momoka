using Momoka.Diagnostics;

namespace Momoka.Emby;

/// <summary>所有在线账号的媒体库目录；一个来源不可用时保留其余目录。</summary>
public static class SourceLibraries
{
    public sealed record Result(IReadOnlyList<EmbyItem> Items, IReadOnlyList<string> FailedSources);

    public static async Task<Result> LoadAsync(EmbySession session, CancellationToken cancellationToken)
    {
        var sources = session.Sources;
        if (sources.Count == 0) return new Result([], []);
        var results = await Task.WhenAll(sources.Select(async source =>
        {
            try
            {
                var items = session.TakeRestoredViews(source)
                    ?? await source.ExecuteAsync((client, token) => client.GetViewsAsync(token), cancellationToken)
                        .ConfigureAwait(false);
                source.ThrowIfUnavailable();
                return new Result(items, []);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                Log.Warn("媒体库", $"读取 {source.DisplayName} 的媒体库失败", error);
                return new Result([], [source.DisplayName]);
            }
        })).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new Result(results.SelectMany(result => result.Items).Where(item => item.Source?.IsAvailable == true).ToArray(),
            results.SelectMany(result => result.FailedSources).ToArray());
    }
}
