using Momoka.Emby;
using Momoka.FnNas;
using Momoka.FnNasSubtitleTests;
using Momoka.Tests;

Console.OutputEncoding = System.Text.Encoding.UTF8;
TestHarness.Test("飞牛字幕协议提供原生搜索入口", () =>
    Assert.NotNull(typeof(FnNasClient).GetMethod("SearchSubtitlesAsync")));
TestHarness.Test("飞牛字幕固定来源服务存在", () =>
    Assert.NotNull(typeof(FnNasClient).Assembly.GetType("Momoka.FnNas.FnNasSubtitles")));
TestHarness.Test("飞牛普通登录账号的字幕管理可达", () =>
{
    var item = new EmbyItem { Id = "movie", Type = EmbyItemType.Movie };
    Assert.True(ItemMenu.For(item, fnNas: true, permissions: new(IsAdministrator: false))
        .Any(row => row.Command == ItemCommand.Subtitles));
});
FnNasSubtitleCases.Register();
SubtitleTransportTests.Register();
DialogChromeTests.Register();
StartupArgsTests.Register();
return TestHarness.Run();
