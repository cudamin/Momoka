using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Momoka.MoviePilot;

public sealed partial class MoviePilotClient
{
    // resource.vue / search.py: each SSE request owns its progress and final result snapshot.
    // Never read system/progress/search: that shared slot can belong to another browser/search.
    internal async Task<JsonElement?> SearchStreamAsync(Uri apiBase, string token, string path,
        IProgress<MoviePilotSearchProgress> progress, CancellationToken cancellationToken)
    {
        var query = path.IndexOf('?');
        var streamPath = query < 0 ? path + "/stream" : path[..query] + "/stream" + path[query..];
        streamPath += (query < 0 ? "?" : "&") + "locale=zh-CN";
        using var request = new HttpRequestMessage(HttpMethod.Get, MoviePilotAddress.Combine(apiBase, BasePath + streamPath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            using var response = await SendAsync(request, timeout.Token, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
                return null;
            if (!response.IsSuccessStatusCode)
            {
                await response.Content.LoadIntoBufferAsync(1024 * 1024, timeout.Token).ConfigureAwait(false);
                _ = await ReadDataAsync(response, timeout.Token).ConfigureAwait(false);
            }
            if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
                throw new MoviePilotException("MoviePilot 未返回搜索进度流，请稍后重试");

            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var state = new SearchStreamState(progress);
            var line = new StringBuilder();
            var data = new StringBuilder();
            var buffer = new char[4096];
            var received = 0L;
            while (true)
            {
                int count;
                try { count = await reader.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (state.Done && !cancellationToken.IsCancellationRequested)
                {
                    // The web client keeps the stream open briefly after done for server cache cleanup.
                    return state.Result();
                }
                catch (IOException) when (state.Done && !cancellationToken.IsCancellationRequested)
                {
                    return state.Result();
                }
                if (count == 0) break;
                received += count;
                if (received > 128 * 1024 * 1024)
                    throw new MoviePilotException("搜索结果过大，请缩小站点范围后重试");
                for (var index = 0; index < count; index++)
                {
                    if (buffer[index] != '\n')
                    {
                        line.Append(buffer[index]);
                        if (line.Length + data.Length > 32 * 1024 * 1024)
                            throw new MoviePilotException("搜索进度消息过大，请缩小站点范围后重试");
                        continue;
                    }
                    var value = line.ToString().TrimEnd('\r');
                    line.Clear();
                    if (value.Length == 0 && data.Length > 0)
                    {
                        var wasDone = state.Done;
                        state.Accept(data.ToString());
                        data.Clear();
                        if (!wasDone && state.Done) timeout.CancelAfter(TimeSpan.FromMilliseconds(1500));
                    }
                    else if (value.StartsWith("data:", StringComparison.Ordinal))
                    {
                        if (data.Length > 0) data.Append('\n');
                        data.Append(value.AsSpan(value.Length > 5 && value[5] == ' ' ? 6 : 5));
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return state.Result();
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MoviePilotUnreachableException("搜索进度连接超时，请稍后重试", error);
        }
        catch (IOException error)
        {
            throw new MoviePilotUnreachableException("搜索进度连接已中断，请稍后重试", error);
        }
        catch (JsonException)
        {
            throw new MoviePilotException("MoviePilot 搜索进度无法解析，请稍后重试");
        }
    }

    private sealed class SearchStreamState(IProgress<MoviePilotSearchProgress> progress)
    {
        private List<JsonElement>? _final;
        private List<JsonElement>? _batch;
        private int _nextBatch;
        private int _batchCount;
        private int _expectedItems;
        private double? _value;
        public bool Done { get; private set; }

        public void Accept(string json)
        {
            if (Done) return;
            using var document = JsonDocument.Parse(json);
            var message = document.RootElement;
            if (message.ValueKind != JsonValueKind.Object) throw Invalid();
            var type = MoviePilotTransfer.Text(message, "type");
            if (type == "heartbeat") return;
            if (type == "error")
            {
                var error = MoviePilotTransfer.Text(message, "message_i18n");
                if (error.Length == 0) error = MoviePilotTransfer.Text(message, "message");
                throw new MoviePilotException(error.Length > 0 ? error : "搜索失败，请稍后重试");
            }
            if (message.TryGetProperty("value", out var numeric) && numeric.ValueKind == JsonValueKind.Number
                && numeric.TryGetDouble(out var number) && double.IsFinite(number))
                _value = Math.Clamp(number, 0, 100);
            var text = MoviePilotTransfer.Text(message, "text_i18n");
            if (text.Length == 0) text = MoviePilotTransfer.Text(message, "text");
            progress.Report(new(_value, text));
            var hasItems = message.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array;
            if (message.TryGetProperty("replace_batch", out var batch) && batch.ValueKind == JsonValueKind.True)
            {
                if (!hasItems || !ReadInt(message, "batch_index", out var index) || !ReadInt(message, "batch_count", out var count)
                    || !ReadInt(message, "total_items", out var total) || count <= 0 || index >= count || total > 100_000)
                    throw Invalid();
                if (index == 0)
                {
                    if (_batch is not null) throw Invalid();
                    _batch = [];
                    _nextBatch = 0; _batchCount = count; _expectedItems = total;
                }
                if (_batch is null || index != _nextBatch || count != _batchCount || total != _expectedItems) throw Invalid();
                _batch.AddRange(items.EnumerateArray().Select(item => item.Clone()));
                if (_batch.Count > _expectedItems) throw Invalid();
                if (++_nextBatch == _batchCount)
                {
                    if (_batch.Count != _expectedItems) throw Invalid();
                    _final = _batch;
                    _batch = null;
                }
            }
            else if (type == "replace" || type == "done" && _final is null)
            {
                if (!hasItems || _batch is not null || items.GetArrayLength() > 100_000) throw Invalid();
                _final = items.EnumerateArray().Select(item => item.Clone()).ToList();
            }
            // append contains unverified candidates during precise search. Only commit final snapshots.
            if (type == "done")
            {
                if (_batch is not null || _final is null) throw Invalid();
                if (message.TryGetProperty("total_items", out _) &&
                    (!ReadInt(message, "total_items", out var total) || total != _final.Count)) throw Invalid();
                Done = true;
            }
        }

        public JsonElement Result() => Done && _final is not null
            ? JsonSerializer.SerializeToElement(_final) : throw Invalid();

        private static bool ReadInt(JsonElement data, string name, out int value)
        {
            value = 0;
            return data.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number
                && property.TryGetInt32(out value) && value >= 0;
        }

        private static MoviePilotException Invalid() => new("搜索结果未完整返回，请稍后重试");
    }
}
