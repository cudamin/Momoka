using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class SearchProgressTests
{
    internal static void Register()
    {
        foreach (var precise in new[] { false, true })
        {
            Test("进度流：实际关键词/精确搜索接最终快照 precise=" + precise, () =>
            {
                using var context = new PumpContext();
                using var fixture = new SiteFixture();
                fixture.Transport.StreamReply = (request, _) =>
                {
                    Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                    Assert.DoesNotContain("anonymous-token", request.RequestUri!.AbsoluteUri);
                    Assert.Contains(precise ? "/search/media/42/stream?" : "/search/title/stream?", request.RequestUri.AbsoluteUri);
                    return Task.FromResult(Stream("{\"type\":\"progress\",\"value\":54,\"text\":\"已完成 9 / 15 个搜索来源\"}",
                        "{\"type\":\"append\",\"items\":[{\"torrent_info\":{\"title\":\"unverified\"}}]}",
                        "{\"type\":\"replace\",\"items\":[{\"torrent_info\":{\"title\":\"final\"}}]}",
                        "{\"type\":\"done\",\"total_items\":1}"));
                };
                if (precise)
                {
                    using var vm = new MoviePilotResourceViewModel();
                    vm.Attach(fixture.Service, ResourceFixture.Media, new FixtureLauncher());
                    context.Finish(vm.SearchAsync());
                    Assert.Equal("final", vm.Resources.Single().Title);
                    Assert.False(vm.SearchProgress.IsActive);
                }
                else
                {
                    using var vm = new MoviePilotVersionsViewModel();
                    vm.Attach(fixture.Service, new FixtureLauncher());
                    context.Finish(vm.SearchAsync("Target"));
                    Assert.Equal("final", vm.Versions.Single().Title);
                    Assert.False(vm.SearchProgress.IsActive);
                }
                Assert.Equal(1, fixture.Transport.StreamReads);
                Assert.Equal(0, fixture.Transport.Requests.Count);
            });
        }
        foreach (var events in new[]
        {
            new[] { "{\"type\":\"replace\",\"replace_batch\":true,\"batch_index\":0,\"batch_count\":2,\"total_items\":2,\"items\":[{}]}", "{\"type\":\"done\",\"items\":[]}" },
            new[] { "{\"type\":\"append\",\"items\":[{}]}" },
            new[] { "{\"type\":\"done\",\"items\":[{}],\"total_items\":2}" },
            new[] { "{\"type\":\"error\",\"message\":\"来源搜索失败\"}" },
            new[] { "not-json" }
        })
        {
            Test("进度流：不完整/错误不发布结果且不重搜 " + events[0], () =>
            {
                using var context = new PumpContext();
                using var fixture = new SiteFixture();
                fixture.Transport.StreamReply = (_, _) => Task.FromResult(Stream(events));
                using var vm = new MoviePilotVersionsViewModel();
                vm.Attach(fixture.Service, new FixtureLauncher());
                context.Finish(vm.SearchAsync("Target"));
                Assert.True(vm.NoticeOpen);
                Assert.Equal(0, vm.Versions.Count);
                Assert.False(vm.Busy);
                Assert.False(vm.SearchProgress.IsActive);
                Assert.Equal(0, fixture.Transport.Requests.Count, "断流不可悄悄重放搜索");
            });
        }
        Test("进度流：有序完整批次只提交最终数据", () =>
        {
            using var context = new PumpContext();
            using var fixture = new SiteFixture();
            fixture.Transport.StreamReply = (_, _) => Task.FromResult(Stream(
                "{\"type\":\"replace\",\"replace_batch\":true,\"batch_index\":0,\"batch_count\":2,\"total_items\":2,\"items\":[{\"torrent_info\":{\"title\":\"one\"}}]}",
                "{\"type\":\"append\",\"replace_batch\":true,\"batch_index\":1,\"batch_count\":2,\"total_items\":2,\"items\":[{\"torrent_info\":{\"title\":\"two\"}}]}",
                "{\"type\":\"done\",\"total_items\":2}"));
            using var vm = new MoviePilotVersionsViewModel();
            vm.Attach(fixture.Service, new FixtureLauncher());
            context.Finish(vm.SearchAsync("Target"));
            Assert.Equal(2, vm.Versions.Count);
            Assert.False(vm.NoticeOpen);
        });
        foreach (var code in new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotImplemented })
        {
            Test("进度流：旧服务端明确不支持才退回普通搜索 " + code, () =>
            {
                using var context = new PumpContext();
                using var fixture = new SiteFixture();
                fixture.Transport.StreamReply = (_, _) => Task.FromResult(new HttpResponseMessage(code));
                using var vm = new MoviePilotVersionsViewModel();
                vm.Attach(fixture.Service, new FixtureLauncher());
                context.Finish(vm.SearchAsync("Target"));
                Assert.Equal(1, fixture.Transport.StreamReads);
                Assert.Equal(1, fixture.Transport.Requests.Count);
                Assert.False(vm.NoticeOpen);
            });
        }
        Test("进度：比例边界、排队旧回调、结束与新搜索隔离", () =>
        {
            using var context = new PumpContext();
            var vm = new MoviePilotSearchProgressViewModel();
            var first = vm.Start("标题：A", 2011, () => true);
            first.Report(new(53.2, "已完成 9 / 15 个搜索来源"));
            context.Drain();
            Assert.Equal(54d, vm.Value); Assert.Equal("54%", vm.PercentText); Assert.False(vm.IsIndeterminate);
            first.Report(new(90, "旧回调"));
            vm.Stop();
            var second = vm.Start("标题：B", null, () => true);
            context.Drain();
            Assert.Equal(0d, vm.Value); Assert.True(vm.IsIndeterminate); Assert.DoesNotContain("旧回调", vm.Status);
            second.Report(new(101, "边界")); context.Drain(); Assert.Equal(100d, vm.Value);
            second.Report(new(double.NaN, "未知")); context.Drain(); Assert.True(vm.IsIndeterminate);
            vm.Stop(); second.Report(new(44, "迟到")); context.Drain(); Assert.False(vm.IsActive);
        });
        Test("进度流：UTF8跨块、多行data和心跳不丢事件", () =>
        {
            using var context = new PumpContext();
            using var fixture = new SiteFixture();
            fixture.Transport.StreamReply = (_, _) =>
            {
                var bytes = Encoding.UTF8.GetBytes(": heartbeat\r\n\r\ndata: {\"type\":\"done\",\r\ndata: \"items\":[{\"torrent_info\":{\"title\":\"中文资源\"}}],\"total_items\":1}\r\n\r\n");
                var content = new StreamContent(new SplitStream(bytes));
                content.Headers.ContentType = new("text/event-stream");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            };
            using var vm = new MoviePilotVersionsViewModel();
            vm.Attach(fixture.Service, new FixtureLauncher());
            context.Finish(vm.SearchAsync("中文资源"));
            Assert.Equal("中文资源", vm.Versions.Single().Title);
            Assert.False(vm.NoticeOpen);
        });
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.InternalServerError })
        {
            Test("进度流：权限或服务器错误不退回重搜 " + status, () =>
            {
                using var context = new PumpContext();
                using var fixture = new SiteFixture();
                fixture.Transport.StreamReply = (_, _) => Task.FromResult(new HttpResponseMessage(status)
                { Content = new StringContent("{\"message\":\"服务器明确拒绝\"}", Encoding.UTF8, "application/json") });
                using var vm = new MoviePilotVersionsViewModel();
                vm.Attach(fixture.Service, new FixtureLauncher());
                context.Finish(vm.SearchAsync("Target"));
                Assert.True(vm.NoticeOpen);
                Assert.Equal(1, fixture.Transport.StreamReads);
                Assert.Equal(0, fixture.Transport.Requests.Count);
            });
        }
        foreach (var cancel in new[] { "leave", "sites", "keyword" })
        {
            Test("进度：实际VM取消与迟到响应 " + cancel, () =>
            {
                using var context = new PumpContext();
                using var fixture = new SiteFixture();
                var pending = new TaskCompletionSource<HttpResponseMessage>();
                fixture.Transport.StreamReply = (_, _) => pending.Task;
                using var vm = new MoviePilotVersionsViewModel();
                vm.Attach(fixture.Service, new FixtureLauncher());
                var old = vm.SearchAsync("old");
                Assert.True(vm.SearchProgress.IsActive);
                if (cancel == "sites") vm.SearchSites.UseDefault = false;
                else if (cancel == "leave") vm.Cancel();
                else
                {
                    fixture.Transport.StreamReply = (_, _) => Task.FromResult(Stream("{\"type\":\"done\",\"items\":[]}"));
                    context.Finish(vm.SearchAsync("new"));
                }
                pending.SetResult(Stream("{\"type\":\"progress\",\"value\":75,\"text\":\"旧进度\"}", "{\"type\":\"done\",\"items\":[]}"));
                context.Finish(old);
                Assert.False(vm.SearchProgress.IsActive); Assert.False(vm.Busy);
                Assert.DoesNotContain("旧进度", vm.SearchProgress.Status);
            });
        }
    }

    private static HttpResponseMessage Stream(params string[] events) => new(HttpStatusCode.OK)
    { Content = new StringContent(string.Concat(events.Select(value => "data: " + value + "\n\n")), Encoding.UTF8, "text/event-stream") };

    private sealed class SplitStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
    }

    private sealed class PumpContext : SynchronizationContext, IDisposable
    {
        private readonly SynchronizationContext? _previous = Current;
        private readonly ConcurrentQueue<Action> _queue = new();
        public PumpContext() => SetSynchronizationContext(this);
        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue(() => d(state));
        internal void Drain() { while (_queue.TryDequeue(out var action)) action(); }
        internal void Finish(Task task)
        {
            var until = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < until) { Drain(); Thread.Sleep(1); }
            Assert.True(task.IsCompleted, "搜索任务超时");
            Drain(); ResourceFixture.Complete(task);
        }
        public void Dispose() => SetSynchronizationContext(_previous);
    }
}
