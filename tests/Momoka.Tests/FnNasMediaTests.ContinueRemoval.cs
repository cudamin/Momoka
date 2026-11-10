using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.FnNas;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class FnNasMediaTests
{
    private static void RegisterContinueRemoval()
    {
        Test("飞牛续播移除：只对服务器播放记录生成菜单，零进度记录也可移除", () =>
        {
            using var fixture = new Fixture();
            fixture.Login();
            var scope = fixture.Session.Capture();
            fixture.Transport.ContinueRecords = """[{"guid":"episode","parent_guid":"season","tv_guid":"series","type":"Episode","ts":0}]""";
            var record = scope.ExecuteAsync((client, ct) => client.GetResumeAsync(10, ct), default).GetAwaiter().GetResult().Single();
            Assert.True(record.IsFnNasResumeRecord);
            Assert.Equal("episode", record.Id);
            Assert.Equal("series", record.SeriesId);
            Assert.Equal(ItemCommand.RemoveContinueWatching, ItemMenu.For(record, fnNas: true)
                .Single(row => row.Label == "从继续观看移除").Command);
            var plain = FnNasMediaAdapter.Item(JsonNode.Parse("""{"guid":"episode","type":"Episode","ts":12}""")!.AsObject());
            Assert.False(ItemMenu.For(plain, fnNas: true).Any(row => row.Command == ItemCommand.RemoveContinueWatching));
            Assert.False(ItemMenu.For(record, fnNas: true, allowResumeRemoval: false).Any(row => row.Command == ItemCommand.RemoveContinueWatching));
            Assert.True(ItemMenu.Supports(ItemCommand.RemoveContinueWatching, jellyfin: false, fnNas: true));
            foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin, MediaServerKind.Plex })
                Assert.False(ItemMenu.Supports(ItemCommand.RemoveContinueWatching, jellyfin: kind == MediaServerKind.Jellyfin, plex: kind == MediaServerKind.Plex));
            var before = ItemFingerprint.Card(record);
            record.IsFnNasResumeRecord = false;
            Assert.False(before == ItemFingerprint.Card(record), "旧卡不能保留已经失效的续播移除入口");
            Assert.DoesNotContain("IsFnNasResumeRecord", JsonSerializer.Serialize(record));
        });
        Test("飞牛续播移除：DELETE只传记录guid，不标未看、不清零、不夹带补读", () =>
        {
            using var fixture = new Fixture();
            fixture.Login();
            var scope = fixture.Session.Capture();
            var reads = fixture.Transport.Paths.Count;
            scope.ExecuteAsync((client, ct) => client.RemoveFnNasContinueWatchingAsync("episode", ct), default).GetAwaiter().GetResult();
            Assert.Equal(reads + 1, fixture.Transport.Paths.Count);
            Assert.Equal(HttpMethod.Delete, fixture.Transport.LastMethod);
            Assert.Equal("/v/api/v1/play/record", fixture.Transport.LastPath);
            Assert.Equal("{\"item_guid\":\"episode\"}", fixture.Transport.LastBody);
        });
        Test("飞牛续播移除：拒绝空guid，预取消零请求，业务拒绝不算成功", () =>
        {
            using var fixture = new Fixture();
            fixture.Login();
            var scope = fixture.Session.Capture();
            var count = fixture.Transport.Paths.Count;
            Assert.Throws<ArgumentException>(() => scope.ExecuteAsync((client, ct) => client.RemoveFnNasContinueWatchingAsync("", ct), default).GetAwaiter().GetResult());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => scope.ExecuteAsync((client, ct) => client.RemoveFnNasContinueWatchingAsync("episode", ct), cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal(count, fixture.Transport.Paths.Count);
            fixture.Transport.ContinueDeleteDenied = true;
            Assert.Throws<EmbyApiException>(() => scope.ExecuteAsync((client, ct) => client.RemoveFnNasContinueWatchingAsync("episode", ct), default).GetAwaiter().GetResult());
            Assert.Equal(count + 1, fixture.Transport.Paths.Count);
        });
    }
}
