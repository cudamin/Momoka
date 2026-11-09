using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Momoka.Configuration;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotImageConcurrencyTests
{
    public static void Register()
    {
        Test("MP 图片：原生连接池中的慢图不阻塞连接检查，排队图片最终全部读取",
            () => SlowImagesAsync().GetAwaiter().GetResult());
        Test("MP 图片：取消排队与在飞图片后释放名额，旧账号排队图不发请求",
            () => CancellationAsync().GetAwaiter().GetResult());
        Test("MP 图片：账号往返后未取消的旧排队图片也不发请求",
            () => IdentityAsync().GetAwaiter().GetResult());
    }

    private static MoviePilotService Service(MoviePilotClient client, Uri origin, out AppSettings settings)
    {
        settings = new AppSettings();
        settings.MoviePilot.Enabled = true;
        settings.MoviePilot.Url = origin.AbsoluteUri;
        settings.MoviePilot.Username = "anonymous";
        settings.MoviePilot.ProtectedPassword = "fixture-password";
        return new(client, new MoviePilotCredentials(PassthroughSecretProtector.Instance), settings);
    }

    private static async Task SlowImagesAsync()
    {
        await using var server = new ImageServer();
        using var client = new MoviePilotClient();
        using var service = Service(client, server.Origin, out _);
        Assert.True(await service.RefreshAvailabilityAsync());
        var changes = 0;
        service.AvailabilityChanged += (_, _) => changes++;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var fetch = service.CaptureImageFetcher();
        var images = Enumerable.Range(0, 12).Select(index => fetch($"https://image.invalid/{index}.jpg", lifetime.Token)).ToArray();
        try
        {
            await server.FourImages.Task.WaitAsync(lifetime.Token);
            using var check = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            bool connected;
            try { connected = await service.RefreshAvailabilityAsync(check.Token); }
            catch (OperationCanceledException) { connected = false; }
            Assert.True(connected, "慢图不能耗尽 API 连接并使连接检查超时");
            Assert.True(server.MaximumImages <= 4, "图片最多占用四个连接，给页面 API 和后台检查留出连接");
            Assert.Equal(0, changes);
            server.Release.TrySetResult();
            var results = await Task.WhenAll(images).WaitAsync(lifetime.Token);
            Assert.Equal(12, results.Length);
            Assert.True(results.All(bytes => Encoding.UTF8.GetString(bytes) == "fixture-image"));
            Assert.Equal(12, server.Images);
            Assert.Equal(0, server.InvalidImageCookies);
            Assert.True(service.Available);
        }
        finally
        {
            server.Release.TrySetResult();
            await lifetime.CancelAsync();
            try { await Task.WhenAll(images).WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (OperationCanceledException) { }
        }
    }

    private static async Task CancellationAsync()
    {
        await using var server = new ImageServer();
        using var client = new MoviePilotClient();
        using var service = Service(client, server.Origin, out var settings);
        Assert.True(await service.RefreshAvailabilityAsync());
        using var canceled = new CancellationTokenSource();
        var fetch = service.CaptureImageFetcher();
        var images = Enumerable.Range(0, 8).Select(index => fetch($"https://image.invalid/old-{index}.jpg", canceled.Token)).ToArray();
        await server.FourImages.Task.WaitAsync(TimeSpan.FromSeconds(3));
        settings.MoviePilot.Username = "replacement";
        await canceled.CancelAsync();
        try { await Task.WhenAll(images).WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (OperationCanceledException) { }
        foreach (var image in images)
        {
            try { await image; Assert.True(false, "取消后的图片不能成功"); }
            catch (OperationCanceledException) { }
        }
        Assert.True(server.Images <= 4, "排队取消的旧图片不能占用传输连接");
        server.Release.TrySetResult();
        Assert.True(await service.RefreshAvailabilityAsync());
        var bytes = await service.CaptureImageFetcher()("https://image.invalid/new.jpg", default).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("fixture-image", Encoding.UTF8.GetString(bytes));
        Assert.True(service.Available);
    }

    private static async Task IdentityAsync()
    {
        await using var server = new ImageServer();
        using var client = new MoviePilotClient();
        using var service = Service(client, server.Origin, out var settings);
        Assert.True(await service.RefreshAvailabilityAsync());
        var fetch = service.CaptureImageFetcher();
        var images = Enumerable.Range(0, 8).Select(index => fetch($"https://image.invalid/old-{index}.jpg", default)).ToArray();
        await server.FourImages.Task.WaitAsync(TimeSpan.FromSeconds(3));
        settings.MoviePilot.Username = "replacement";
        settings.MoviePilot.Username = "anonymous";
        server.Release.TrySetResult();
        try { await Task.WhenAll(images).WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (OperationCanceledException) { }
        Assert.True(images.All(task => task.IsCanceled));
        Assert.Equal(4, server.Images, "账号往返不能让原代次排队图片使用新 Cookie 发请求");
        Assert.True(await service.RefreshAvailabilityAsync());
        await service.CaptureImageFetcher()("https://image.invalid/new.jpg", default).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(5, server.Images);
    }

    private sealed class ImageServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentBag<Task> _handlers = [];
        private readonly Task _accept;
        private int _activeImages;
        private int _maximumImages;
        private int _images;
        private int _invalidImageCookies;
        public TaskCompletionSource FourImages { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Uri Origin { get; }
        public int Images => Volatile.Read(ref _images);
        public int MaximumImages => Volatile.Read(ref _maximumImages);
        public int InvalidImageCookies => Volatile.Read(ref _invalidImageCookies);

        public ImageServer()
        {
            _listener.Start();
            Origin = new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _accept = AcceptAsync();
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                    _handlers.Add(HandleAsync(await _listener.AcceptTcpClientAsync(_stop.Token)));
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }

        private async Task HandleAsync(TcpClient socket)
        {
            using (socket)
            {
                try
                {
                    await using var stream = socket.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    var first = await reader.ReadLineAsync(_stop.Token) ?? throw new EndOfStreamException();
                    var path = first.Split(' ')[1];
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } line)
                    {
                        var colon = line.IndexOf(':');
                        headers[line[..colon]] = line[(colon + 1)..].Trim();
                    }
                    var length = int.Parse(headers.GetValueOrDefault("Content-Length", "0"), CultureInfo.InvariantCulture);
                    if (length > 4096) throw new InvalidDataException("Fixture request is too large");
                    var body = new char[length];
                    var offset = 0;
                    while (offset < length)
                    {
                        var count = await reader.ReadAsync(body.AsMemory(offset), _stop.Token);
                        if (count == 0) throw new EndOfStreamException();
                        offset += count;
                    }
                    var image = path.Contains("system/cache/image", StringComparison.Ordinal);
                    var reply = path.EndsWith("login/access-token", StringComparison.Ordinal)
                        ? "{\"access_token\":\"fixture-token\"}" : image ? "fixture-image" : "{}";
                    if (image)
                    {
                        if (headers.GetValueOrDefault("Cookie") != "MoviePilot=fixture-resource")
                            Interlocked.Increment(ref _invalidImageCookies);
                        Interlocked.Increment(ref _images);
                        var active = Interlocked.Increment(ref _activeImages);
                        int previous;
                        do { previous = Volatile.Read(ref _maximumImages); }
                        while (active > previous && Interlocked.CompareExchange(ref _maximumImages, active, previous) != previous);
                        if (active >= 4) FourImages.TrySetResult();
                        try { await Release.Task.WaitAsync(_stop.Token); }
                        finally { Interlocked.Decrement(ref _activeImages); }
                    }
                    var bytes = Encoding.UTF8.GetBytes(reply);
                    var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\n" +
                        "Connection: close\r\nSet-Cookie: MoviePilot=fixture-resource; Path=/; HttpOnly\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _stop.Token);
                    await stream.WriteAsync(bytes, _stop.Token);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
                catch (IOException) { /* The client may cancel a held anonymous request. */ }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _accept.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.WhenAll(_handlers).WaitAsync(TimeSpan.FromSeconds(3));
            _stop.Dispose();
        }
    }
}
