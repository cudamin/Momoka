using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 主页实时通道那两件纯逻辑：地址怎么拼、什么消息算「有变更」，以及重连退避的序列。
/// <para>
/// 这三样都是屏上看不出来的 —— 拼错的那一版表现为「一直连不上」，判错的那一版表现为「有新东西却不刷」或者
/// 「没事乱刷」，退避写错的表现是「偶尔卡一下」。所以它们必须被钉在这里。
/// </para>
/// <para>
/// 端点契约照的是 Emby Theater 的 <c>apiclient.js</c>：<c>emby/socket</c> 换成 <c>embywebsocket</c>、
/// <c>http(s)</c> 换 <c>ws(s)</c>、令牌和设备号拼在查询串上。这套东西不在 <c>emby-openapi.json</c> 里
/// （434 条路径没有一条 socket 路径），所以它是从实现里读出来的，不是从文档里猜的。
/// </para>
/// </summary>
internal static class EmbyRealtimeTests
{
    public static void Register()
    {
        Test("实时通道：http 换 ws、路径接在反代前缀后面、令牌和设备号进查询串", () =>
        {
            var url = EmbyRealtimeProtocol.Build(new Uri("http://192.168.31.230:8896"), "TOKEN123", "DEV42");

            Assert.Equal("ws", url.Scheme);
            Assert.Equal(8896, url.Port);
            Assert.Equal("/embywebsocket", url.AbsolutePath);
            Assert.Contains("api_key=TOKEN123", url.Query);
            Assert.Contains("deviceId=DEV42", url.Query);
        });
        Test("实时通道：https 换 wss，反代前缀原样带着", () =>
        {
            var url = EmbyRealtimeProtocol.Build(new Uri("https://nas.invalid/proxy/v"), "t", "d");

            Assert.Equal("wss", url.Scheme);
            Assert.Equal("/proxy/v/embywebsocket", url.AbsolutePath);
        });
        Test("实时通道：Jellyfin 走网页端自己的 /socket，不套 Emby 的 embywebsocket", () =>
        {
            var url = EmbyRealtimeProtocol.Build(new Uri("http://192.168.31.230:8096/"), "TOKEN123", "DEV42",
                MediaServerKind.Jellyfin);
            Assert.Equal("ws", url.Scheme);
            Assert.Equal("/socket", url.AbsolutePath);
            Assert.Contains("api_key=TOKEN123", url.Query);
            Assert.Contains("deviceId=DEV42", url.Query);
        });
        Test("实时通道：基地址带尾斜杠不会拼出双斜杠", () =>
        {
            var url = EmbyRealtimeProtocol.Build(new Uri("http://nas.invalid:8096/"), "t", "d");
            Assert.Equal("/embywebsocket", url.AbsolutePath);
        });
        Test("实时通道：令牌进查询串时被转义，不会切断查询串", () =>
        {
            var url = EmbyRealtimeProtocol.Build(new Uri("http://nas.invalid"), "a&b=c", "d/e");
            Assert.Contains("api_key=a%26b%3Dc", url.Query);
            Assert.Contains("deviceId=d%2Fe", url.Query);
        });
        Test("实时通道：没有令牌或设备号直接拒绝，不连一条「活着但永远沉默」的连接", () =>
        {
            Assert.Throws<ArgumentException>(() => EmbyRealtimeProtocol.Build(new Uri("http://nas.invalid"), "", "d"));
            Assert.Throws<ArgumentException>(() => EmbyRealtimeProtocol.Build(new Uri("http://nas.invalid"), "t", "  "));
        });
        Test("实时通道：不是 http/https 的基地址拒绝", () =>
        {
            Assert.Throws<ArgumentException>(() => EmbyRealtimeProtocol.Build(new Uri("ftp://nas.invalid"), "t", "d"));
        });

        Test("实时通道：只认 LibraryChanged 一种消息", () =>
        {
            Assert.True(EmbyRealtimeProtocol.IsLibraryChanged("LibraryChanged"));
            Assert.False(EmbyRealtimeProtocol.IsLibraryChanged("librarychanged"), "大小写不同不算，服务器发的是这个写法");
            Assert.False(EmbyRealtimeProtocol.IsLibraryChanged("Sessions"), "播放期间每 2 秒一条，认了就是看片时一直重读");
            Assert.False(EmbyRealtimeProtocol.IsLibraryChanged("UserDataChanged"), "观看进度那一路归播放返回那一趟管");
            Assert.False(EmbyRealtimeProtocol.IsLibraryChanged("RefreshProgress"), "扫描进度条不是「有新东西」");
            Assert.False(EmbyRealtimeProtocol.IsLibraryChanged("ActivityLogEntry"));
            Assert.False(EmbyRealtimeProtocol.IsLibraryChanged(null));
            Assert.False(EmbyRealtimeProtocol.IsLibraryChanged(""));
        });

        Test("实时通道：从帧里取 MessageType，字段顺序无关、坏 JSON 不抛异常", () =>
        {
            Assert.Equal("LibraryChanged", EmbyRealtimeProtocol.ReadMessageType(
                """{"MessageType":"LibraryChanged","Data":{"ItemsAdded":["1"]}}"""));
            Assert.Equal("Sessions", EmbyRealtimeProtocol.ReadMessageType(
                """{"Data":[1,2,3],"MessageType":"Sessions"}"""));
            Assert.Null(EmbyRealtimeProtocol.ReadMessageType("{}"));
            Assert.Null(EmbyRealtimeProtocol.ReadMessageType("{ 这不是 JSON"));
            Assert.Null(EmbyRealtimeProtocol.ReadMessageType(""));
            Assert.Null(EmbyRealtimeProtocol.ReadMessageType("""{"MessageType":42}"""));
        });
        Test("实时通道：真机抓下来的那一帧能被认出来", () =>
        {
            // 2026-10-17 真机探针从 果服 4.10.1.0 收到的一条 UserDataChanged 原样。
            const string frame = """
                {"MessageType":"UserDataChanged","MessageId":"341085293d484d32bd1d8ac53d7e47ec","Data":{"UserId":"2f7f0bb5659f4ebd98f4447d270b94bc","UserDataList":[{"PlaybackPositionTicks":0,"PlayCount":1,"IsFavorite":false,"LastPlayedDate":"2026-10-16T16:39:03.0000000Z","Played":false,"ItemId":"8740"}]}}
                """;
            Assert.Equal("UserDataChanged", EmbyRealtimeProtocol.ReadMessageType(frame));
            Assert.False(EmbyRealtimeProtocol.IsLibraryChanged(EmbyRealtimeProtocol.ReadMessageType(frame)));
        });

        Test("实时退避：1、2、4、8、16、30，之后一直是 30", () =>
        {
            var backoff = new EmbyRealtimeBackoff();
            var waits = Enumerable.Range(0, 8).Select(_ => (int)backoff.Next().TotalSeconds).ToArray();

            Assert.Equal("1,2,4,8,16,30,30,30", string.Join(',', waits));
        });
        Test("实时退避：连上过一次就归零，下一次断线从头来", () =>
        {
            var backoff = new EmbyRealtimeBackoff();
            for (var i = 0; i < 5; i++) backoff.Next();
            Assert.Equal(30, (int)backoff.Next().TotalSeconds, "退到上界");

            backoff.Reset();
            Assert.Equal(1, (int)backoff.Next().TotalSeconds);
        });
        Test("实时退避：上界不超过 30 秒，再长不如交给 30 秒那道兜底", () =>
        {
            var backoff = new EmbyRealtimeBackoff();
            for (var i = 0; i < 40; i++)
                Assert.True(backoff.Next().TotalSeconds <= EmbyRealtimeBackoff.MaxSeconds);
        });
    }
}
