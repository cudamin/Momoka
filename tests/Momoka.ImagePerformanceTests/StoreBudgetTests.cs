using System.Net;
using Momoka.Emby;
using static Momoka.Tests.Program;

namespace Momoka.Tests;

internal static class StoreBudgetTests
{
    internal static void Register()
    {
        Test("实际Store：500冷缓存不同键离开后有界清零，不落旧缓存", async () =>
        {
            await using var fixture = new StoreFixture();
            var gate = Pending<HttpResponseMessage>();
            fixture.Handler.Respond = (_, _, token) => gate.Task.WaitAsync(token);
            var sources = new List<CancellationTokenSource>();
            var waits = new List<Task<byte[]?>>();
            var peakKeys = 0;
            var peakQueued = 0;
            var peakDemands = 0;
            for (var i = 0; i < 500; i++)
            {
                var source = new CancellationTokenSource();
                sources.Add(source);
                waits.Add(fixture.Get($"poster-{i}", source.Token));
                peakKeys = Math.Max(peakKeys, fixture.Downloads.Running);
                peakQueued = Math.Max(peakQueued, fixture.Downloads.Queued);
                peakDemands = Math.Max(peakDemands, fixture.Downloads.Demands);
                Assert.True(fixture.Downloads.Executing <= 6 && fixture.Downloads.Queued <= 48 && fixture.Downloads.Demands <= 128);
                Assert.True(fixture.Downloads.Running <= 54);
            }
            await Eventually(() => fixture.Handler.Active == 6, "执行数应达但不超过6");
            foreach (var source in sources) source.Cancel();
            foreach (var wait in waits) await Cancelled(wait);
            await Eventually(() => fixture.Downloads.Running == 0 && fixture.Handler.Active == 0, "500退出后应归零");
            Assert.Equal(0, fixture.Downloads.Demands);
            Assert.Equal(0, fixture.Downloads.Queued);
            Assert.Equal(6, fixture.Handler.Total, "只启动6真实生产HTTP请求，不是500等待任务");
            Assert.Equal(0, Directory.GetFiles(Path.Combine(fixture.Root, "images"), "*.img").Length);
            Measurements.Add(new
            {
                kind = "actual-store-500-abandoned",
                keys = 500,
                peakKeys,
                peakQueued,
                peakDemands,
                handlerCalls = fixture.Handler.Total,
                peakHandlerRequests = fixture.Handler.PeakActive,
                sharedRemaining = fixture.Downloads.Running,
                demandsRemaining = fixture.Downloads.Demands
            });
            foreach (var source in sources) source.Dispose();
        });

        Test("实际Store：同图首等待取消，另一张拿到字节并持久化", async () =>
        {
            await using var fixture = new StoreFixture();
            var gate = Pending<HttpResponseMessage>();
            CancellationToken shared = default;
            fixture.Handler.Respond = (_, _, token) => { shared = token; return gate.Task; };
            using var firstSource = new CancellationTokenSource();
            var first = fixture.Get("same", firstSource.Token);
            var staying = fixture.Get("same");
            await Eventually(() => fixture.Handler.Active == 1 && fixture.Downloads.Demands == 2, "两个等待共享一个请求");
            firstSource.Cancel();
            await Cancelled(first);
            Assert.False(shared.IsCancellationRequested);
            gate.SetResult(ImageHandler.Bytes([7, 8, 9]));
            Assert.True((await staying.WaitAsync(Deadline))!.SequenceEqual(new byte[] { 7, 8, 9 }));
            await Eventually(() => fixture.Downloads.Running == 0, "完成退场");
            Assert.True((await fixture.Get("same"))!.SequenceEqual(new byte[] { 7, 8, 9 }));
            Assert.Equal(1, fixture.Handler.Total, "第二次走实际磁盘缓存");
            Assert.Equal(1, Directory.GetFiles(Path.Combine(fixture.Root, "images"), "*.img").Length);
        });

        Test("实际Store：零需求后迟到旧响应不写盘，新同键可重试", async () =>
        {
            await using var fixture = new StoreFixture();
            var stale = Pending<HttpResponseMessage>();
            fixture.Handler.Respond = (_, call, _) => call == 1 ? stale.Task : Task.FromResult(ImageHandler.Bytes([9]));
            using var source = new CancellationTokenSource();
            var old = fixture.Get("same", source.Token);
            await Eventually(() => fixture.Handler.Total == 1, "旧请求启动");
            source.Cancel();
            await Cancelled(old);
            var fresh = fixture.Get("same");
            Assert.True((await fresh.WaitAsync(Deadline))!.SequenceEqual(new byte[] { 9 }));
            stale.SetResult(ImageHandler.Bytes([1]));
            await Eventually(() => fixture.Downloads.Running == 0, "迟到响应也退出");
            Assert.True((await fixture.Get("same"))!.SequenceEqual(new byte[] { 9 }));
            Assert.Equal(2, fixture.Handler.Total);
        });

        Test("实际Store：账号寿命取消同时清理正在执行与排队需求", async () =>
        {
            await using var fixture = new StoreFixture();
            var gate = Pending<HttpResponseMessage>();
            fixture.Handler.Respond = (_, _, token) => gate.Task.WaitAsync(token);
            var waits = Enumerable.Range(0, 40).Select(i => fixture.Get($"lifetime-{i}")).ToArray();
            await Eventually(() => fixture.Handler.Active == 6, "先占执行槽");
            fixture.Scope.IdentityState.Lifetime.Cancel();
            foreach (var wait in waits) await Cancelled(wait);
            await Eventually(() => fixture.Downloads.Running == 0, "寿命取消连队列一起撤销");
            Assert.Equal(0, fixture.Downloads.Demands);
            Assert.Equal(0, fixture.Downloads.Queued);
        });

        Test("实际Store：网络故障原重试表保留，最终故障移除后新需求重试", async () =>
        {
            await using var fixture = new StoreFixture();
            fixture.Handler.Respond = (_, call, _) => Task.FromResult(call <= 3
                ? ImageHandler.Bytes([], HttpStatusCode.InternalServerError) : ImageHandler.Bytes([4]));
            Assert.True(await Failure(fixture.Get("failure")) is EmbyApiException);
            Assert.Equal(3, fixture.Handler.Total);
            await Eventually(() => fixture.Downloads.Running == 0, "失败也放开键");
            Assert.True((await fixture.Get("failure").WaitAsync(Deadline))!.SequenceEqual(new byte[] { 4 }));
            Assert.Equal(4, fixture.Handler.Total);
        });

        Test("实际Store：取消中止网络重试等待，不当没图或继续填充", async () =>
        {
            await using var fixture = new StoreFixture();
            fixture.Handler.Respond = (_, _, _) => Task.FromResult(ImageHandler.Bytes([], HttpStatusCode.InternalServerError));
            using var source = new CancellationTokenSource();
            var wait = fixture.Get("retry-delay", source.Token);
            await Eventually(() => fixture.Handler.Total == 1 && fixture.Handler.Active == 0, "进入重试等待");
            source.Cancel();
            await Cancelled(wait);
            await Eventually(() => fixture.Downloads.Running == 0, "重试delay必须被取消");
            Assert.Equal(1, fixture.Handler.Total);
        });

        Test("实际Store：明确404仍为空且移除，新需求能正常取图", async () =>
        {
            await using var fixture = new StoreFixture();
            fixture.Handler.Respond = (_, call, _) => Task.FromResult(call == 1 ? ImageHandler.Bytes([], HttpStatusCode.NotFound) : ImageHandler.Bytes([2]));
            Assert.Null(await fixture.Get("404"));
            await Eventually(() => fixture.Downloads.Running == 0, "404不是永久共享结果");
            Assert.True((await fixture.Get("404"))!.SequenceEqual(new byte[] { 2 }));
            Assert.Equal(2, fixture.Handler.Total);
        });

        Test("实际Store：chapter图片入口遵守同样共享预算", async () =>
        {
            await using var fixture = new StoreFixture();
            var gate = Pending<HttpResponseMessage>();
            fixture.Handler.Respond = (_, _, token) => gate.Task.WaitAsync(token);
            using var source = new CancellationTokenSource();
            var waits = Enumerable.Range(0, 70).Select(i => fixture.Images.GetChapterAsync(fixture.Scope,
                $"chapter-{i}", 0, "tag", 1280, source.Token)).ToArray();
            Assert.True(fixture.Downloads.Running <= 54 && fixture.Downloads.Queued <= 48);
            source.Cancel();
            foreach (var wait in waits) await Cancelled(wait);
            await Eventually(() => fixture.Downloads.Running == 0, "chapter取消清理");
        });

        Test("实际Store：坏缓存恢复共享同一预算，首等待取消不杀剩余解码需求", async () =>
        {
            await using var fixture = new StoreFixture();
            fixture.Handler.Respond = (_, call, _) => Task.FromResult(ImageHandler.Bytes(call == 1 ? new byte[] { 1 } : new byte[] { 9 }));
            await fixture.Get("recovery");
            var gate = Pending<byte[]>();
            fixture.Handler.Respond = async (_, _, token) => ImageHandler.Bytes(await gate.Task.WaitAsync(token));
            var decodes = 0;
            Task<object?> Decode(byte[] bytes)
            {
                Interlocked.Increment(ref decodes);
                if (bytes[0] == 1) throw new InvalidDataException("synthetic bad cache");
                return Task.FromResult<object?>(new object());
            }
            using var source = new CancellationTokenSource();
            var first = fixture.Images.GetDecodedAsync(fixture.Scope, "recovery", EmbyImageStore.Backdrop,
                "tag", 1280, Decode, source.Token);
            // The second caller arrives after invalidation and joins as a normal cache miss.
            // Both-recovery callers do not exercise ownership when the only recovery waiter leaves.
            await Eventually(() => fixture.Handler.Active == 1, "坏缓存已失效且恢复请求已开始");
            var staying = fixture.Images.GetDecodedAsync(fixture.Scope, "recovery", EmbyImageStore.Backdrop,
                "tag", 1280, Decode, CancellationToken.None);
            await Eventually(() => fixture.Handler.Active == 1 && fixture.Downloads.Demands == 2, "恢复与普通取图共享一键");
            source.Cancel();
            await Cancelled(first);
            gate.TrySetResult([9]);
            Assert.NotNull(await staying.WaitAsync(Deadline));
            await Eventually(() => fixture.Downloads.Running == 0, "恢复退场");
            Assert.Equal(2, fixture.Handler.Total);
            Assert.True((await fixture.Get("recovery"))!.SequenceEqual(new byte[] { 9 }));
            Assert.Equal(2, fixture.Handler.Total, "恢复字节必须已持久化，普通读取不能再发 GET");
            Assert.True(decodes >= 2);
        });

        Test("实际Store：只释放一槽时新可见先于旧排队，其他活动需求保持", async () =>
        {
            await using var fixture = new StoreFixture();
            var blocker = Pending<HttpResponseMessage>();
            var freshResult = Pending<HttpResponseMessage>();
            fixture.Handler.Respond = (id, _, token) => id.StartsWith("active-", StringComparison.Ordinal)
                ? blocker.Task.WaitAsync(token) : id == "new-visible" ? freshResult.Task.WaitAsync(token)
                : Task.FromResult(ImageHandler.Bytes([2]));
            var sources = Enumerable.Range(0, 6).Select(_ => new CancellationTokenSource()).ToArray();
            var active = sources.Select((source, i) => fixture.Get($"active-{i}", source.Token)).ToArray();
            await Eventually(() => fixture.Handler.Active == 6, "6活动下载先启动");
            var older = fixture.Get("older-visible");
            var fresh = fixture.Get("new-visible");
            await Eventually(() => fixture.Downloads.Queued == 2, "确保新旧都完成缓存检查且已排队");
            // Release ONE slot: simultaneous free slots legitimately start both keys on independent
            // workers, whose arrival order at the handler is not a scheduling priority contract.
            sources[0].Cancel();
            await Cancelled(active[0]);
            await Eventually(() => fixture.Handler.Calls.ContainsKey("new-visible"), "新可见键应先获得唯一空槽");
            Assert.False(fixture.Handler.Calls.ContainsKey("older-visible"));
            Assert.Equal(6, fixture.Handler.Active, "其余5个活动需求未被抢占");
            freshResult.SetResult(ImageHandler.Bytes([3]));
            Assert.NotNull(await fresh.WaitAsync(Deadline));
            Assert.NotNull(await older.WaitAsync(Deadline));
            foreach (var source in sources) source.Cancel();
            foreach (var wait in active) await Cancelled(wait);
            foreach (var source in sources) source.Dispose();
        });
    }
}
