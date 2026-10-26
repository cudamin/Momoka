using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Emby;

namespace Momoka.NaturalContinuationCallerTests;

internal sealed class AnonymousTransport(Tape tape) : HttpMessageHandler
{
    internal sealed record Read(string Host, string Path, CancellationToken Token);
    private readonly Dictionary<string, Func<Read, Task<object>>> _routes = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly List<Read> _reads = [];
    internal int Rejected { get; private set; }
    internal Read[] Reads { get { lock (_gate) return _reads.ToArray(); } }
    internal TaskCompletionSource<Read> Entered { get; } = Signals.New<Read>();

    internal void Item(EmbySessionScope source, string id, Func<Read, Task<object>> response) =>
        _routes.Add(source.Connection.ApiBase.Host + "/emby/Users/anonymous/Items/" + id, response);
    internal void Item(EmbySessionScope source, EmbyItem item) => Item(source, item.Id, _ => Task.FromResult<object>(item));
    internal void Seasons(EmbySessionScope source, string series, IReadOnlyList<EmbyItem> rows) =>
        _routes.Add(source.Connection.ApiBase.Host + "/emby/Shows/" + series + "/Seasons", _ => Task.FromResult<object>(new { Items = rows }));
    internal void Episodes(EmbySessionScope source, string series, IReadOnlyList<EmbyItem> rows) =>
        _routes.Add(source.Connection.ApiBase.Host + "/emby/Shows/" + series + "/Episodes", _ => Task.FromResult<object>(new { Items = rows }));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new TestFailure("anonymous read has no URI");
        if (request.Method != HttpMethod.Get || !uri.Host.EndsWith(".invalid", StringComparison.Ordinal)
            || !_routes.TryGetValue(uri.Host + uri.AbsolutePath, out var response))
        {
            Rejected++;
            throw new TestFailure("non-allowlisted endpoint: " + request.Method + " " + uri.Host + uri.AbsolutePath);
        }
        var read = new Read(uri.Host, uri.AbsolutePath, cancellationToken);
        lock (_gate) _reads.Add(read);
        tape.Add("metadata:" + read.Host + read.Path);
        Entered.TrySetResult(read);
        cancellationToken.ThrowIfCancellationRequested();
        var data = await response(read).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json")
        };
    }
}
