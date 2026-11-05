using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Emby;
using Momoka.Plex;

internal static class PlexSearch
{
    public static async Task RunAsync()
    {
        foreach (var firstMovie in new[] { false, true })
            foreach (var aggregate in new[] { false, true })
                foreach (var totalRecords in new[] { 21, 121 })
                {
                    using var handler = new SearchTransport(firstMovie, totalRecords);
                    using var http = new PlexHttp(handler, false);
                    var adapter = new PlexMediaAdapter(new PlexClient(http), new PlexConnection(new Uri("https://search.invalid/"), ""));
                    var actual = new List<string>();
                    if (aggregate)
                    {
                        var cursor = new AggregateQueryCursor(["plex"]);
                        for (var attempt = 0; cursor.HasMore && attempt < 10; attempt++)
                        {
                            var page = await cursor.ReadAsync((key, offset, token) => adapter.GetItemsAsync(LibrarySearch.Query("query", LibrarySearchCategory.Movies, offset, 1)!, token), default);
                            actual.AddRange(page.Result.Items.Select(item => item.Id));
                        }
                        if (cursor.HasMore) throw new InvalidOperationException("Filtered search never completed");
                    }
                    else
                    {
                        var total = int.MaxValue;
                        for (var offset = 0; offset < total;)
                        {
                            var page = await adapter.GetItemsAsync(LibrarySearch.Query("query", LibrarySearchCategory.Movies, offset, 1)!, default);
                            actual.AddRange(page.Items.Select(item => item.Id));
                            total = page.TotalRecordCount;
                            if (page.Items.Count == 0) break;
                            offset += page.Items.Count;
                        }
                    }
                    var expected = firstMovie ? "0," + (totalRecords - 1) : (totalRecords - 1).ToString();
                    if (string.Join(',', actual) != expected) throw new InvalidOperationException("Plex filtered pagination skipped or duplicated matches");
                }
        foreach (var cancel in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource();
            using var handler = new SearchTransport(false, 121)
            {
                BeforePage = offset =>
                {
                    if (offset == 0) return;
                    if (cancel) cancellation.Cancel();
                    else throw new HttpRequestException("anonymous page failure");
                }
            };
            using var http = new PlexHttp(handler, false);
            var adapter = new PlexMediaAdapter(new PlexClient(http), new PlexConnection(new Uri("https://search.invalid/"), ""));
            try
            {
                await adapter.GetItemsAsync(LibrarySearch.Query("query", LibrarySearchCategory.Movies, 0, 20)!, cancellation.Token);
                throw new InvalidOperationException("Incomplete raw search was reported as a complete filtered page");
            }
            catch (OperationCanceledException) when (cancel) { }
            catch (PlexApiException) when (!cancel) { }
        }
        Console.WriteLine("R1: single-source and aggregate filtered search pagination cases passed.");
    }

    private sealed class SearchTransport(bool firstMovie, int totalRecords) : HttpMessageHandler
    {
        public Action<int>? BeforePage { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequestUri?.Host != "search.invalid" || request.RequestUri.AbsolutePath != "/search")
                throw new InvalidOperationException("Unexpected search request");
            var offset = int.Parse(request.Headers.GetValues("X-Plex-Container-Start").Single());
            var size = int.Parse(request.Headers.GetValues("X-Plex-Container-Size").Single());
            BeforePage?.Invoke(offset);
            cancellationToken.ThrowIfCancellationRequested();
            var items = Enumerable.Range(0, totalRecords).Skip(offset).Take(size).Select(index => new
            { ratingKey = index.ToString(), type = index == totalRecords - 1 || firstMovie && index == 0 ? "movie" : "show", title = "anonymous" });
            var body = JsonSerializer.Serialize(new { MediaContainer = new { totalSize = totalRecords, Metadata = items } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
