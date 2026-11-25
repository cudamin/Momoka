using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Momoka.Emby;

namespace Momoka.Bangumi;

public sealed partial class BangumiClient
{
    /// <summary>优先读取已有编号，缺少编号时用标题、年份和影视形态核验官方搜索结果。</summary>
    public async Task<decimal?> RatingAsync(EmbyItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (BangumiRatingIdentity.For(item) is { } subject)
            return await RatingAsync(subject, cancellationToken).ConfigureAwait(false);
        if (BangumiRatingIdentity.HasProvider(item) || item.Type is not (EmbyItemType.Movie or EmbyItemType.Series))
            return null;
        var year = item.ProductionYear ?? item.PremiereDate?.Year;
        if (year is not (>= 1800 and <= 9998)) return null;
        var titles = new[] { item.OriginalTitle, item.Name }.Where(title => !string.IsNullOrWhiteSpace(title)
            && title.Length <= 200).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (titles.Length == 0) return null;
        var start = year.Value.ToString("0000", CultureInfo.InvariantCulture) + "-01-01";
        var end = (year.Value + 1).ToString("0000", CultureInfo.InvariantCulture) + "-01-01";

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var connection = AcquireConnection();
        try
        {
            foreach (var title in titles)
            {
                // bangumi/server 522cc1d, internal/search/subject/handle.go returns names, aliases and platform.
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.bgm.tv/v0/search/subjects?limit=20")
                {
                    Content = JsonContent.Create(new
                    {
                        keyword = title!.Trim(),
                        sort = "match",
                        filter = new { type = new[] { 2, 6 }, air_date = new[] { ">=" + start, "<" + end } }
                    })
                };
                using var response = await connection.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                using var document = await ReadDocumentAsync(response, deadline.Token).ConfigureAwait(false);
                if (document is null) return null;
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("total", out var total)
                    || total.ValueKind != JsonValueKind.Number || !total.TryGetInt32(out var count) || count is < 0 or > 20
                    || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array
                    || data.GetArrayLength() > 20) return null;
                long? match = null;
                foreach (var candidate in data.EnumerateArray())
                {
                    if (!BangumiRatingIdentity.Matches(candidate, item, year.Value)
                        || !candidate.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var found)
                        || found is < 1 or > 10000000) continue;
                    if (match is not null && match != found) return null;
                    match = found;
                }
                if (match is { } matched) return await RatingAsync(matched, deadline.Token).ConfigureAwait(false);
            }
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (HttpRequestException) { return null; }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        finally { ReleaseConnection(connection); }
    }
}
