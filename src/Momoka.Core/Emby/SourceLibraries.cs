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
                await MarkMixedLibrariesAsync(source, items, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Emby 的混合内容库没有 <see cref="EmbyItem.CollectionType"/>，靠库根的 Subviews 识别
    /// （见 <see cref="LibraryBrowse.IsMixedContent"/>）。这一步只读；失败不把整个来源判成不可用 ——
    /// 那一库退回文件夹样子，其余目录照常。
    /// </summary>
    private static async Task MarkMixedLibrariesAsync(EmbySessionScope source, IReadOnlyList<EmbyItem> libraries,
        CancellationToken cancellationToken)
    {
        if (source.Connection.ServerKind != MediaServerKind.Emby) return;
        if (libraries.All(item => item.CollectionType is not null)) return;
        try
        {
            await source.ExecuteAsync((client, token) => client.FetchLibrarySubviewsAsync(libraries, token), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception error)
        {
            Log.Warn("媒体库", $"读取 {source.DisplayName} 的库分类失败", error);
            return;
        }
        foreach (var library in libraries)
            if (LibraryBrowse.IsMixedContent(library.CollectionType, library.Subviews))
                library.CollectionType = "mixed";
    }
}
