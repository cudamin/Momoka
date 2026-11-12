using System.Net;
using System.Text.Json;
using Momoka.Emby;
using Momoka.Shell;

namespace Momoka.MusicViewModelTests;

internal static class MusicPagingProbeTests
{
    internal static IEnumerable<(string Name, Func<Task> Run)> Cases()
    {
        yield return ("原生分页探针匿名源准确返回81/160三类型，末页停止", PagesAsync);
        yield return ("原生分页探针第二页失败一次后恢复原偏移", RetryAsync);
        yield return ("原生分页探针拒绝真实地址、写入和非法偏移", RejectAsync);
        yield return ("原生分页读数拒绝漏项、乱序、旧类型和伪末页", ReadingsAsync);
        yield return ("原生分页探针图片失败与取消不读真实数据", ImagesAndCancelAsync);
    }

    private static async Task PagesAsync()
    {
        foreach (var type in new[] { EmbyItemType.MusicAlbum, EmbyItemType.MusicArtist, EmbyItemType.Playlist })
            foreach (var total in new[] { 81, 160 })
            {
                var server = new MusicPagingProbeServer { Total = total };
                var all = new List<EmbyItem>();
                for (var offset = 0; offset < total; offset += 80)
                {
                    using var request = Request(type, offset);
                    using var reply = await server.ReplyAsync(request, default);
                    Require(reply.StatusCode == HttpStatusCode.OK, "匿名首/末页未成功");
                    var page = JsonSerializer.Deserialize<ItemsResult>(await reply.Content.ReadAsStringAsync())!;
                    Require(page.TotalRecordCount == total && page.Items.Count == Math.Min(80, total - offset), "响应未遵循偏移/上限");
                    all.AddRange(page.Items);
                }
                MusicPagingProbeServer.ValidateLoaded(total, all, false, type);
                using var endRequest = Request(type, total);
                using var endReply = await server.ReplyAsync(endRequest, default);
                Require(JsonSerializer.Deserialize<ItemsResult>(await endReply.Content.ReadAsStringAsync())!.Items.Count == 0, "末页以后没有空响应");
                Require(server.Requests.Select(entry => entry.Offset).SequenceEqual([0, 80, total]), "记录没有保留真实分页参数");
                Require(server.Requests.All(entry => entry.Type == type && entry.Limit == 80), "Artists路由或类型/上限不正确");
            }
    }

    private static async Task RetryAsync()
    {
        var server = new MusicPagingProbeServer { Total = 160, FailNextMore = true };
        using var request = Request(EmbyItemType.Playlist, 80);
        using var failed = await server.ReplyAsync(request, default);
        Require(failed.StatusCode == HttpStatusCode.InternalServerError && !server.FailNextMore, "失败钩子没有只消耗一次");
        using var retry = await server.ReplyAsync(request, default);
        var page = JsonSerializer.Deserialize<ItemsResult>(await retry.Content.ReadAsStringAsync())!;
        Require(retry.IsSuccessStatusCode && page.Items.Count == 80 && page.Items[0].Id == MusicPagingProbeServer.ItemId(EmbyItemType.Playlist, 80), "重试没有恢复原偏移");
        Require(server.Requests.Count == 2 && server.Requests.All(entry => entry.Offset == 80), "重试计数或偏移记录不正确");
    }

    private static async Task RejectAsync()
    {
        foreach (var address in new[]
        {
            "https://example.com/Items?StartIndex=0&Limit=80",
            "https://fixture.invalid/Items?StartIndex=-1&Limit=80",
            "https://fixture.invalid/Items?StartIndex=not-number&Limit=80",
            "https://fixture.invalid/Items?StartIndex=0&Limit=0",
            "https://fixture.invalid/Items?StartIndex=0&Limit=1001",
            "https://fixture.invalid/Unknown?StartIndex=0&Limit=80",
            "https://fixture.invalid/Items?IncludeItemTypes=Movie&StartIndex=0&Limit=80"
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            var server = new MusicPagingProbeServer();
            try { using var response = await server.ReplyAsync(request, default); throw new Exception("拒绝夹具错误接受：" + address); }
            catch (InvalidOperationException) { Require(server.Requests.Count == 0, "拒绝以后仍记录/执行请求"); }
        }
        using var post = new HttpRequestMessage(HttpMethod.Post, "https://fixture.invalid/Items");
        try { using var response = await new MusicPagingProbeServer().ReplyAsync(post, default); throw new Exception("分页探针接受了写入"); }
        catch (InvalidOperationException) { }
    }

    private static async Task ReadingsAsync()
    {
        var server = new MusicPagingProbeServer { Total = 81 };
        var all = new List<EmbyItem>();
        foreach (var offset in new[] { 0, 80 })
        {
            using var request = Request(EmbyItemType.MusicAlbum, offset);
            using var response = await server.ReplyAsync(request, default);
            all.AddRange(JsonSerializer.Deserialize<ItemsResult>(await response.Content.ReadAsStringAsync())!.Items);
        }
        MusicPagingProbeServer.ValidateLoaded(81, all, false, EmbyItemType.MusicAlbum);
        RejectReading(all.Take(80).ToArray(), false);
        RejectReading(all.ToArray(), true);
        RejectReading(all.Reverse<EmbyItem>().ToArray(), false);
        var reordered = all.ToArray();
        (reordered[10], reordered[11]) = (reordered[11], reordered[10]);
        RejectReading(reordered, false);
        var wrongType = all.Select(item => new EmbyItem { Id = item.Id, Type = EmbyItemType.Playlist }).ToArray();
        RejectReading(wrongType, false);
        void RejectReading(IReadOnlyList<EmbyItem> items, bool more)
        {
            try { MusicPagingProbeServer.ValidateLoaded(81, items, more, EmbyItemType.MusicAlbum); throw new Exception("读数检查接受伪通过"); }
            catch (InvalidOperationException) { }
        }
    }

    private static async Task ImagesAndCancelAsync()
    {
        var server = new MusicPagingProbeServer();
        using var image = new HttpRequestMessage(HttpMethod.Get, "https://fixture.invalid/Items/a/Images/Primary");
        using var response = await server.ReplyAsync(image, default);
        Require(response.StatusCode == HttpStatusCode.NotFound && server.Requests.Count == 0, "分页夹具错误地加载图片数据");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var request = Request(EmbyItemType.MusicAlbum, 0);
        try { using var reply = await server.ReplyAsync(request, canceled.Token); throw new Exception("取消请求仍被执行"); }
        catch (OperationCanceledException) { Require(server.Requests.Count == 0, "取消请求污染分页记录"); }
    }

    private static HttpRequestMessage Request(string type, int offset) => new(HttpMethod.Get,
        $"https://fixture.invalid/{(type == EmbyItemType.MusicArtist ? "Artists" : "Items")}?IncludeItemTypes={type}&StartIndex={offset}&Limit=80");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
