using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>封面预览的像素预算、同张候选回退与实际服务器代理请求；只用占位地址和内存传输。</summary>
internal static class ArtworkPreviewTests
{
    private const string Thumbnail = "https://images.example.invalid/thumbnail.png";
    private const string Original = "https://images.example.invalid/original.png";

    public static void Register()
    {
        RegisterKinds();
        RegisterPixels();
        RegisterAddresses();
        RegisterFailures();
        RegisterCancellation();
        RegisterContext();
        RegisterProxy();
    }

    private static void RegisterKinds()
    {
        Test("封面预览：单集 Primary 使用横向 16:9，保留名称、图种和多图标记", () =>
        {
            var kind = new ArtworkKind(EmbyImageStore.Primary, "本集剧照", 2d / 3, Many: true);
            var result = ArtworkPreview.KindFor(new EmbyItem { Type = EmbyItemType.Episode }, kind);
            Assert.Equal(16d / 9, result.Ratio);
            Assert.Equal(kind.ImageType, result.ImageType);
            Assert.Equal(kind.Name, result.Name);
            Assert.Equal(kind.Many, result.Many);
            Assert.Equal(2d / 3, kind.Ratio, "按条目调整不能修改传入图种");
        });

        Test("封面预览：单集比例调整不污染全局 Panel 和 SingleKinds 的海报比例", () =>
        {
            var before = ItemArtwork.Panel.ToArray();
            var primary = ItemArtwork.Panel.Single(kind => kind.ImageType == EmbyImageStore.Primary);
            var result = ArtworkPreview.KindFor(new EmbyItem { Type = EmbyItemType.Episode }, primary);
            Assert.Equal(16d / 9, result.Ratio);
            Assert.Equal(2d / 3, ItemArtwork.Panel.Single(kind => kind.ImageType == EmbyImageStore.Primary).Ratio);
            Assert.Equal(2d / 3, ItemArtwork.SingleKinds.Single(kind => kind.ImageType == EmbyImageStore.Primary).Ratio);
            Assert.True(before.SequenceEqual(ItemArtwork.Panel), "其它页使用的全局图种必须保持原值");
        });

        foreach (var itemType in new[] { EmbyItemType.Series, EmbyItemType.Movie, EmbyItemType.Season })
            Test("封面预览：" + itemType + " Primary 保持竖向 2:3", () =>
            {
                var kind = ItemArtwork.Panel.Single(row => row.ImageType == EmbyImageStore.Primary);
                var result = ArtworkPreview.KindFor(new EmbyItem { Type = itemType }, kind);
                Assert.Equal(kind, result);
                Assert.Equal(2d / 3, result.Ratio);
            });

        foreach (var kind in ItemArtwork.Panel.Where(row => row.ImageType != EmbyImageStore.Primary))
            Test("封面预览：单集的 " + kind.ImageType + " 图种保持原值", () =>
            {
                var result = ArtworkPreview.KindFor(new EmbyItem { Type = EmbyItemType.Episode }, kind);
                Assert.Equal(kind, result);
            });

        Test("封面预览：图种按 ordinal 匹配，不改大小写不同的 primary", () =>
        {
            var kind = new ArtworkKind("primary", "自定义图种", 1.25);
            Assert.Equal(kind, ArtworkPreview.KindFor(new EmbyItem { Type = EmbyItemType.Episode }, kind));
        });

        Test("封面预览：无条目时明确拒绝比例调整", () =>
            Assert.Throws<ArgumentNullException>(() => ArtworkPreview.KindFor(null!, ItemArtwork.Panel[0])));
    }

    private static void RegisterPixels()
    {
        foreach (var (width, scale, expected) in new (int, double, int)[]
        {
            (140, 1, 140), (140, 1.25, 175), (140, 2, 280),
            (188, 1, 188), (188, 1.25, 235), (188, 2, 376),
            (1280, 1.5, 1920), (1280, 2, 2560),
            (140, 1.01, 142), (188, 1.333, 251),
            (1, double.Epsilon, 1), (140, 0.25, 35),
            (16384, 1, 16384), (16384, 1.0001, 16384),
            (int.MaxValue, double.MaxValue, 16384)
        })
            Test($"封面预览：{width} DIP × {scale} 倍对应 {expected} 像素", () =>
                Assert.Equal(expected, ArtworkPreview.PixelWidth(width, scale)));

        foreach (var width in new[] { 0, -1, int.MinValue })
            Test($"封面预览：拒绝无效逻辑宽度 {width}", () =>
                Assert.Throws<ArgumentOutOfRangeException>(() => ArtworkPreview.PixelWidth(width, 1)));
        foreach (var scale in new[] { 0d, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Test($"封面预览：拒绝无效光栅倍率 {scale}", () =>
                Assert.Throws<ArgumentOutOfRangeException>(() => ArtworkPreview.PixelWidth(140, scale)));
    }

    private static void RegisterAddresses()
    {
        Test("封面预览：缩略图成功不再请求原图，保持图片与取消令牌", () =>
        {
            var calls = new List<string>();
            var picture = new object();
            using var lifetime = new CancellationTokenSource();
            var result = Wait(ArtworkPreview.RemoteAsync(new RemoteImageInfo { ThumbnailUrl = Thumbnail, Url = Original },
                (address, token) =>
                {
                    calls.Add(address);
                    Assert.Equal(lifetime.Token, token);
                    return Task.FromResult<object?>(picture);
                }, lifetime.Token));
            Assert.True(ReferenceEquals(picture, result));
            Assert.Equal(Thumbnail, calls.Single());
        });

        foreach (var thumbnail in new string?[] { null, "", " \t\r\n" })
            Test("封面预览：空白缩略图直接读取原图：" + (thumbnail is null ? "null" : thumbnail.Length), () =>
            {
                var calls = new List<string>();
                var result = Wait(ArtworkPreview.RemoteAsync<string>(new RemoteImageInfo { ThumbnailUrl = thumbnail, Url = Original },
                    (address, _) => { calls.Add(address); return Task.FromResult<string?>("picture"); }, default));
                Assert.Equal("picture", result);
                Assert.Equal(Original, calls.Single());
            });

        foreach (var original in new[] { "", " \t" })
            Test("封面预览：原图为空不重复读取失败缩略图：" + original.Length, () =>
            {
                var calls = new List<string>();
                var result = Wait(ArtworkPreview.RemoteAsync<string>(new RemoteImageInfo { ThumbnailUrl = Thumbnail, Url = original },
                    (address, _) => { calls.Add(address); return Task.FromResult<string?>(null); }, default));
                Assert.Null(result);
                Assert.Equal(Thumbnail, calls.Single());
            });

        Test("封面预览：缩略图与原图同地址失败时只请求一次", () =>
        {
            var calls = 0;
            var result = Wait(ArtworkPreview.RemoteAsync<string>(new RemoteImageInfo { ThumbnailUrl = Original, Url = Original },
                (_, _) => { calls++; return Task.FromException<string?>(new InvalidDataException("synthetic corrupt")); }, default));
            Assert.Null(result);
            Assert.Equal(1, calls);
        });

        Test("封面预览：按 ordinal 去重，大小写不同的地址保持原样与顺序", () =>
        {
            var upper = "https://images.example.invalid/Picture.png";
            var lower = "https://images.example.invalid/picture.png";
            var calls = new List<string>();
            var result = Wait(ArtworkPreview.RemoteAsync<string>(new RemoteImageInfo { ThumbnailUrl = upper, Url = lower },
                (address, _) => { calls.Add(address); return Task.FromResult(address == upper ? null : "picture"); }, default));
            Assert.Equal("picture", result);
            Assert.True(new[] { upper, lower }.SequenceEqual(calls));
        });

        Test("封面预览：两个地址都空白时零请求", () =>
        {
            var result = Wait(ArtworkPreview.RemoteAsync<string>(new RemoteImageInfo { ThumbnailUrl = " \t", Url = "\r\n" },
                (_, _) => throw new AssertionException("empty addresses must not load"), default));
            Assert.Null(result);
        });
    }

    private static void RegisterFailures()
    {
        foreach (var (name, error) in new (string, Exception?)[]
        {
            ("空图片", null),
            ("HTTP 404", new EmbyApiException("synthetic missing", HttpStatusCode.NotFound)),
            ("HTTP 500", new EmbyApiException("synthetic unavailable", HttpStatusCode.InternalServerError)),
            ("HTTP 503", new EmbyApiException("synthetic unavailable", HttpStatusCode.ServiceUnavailable)),
            ("连接已耗尽", new EmbyUnreachableException("synthetic offline")),
            ("坏数据", new InvalidDataException("synthetic corrupt")),
            ("WIC 坏图片", new COMException("synthetic decoder", unchecked((int)0x88982F60))),
            ("WIC 坏数据流", new COMException("synthetic decoder", unchecked((int)0x88982F70)))
        })
            Test("封面预览：缩略图" + name + "后读取同张原图", () =>
            {
                var calls = new List<string>();
                var result = Wait(ArtworkPreview.RemoteAsync<string>(new RemoteImageInfo { ThumbnailUrl = Thumbnail, Url = Original },
                    (address, _) =>
                    {
                        calls.Add(address);
                        return address == Original ? Task.FromResult<string?>("original picture")
                            : error is null ? Task.FromResult<string?>(null) : Task.FromException<string?>(error);
                    }, default));
                Assert.Equal("original picture", result);
                Assert.True(new[] { Thumbnail, Original }.SequenceEqual(calls));
            });

        foreach (var error in new Exception?[] { null, new InvalidDataException("synthetic original corrupt") })
            Test("封面预览：缩略图与原图均不可用时返回空且各读一次：" + (error?.GetType().Name ?? "null"), () =>
            {
                var calls = new List<string>();
                var result = Wait(ArtworkPreview.RemoteAsync<string>(new RemoteImageInfo { ThumbnailUrl = Thumbnail, Url = Original },
                    (address, _) =>
                    {
                        calls.Add(address);
                        return error is null ? Task.FromResult<string?>(null) : Task.FromException<string?>(error);
                    }, default));
                Assert.Null(result);
                Assert.True(new[] { Thumbnail, Original }.SequenceEqual(calls));
            });

        foreach (var (name, error) in new (string, Exception)[]
        {
            ("文件权限", new UnauthorizedAccessException()),
            ("HTTP 403", new EmbyApiException("synthetic denied", HttpStatusCode.Forbidden)),
            ("HTTP 401", new EmbyApiException("synthetic expired", HttpStatusCode.Unauthorized)),
            ("身份拒绝", new EmbyAuthenticationException("synthetic rejected")),
            ("令牌失效", new EmbyTokenExpiredException()),
            ("来源取消", new OperationCanceledException()),
            ("低内存", new OutOfMemoryException()),
            ("未知解码错误", new COMException("synthetic generic", unchecked((int)0x80004005))),
            ("一般 IO", new IOException("synthetic io")),
            ("参数错误", new ArgumentException("synthetic bug"))
        })
            Test("封面预览：不得把" + name + "吞成原图回退", () =>
            {
                var calls = new List<string>();
                var thrown = Assert.Catch<Exception>(() => Wait(ArtworkPreview.RemoteAsync<string>(
                    new RemoteImageInfo { ThumbnailUrl = Thumbnail, Url = Original }, (address, _) =>
                    {
                        calls.Add(address);
                        return Task.FromException<string?>(error);
                    }, default)));
                Assert.True(ReferenceEquals(error, thrown), "传播原异常，不能换成无图");
                Assert.Equal(Thumbnail, calls.Single());
            });
    }

    private static void RegisterCancellation()
    {
        Test("封面预览：预取消时零图片请求", () =>
        {
            using var lifetime = new CancellationTokenSource();
            lifetime.Cancel();
            Assert.Throws<OperationCanceledException>(() => Wait(ArtworkPreview.RemoteAsync<string>(
                new RemoteImageInfo { ThumbnailUrl = Thumbnail, Url = Original },
                (_, _) => throw new AssertionException("canceled load must not start"), lifetime.Token)));
        });

        foreach (var outcome in new[] { "null", "bad-image", "picture" })
            Test("封面预览：缩略图响应同刻取消后不启动原图：" + outcome, () =>
            {
                using var lifetime = new CancellationTokenSource();
                var calls = 0;
                Assert.Throws<OperationCanceledException>(() => Wait(ArtworkPreview.RemoteAsync<string>(
                    new RemoteImageInfo { ThumbnailUrl = Thumbnail, Url = Original }, (_, _) =>
                    {
                        calls++;
                        lifetime.Cancel();
                        return outcome == "bad-image" ? Task.FromException<string?>(new InvalidDataException("synthetic corrupt"))
                            : Task.FromResult(outcome == "picture" ? "old picture" : null);
                    }, lifetime.Token)));
                Assert.Equal(1, calls);
            });
    }

    private static void RegisterContext()
    {
        Test("封面预览：异步缩略图失败后原图回调仍在调用方上下文", () =>
        {
            var prior = SynchronizationContext.Current;
            using var context = new PreviewContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var first = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var calls = new List<SynchronizationContext?>();
                var loading = ArtworkPreview.RemoteAsync<string>(new RemoteImageInfo { ThumbnailUrl = Thumbnail, Url = Original },
                    (address, _) =>
                    {
                        calls.Add(SynchronizationContext.Current);
                        return address == Thumbnail ? first.Task : Task.FromResult<string?>("picture");
                    }, default);
                Task.Run(() => first.SetException(new InvalidDataException("synthetic corrupt"))).GetAwaiter().GetResult();
                context.DrainUntil(loading);
                Assert.Equal("picture", Wait(loading));
                Assert.Equal(2, calls.Count);
                Assert.True(calls.All(seen => ReferenceEquals(context, seen)));
            }
            finally { SynchronizationContext.SetSynchronizationContext(prior); }
        });
    }

    private static void RegisterProxy()
    {
        Test("封面预览：Emby 通过服务器 GET 代理且凭据只在请求头", () =>
        {
            const string source = "https://images.example.invalid/海报.png?size=large&name=a+b#crop";
            const string token = "artwork-fixture-token";
            var root = new Uri("http://192.0.2.10:8896/emby/");
            var device = DeviceIdentity.Create("artwork-fixture-device", "1.0.0");
            using var transport = new PreviewTransport();
            using var http = new EmbyHttp(transport);
            var client = new EmbyClient(http, new EmbyConnection(root, token, "fixture-user", "fixture", "fixture", device));
            var bytes = Wait(client.GetRemoteImageBytesAsync(source, default));
            var request = transport.Requests.Single();
            Assert.Equal("GET", request.Method);
            Assert.Equal(root.AbsoluteUri + "Images/Remote?ImageUrl=" + Uri.EscapeDataString(source), request.Url);
            Assert.DoesNotContain(token, request.Url);
            Assert.DoesNotContain("RemoteImages/Download", request.Url);
            Assert.False(request.HasBody);
            Assert.Equal("preview-image", Encoding.UTF8.GetString(bytes));
            Assert.Equal(token, request.Headers["X-Emby-Token"]);
            Assert.Equal(device.ToAuthorizationHeader(), request.Headers["X-Emby-Authorization"]);
            Assert.False(request.Headers.ContainsKey("Authorization"));
        });

        Test("封面预览：Jellyfin 不支持 Emby 远程图片代理时零请求拒绝", () =>
        {
            using var transport = new PreviewTransport();
            using var http = new EmbyHttp(transport);
            var client = new EmbyClient(http, new EmbyConnection(new Uri("http://192.0.2.10:8896/"),
                "artwork-fixture-token", "fixture-user", "fixture", "fixture", DeviceIdentity.Create("artwork-fixture-device", "1.0.0"))
            { ServerKind = MediaServerKind.Jellyfin });
            Assert.Throws<NotSupportedException>(() => Wait(client.GetRemoteImageBytesAsync(Original, default)));
            Assert.Equal(0, transport.Requests.Count);
        });
    }

    private static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private sealed class PreviewContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Work, object? State)> _queue = new();
        public override void Post(SendOrPostCallback work, object? state) => _queue.Add((work, state));
        public void DrainUntil(Task work)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!work.IsCompleted && DateTime.UtcNow < deadline)
                if (_queue.TryTake(out var next, 100)) next.Work(next.State);
            Assert.True(work.IsCompleted, "预览上下文回调必须完成");
        }
        public void Dispose() => _queue.Dispose();
    }

    private sealed class PreviewTransport : HttpMessageHandler
    {
        internal sealed record Request(string Method, string Url, bool HasBody, Dictionary<string, string> Headers);
        internal List<Request> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new(request.Method.Method, request.RequestUri!.AbsoluteUri, request.Content is not null,
                request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase)));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("preview-image")) });
        }
    }
}
