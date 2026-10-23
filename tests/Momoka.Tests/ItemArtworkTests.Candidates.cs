using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class ItemArtworkTests
{
    private static void RegisterCandidates()
    {
        foreach (var code in new uint[] { 0x88982F07, 0x88982F50, 0x88982F60, 0x88982F61, 0x88982F62, 0x88982F63, 0x88982F70 })
            Test($"R-015：明确图片格式/数据错误 {code:X8} 才降到下一候选", () =>
                AssertCandidateFallback(new COMException("synthetic decoder failure", unchecked((int)code))));
        Test("R-015：WinRT 投影为普通异常时仍按明确坏数据HRESULT降级", () =>
            AssertCandidateFallback(new ProjectedImageException(unchecked((int)0x88982F60))));
        Test("R-015：命名坏数据异常允许备用图，不改变候选次序", () =>
            AssertCandidateFallback(new InvalidDataException("synthetic corrupt image")));
        Test("R-015：已耗尽本候选的连接失败允许备用图", () =>
            AssertCandidateFallback(new EmbyUnreachableException("synthetic offline failure")));
        foreach (var code in new[] { HttpStatusCode.NotFound, HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable })
            Test("R-015：可降级图片 HTTP " + code, () =>
                AssertCandidateFallback(new EmbyApiException("synthetic image failure", code)));

        foreach (var (name, error) in new (string, Exception)[]
        {
            ("来源取消", new OperationCanceledException()),
            ("超时取消未经映射", new TaskCanceledException()),
            ("本地权限", new UnauthorizedAccessException()),
            ("低内存", new OutOfMemoryException()),
            ("参数程序错误", new ArgumentException("synthetic bug")),
            ("一般IO不是明确坏数据", new IOException()),
            ("未知图错误", new COMException("synthetic generic", unchecked((int)0x80004005))),
            ("WIC权限", new COMException("synthetic denied", unchecked((int)0x80070005))),
            ("WIC取消", new COMException("synthetic aborted", unchecked((int)0x80004004))),
            ("WIC内存", new COMException("synthetic memory", unchecked((int)0x8007000E))),
            ("服务器403", new EmbyApiException("synthetic denied", HttpStatusCode.Forbidden)),
            ("服务器401", new EmbyApiException("synthetic expired", HttpStatusCode.Unauthorized)),
            ("身份拒绝", new EmbyAuthenticationException("synthetic identity rejection")),
            ("程序空引用", new NullReferenceException("synthetic bug"))
        })
            Test("R-015：不得吞成备用图的错误：" + name, () =>
            {
                var asked = 0;
                var thrown = Assert.Catch<Exception>(() => ArtworkCandidates.FirstAsync<int, string>([1, 2], (_, _) =>
                {
                    asked++;
                    return Task.FromException<string?>(error);
                }, default).GetAwaiter().GetResult());
                Assert.True(ReferenceEquals(error, thrown), "传播原异常而不是替换成无图");
                Assert.Equal(1, asked);
            });

        Test("R-015：空图继续，第一张成功后不再读取后续候选", () =>
        {
            var asked = new List<int>();
            var result = ArtworkCandidates.FirstAsync<int, string>([1, 2, 3], (candidate, _) =>
            {
                asked.Add(candidate);
                return Task.FromResult(candidate == 1 ? null : "picture");
            }, default).GetAwaiter().GetResult();
            Assert.Equal("picture", result);
            Assert.Equal("1,2", string.Join(',', asked));
        });

        Test("R-015：全部明确坏图才返回空，每个候选只试一次", () =>
        {
            var asked = 0;
            var result = ArtworkCandidates.FirstAsync<int, string>([1, 2], (_, _) =>
            {
                asked++;
                return Task.FromException<string?>(new InvalidDataException("synthetic corrupt"));
            }, default).GetAwaiter().GetResult();
            Assert.Null(result);
            Assert.Equal(2, asked);
        });

        Test("R-015：调用者预取消时零候选读取", () =>
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var asked = 0;
            Assert.Throws<OperationCanceledException>(() => ArtworkCandidates.FirstAsync<int, string>([1], (_, _) =>
            {
                asked++;
                return Task.FromResult<string?>("picture");
            }, canceled.Token).GetAwaiter().GetResult());
            Assert.Equal(0, asked);
        });

        Test("R-015：首候选失败时同时取消，不再降级到备用", () =>
        {
            using var canceled = new CancellationTokenSource();
            var asked = 0;
            Assert.Throws<OperationCanceledException>(() => ArtworkCandidates.FirstAsync<int, string>([1, 2], (_, _) =>
            {
                asked++;
                canceled.Cancel();
                return Task.FromException<string?>(new InvalidDataException("synthetic corrupt"));
            }, canceled.Token).GetAwaiter().GetResult());
            Assert.Equal(1, asked);
        });

        Test("R-015：成功图迟到但来源已经取消，不发布该成功", () =>
        {
            using var canceled = new CancellationTokenSource();
            var image = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = ArtworkCandidates.FirstAsync<int, string>([1, 2], (_, _) => image.Task, canceled.Token);
            canceled.Cancel();
            image.SetResult("late picture");
            Assert.Throws<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
        });

        Test("R-015：空候选没有请求，参数不合法仍明确拒绝", () =>
        {
            Assert.Null(ArtworkCandidates.FirstAsync<int, string>([], (_, _) => throw new AssertionException("zero requests"), default)
                .GetAwaiter().GetResult());
            Assert.Throws<ArgumentNullException>(() => ArtworkCandidates.FirstAsync<int, string>(null!, (_, _) => Task.FromResult<string?>(null), default)
                .GetAwaiter().GetResult());
            Assert.Throws<ArgumentNullException>(() => ArtworkCandidates.FirstAsync<int, string>([], null!, default).GetAwaiter().GetResult());
        });

        Test("R-015：候选失败不会中断其它独立图片任务", () =>
        {
            var good = ArtworkCandidates.FirstAsync<int, string>([1], (_, _) => Task.FromResult<string?>("plate"), default);
            var fallback = ArtworkCandidates.FirstAsync<int, string>([1, 2], (candidate, _) => candidate == 1
                ? Task.FromException<string?>(new InvalidDataException("synthetic corrupt")) : Task.FromResult<string?>("hero"), default);
            Task.WhenAll(good, fallback).GetAwaiter().GetResult();
            Assert.Equal("plate", good.Result);
            Assert.Equal("hero", fallback.Result);
        });

        Test("R-015：备用候选回调仍在原同步上下文，不把WinUI解码移到池线程", () =>
        {
            var prior = SynchronizationContext.Current;
            var context = new CandidateContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var first = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var seen = new List<SynchronizationContext?>();
                var work = ArtworkCandidates.FirstAsync<int, string>([1, 2], (candidate, _) =>
                {
                    seen.Add(SynchronizationContext.Current);
                    return candidate == 1 ? first.Task : Task.FromResult<string?>("second");
                }, default);
                Task.Run(() => first.SetException(new InvalidDataException("synthetic corrupt"))).GetAwaiter().GetResult();
                context.DrainUntil(work);
                Assert.Equal("second", work.GetAwaiter().GetResult());
                Assert.Equal(2, seen.Count);
                Assert.True(seen.All(item => ReferenceEquals(context, item)));
            }
            finally { SynchronizationContext.SetSynchronizationContext(prior); }
        });
    }

    private static void AssertCandidateFallback(Exception error)
    {
        var asked = new List<ArtworkRef>();
        var candidates = new[] { new ArtworkRef("show", "Backdrop", "old"), new ArtworkRef("episode", "Art", "current") };
        var result = ArtworkCandidates.FirstAsync<ArtworkRef, string>(candidates, (candidate, _) =>
        {
            asked.Add(candidate);
            return candidate.ItemId == "show" ? Task.FromException<string?>(error) : Task.FromResult<string?>("backup image");
        }, default).GetAwaiter().GetResult();
        Assert.Equal("backup image", result);
        Assert.True(candidates.SequenceEqual(asked));
    }

    private sealed class ProjectedImageException : Exception
    {
        internal ProjectedImageException(int code) => HResult = code;
    }

    private sealed class CandidateContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Work, object? State)> _queue = new();
        public override void Post(SendOrPostCallback work, object? state) => _queue.Add((work, state));
        public void DrainUntil(Task work)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!work.IsCompleted && DateTime.UtcNow < deadline)
                if (_queue.TryTake(out var next, 100)) next.Work(next.State);
            Assert.True(work.IsCompleted, "候选上下文回调必须完成");
        }
    }
}
