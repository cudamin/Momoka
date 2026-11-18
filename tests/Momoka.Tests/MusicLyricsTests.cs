using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Music;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MusicLyricsTests
{
    public static void Register()
    {
        Test("歌词解析：LRC 时间轴、一句多时间戳与头部标签", () =>
        {
            var lyrics = MusicLyrics.Parse(
                "[ar:Queen]\n[ti:Under Pressure]\n[offset:0]\n" +
                "[00:01.50]第一句\n[00:04.00][00:12.25]副歌\n[00:08.00]\n");
            Assert.True(lyrics.Synced);
            Assert.Equal(3, lyrics.Lines.Count);
            Assert.Equal(TimeSpan.FromSeconds(1.5), lyrics.Lines[0].Time);
            Assert.Equal("第一句", lyrics.Lines[0].Text);
            Assert.Equal(TimeSpan.FromSeconds(4), lyrics.Lines[1].Time);
            Assert.Equal(TimeSpan.FromSeconds(12.25), lyrics.Lines[2].Time);
            Assert.Equal("副歌", lyrics.Lines[2].Text);
        });

        Test("歌词解析：纯文本按原文顺序，不当时轴", () =>
        {
            var lyrics = MusicLyrics.Parse("第一行\r\n\r\n第二行\n第三行");
            Assert.False(lyrics.Synced);
            Assert.Equal(3, lyrics.Lines.Count);
            Assert.Equal("第一行", lyrics.Lines[0].Text);
            Assert.Equal("第三行", lyrics.Lines[2].Text);
            Assert.True(lyrics.Lines.All(line => line.Time == TimeSpan.Zero));
        });

        Test("歌词解析：空白、只有标签与 BOM 都得到空结果", () =>
        {
            Assert.True(MusicLyrics.Parse(null).IsEmpty);
            Assert.True(MusicLyrics.Parse("  \r\n\t").IsEmpty);
            Assert.True(MusicLyrics.Parse("[ar:歌手]\n[ti:歌名]\n").IsEmpty);
            Assert.True(MusicLyrics.Parse("\uFEFF").IsEmpty);
        });

        Test("歌词解析：offset 平移与时间轴排序", () =>
        {
            var lyrics = MusicLyrics.Parse("[offset:+500]\n[00:05.00]后\n[00:01.00]前");
            Assert.True(lyrics.Synced);
            Assert.Equal("前", lyrics.Lines[0].Text);
            Assert.Equal(TimeSpan.FromSeconds(1.5), lyrics.Lines[0].Time);
            Assert.Equal(TimeSpan.FromSeconds(5.5), lyrics.Lines[1].Time);
        });

        Test("歌词解析：服务端偏移叠加在原文 offset 上，负时间夹到零", () =>
        {
            var lyrics = MusicLyrics.Parse("[offset:+500]\n[00:01.00]一句", TimeSpan.FromMilliseconds(-250));
            Assert.Equal(TimeSpan.FromSeconds(1.25), lyrics.Lines[0].Time);
            var shifted = MusicLyrics.Parse("[00:01.00]一句", TimeSpan.FromMilliseconds(-2000));
            Assert.Equal(TimeSpan.Zero, shifted.Lines[0].Time);
        });

        Test("歌词定位：二分找最后一句已到的行", () =>
        {
            var lyrics = MusicLyrics.Parse("[00:01.00]一\n[00:05.00]二\n[00:09.00]三");
            Assert.Equal(-1, lyrics.ActiveIndex(0.9));
            Assert.Equal(0, lyrics.ActiveIndex(1.0));
            Assert.Equal(0, lyrics.ActiveIndex(4.99));
            Assert.Equal(1, lyrics.ActiveIndex(5.0));
            Assert.Equal(2, lyrics.ActiveIndex(60));
            Assert.Equal(-1, MusicLyrics.Parse("纯文本").ActiveIndex(60));
        });

        Test("歌词标签：从 display-tags 里挑大小写不敏感的歌词键", () =>
        {
            Assert.Equal("LYRICS", MusicLyrics.FindTag(["Title", "LYRICS", "Artist"]));
            Assert.Equal("UnsynchronizedLyrics", MusicLyrics.FindTag(["Title", "UnsynchronizedLyrics"]));
            Assert.Null(MusicLyrics.FindTag(["Title", "Artist"]));
            Assert.Null(MusicLyrics.FindTag(null));
        });

        Test("歌词行：正在唱的那一句换不透明度并发通知", () =>
        {
            var line = new MusicLyricLine(TimeSpan.Zero, "一句");
            var raised = 0;
            line.PropertyChanged += (_, _) => raised++;
            Assert.Equal(0.38, line.ActiveOpacity);
            line.IsActive = true;
            Assert.Equal(1, line.ActiveOpacity);
            Assert.Equal(2, raised);
            line.IsActive = true;
            Assert.Equal(2, raised);
        });

        Test("播放页格式角标：容器优先、编码兜底、都没有就收起", () =>
        {
            var withContainer = new EmbyItem
            {
                Id = "a",
                Name = "歌",
                Type = EmbyItemType.Audio,
                MediaSources = [new MediaSource { Container = "flac" }]
            };
            Assert.Equal("FLAC", MusicCatalog.FormatLabel(withContainer));

            var withCodec = new EmbyItem
            {
                Id = "b",
                Name = "歌",
                Type = EmbyItemType.Audio,
                MediaSources = [new MediaSource { MediaStreams = [new MediaStream { Type = "Audio", Codec = "aac" }] }]
            };
            Assert.Equal("AAC", MusicCatalog.FormatLabel(withCodec));

            Assert.Equal("", MusicCatalog.FormatLabel(new EmbyItem { Id = "c", Name = "歌", Type = EmbyItemType.Audio }));
        });

        Test("播放页布局：两栏断点、封面上限与窄窗单栏", () =>
        {
            Assert.True(MusicPlayerLayout.ShowsLyrics(1024, 552));
            Assert.False(MusicPlayerLayout.ShowsLyrics(760, 552));
            Assert.False(MusicPlayerLayout.ShowsLyrics(1024, 300));
            // 1024×740 的窗口：封面取参考图那一档 320，控制排与歌词都在。
            Assert.Equal(320, MusicPlayerLayout.CoverSize(1024, 552));
            Assert.Equal(MusicPlayerLayout.MaxCover, MusicPlayerLayout.CoverSize(2560, 1812));
            // 窄窗先让位：360 宽、400 高的内容区里封面缩到 190，整摞仍装得下。
            Assert.Equal(190, MusicPlayerLayout.CoverSize(360, 400, lyrics: false));
            Assert.Equal(420, MusicPlayerLayout.LyricsWidth(1024));
            Assert.Equal(MusicPlayerLayout.LyricsMaxWidth, MusicPlayerLayout.LyricsWidth(1920));
            Assert.Equal(64, MusicPlayerLayout.Gap(1920));
            Assert.Equal(48, MusicPlayerLayout.Gap(860));
            Assert.Equal(552, MusicPlayerLayout.StageHeight(740));
            Assert.Equal("1:02", TimeFormat.Clock(TimeSpan.FromSeconds(62)));
        });
    }
}
