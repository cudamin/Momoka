using System.Net;
using System.Text;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class LibraryEpisodeFolderTests
{
    public static void Register()
    {
        Test("电视集：排序菜单逐项匹配参考图", () => Assert.Equal(
            "分辨率,剧集,加入日期,发行日期,媒体容器,家长评分,导演,帧率,年份,影评人评分,播放日期,播放时长,播放次数,文件名,文件尺寸,标题,比特率,社区评分,视频编解码器,随机",
            Labels(LibrarySection.Episodes)));
        Test("电视文件夹：排序菜单逐项匹配参考图", () => Assert.Equal(
            "上次添加内容日期,专辑,专辑艺人,作曲家,分辨率,加入日期,发行日期,媒体容器,家长评分,导演,帧率,年份,影评人评分,播放日期,播放时长,播放次数,文件名,文件尺寸,标题,比特率,社区评分,编号,艺人,视频编解码器,随机",
            Labels(LibrarySection.Folders)));
        Test("电视分类：默认排序分别是剧集和文件名", () =>
        {
            Assert.Equal(LibrarySortCatalog.EpisodeOrder, LibrarySortCatalog.Default("tvshows", LibrarySection.Episodes).Value);
            Assert.Equal(LibrarySortCatalog.Filename, LibrarySortCatalog.Default("tvshows", LibrarySection.Folders).Value);
            Assert.False(LibrarySortCatalog.Default("tvshows", LibrarySection.Folders).DescendingByDefault);
        });
        Test("电视集：筛选行与分组逐项匹配参考图", () => Assert.Equal(
            "播放状态,收藏,重制,类型,工作室,年份,媒体容器,视频编解码器,视频类型,分辨率,3D,音频编解码器,音频布局,音频语言,字幕,字幕编解码器,字幕语言,预告片,附加篇,主题曲,主题视频,已锁定,概要,IMDb Id,Tvdb Id,MovieDb Id",
            string.Join(',', EmbyFilterBy.For(EmbySortPreset.Episode, new Version(4, 10, 1)).Select(field => field.Label))));
        Test("电视分类：默认海报且字段和分组互不混用", () =>
        {
            var episode = LibraryDisplayCatalog.DefaultFor(LibrarySection.Episodes);
            var folder = LibraryDisplayCatalog.DefaultFor(LibrarySection.Folders);
            Assert.Equal(LibraryView.Poster, episode.View);
            Assert.Equal("Title", string.Join(',', episode.Fields));
            Assert.Equal("Title,ProductionYear", string.Join(',', folder.Fields));
            Assert.False(episode.GroupIntoCollections);
            Assert.False(folder.GroupIntoCollections);
            Assert.Equal(169d, LibraryCardMetrics.Height(LibraryView.Poster, 300, LibrarySection.Episodes));
            Assert.Equal(255d, LibraryCardMetrics.Height(LibraryView.Poster, 170, LibrarySection.Folders));
        });
        Test("电视集：标题两行及隐藏标题不回退旧文案", () =>
        {
            var item = new EmbyItem { Name = "地龙", Type = EmbyItemType.Episode, SeriesName = "异世界料理", ParentIndexNumber = 2, IndexNumber = 4 };
            Assert.Equal("异世界料理", LibraryCardText.Title(item, [LibraryField.Title], LibrarySection.Episodes));
            Assert.Equal("S2:E4 - 地龙", LibraryCardText.Subtitle(item, [LibraryField.Title], LibrarySection.Episodes));
            Assert.Equal("", LibraryCardText.Title(item, [], LibrarySection.Episodes));
            Assert.Equal("", LibraryCardText.Subtitle(item, [], LibrarySection.Episodes));
        });
        Test("电视集：媒体筛选克隆清空及序列化不会漏值", () =>
        {
            var filters = new ItemFilters();
            filters.SetValue(EmbyFilterBy.Keys.VideoCodecs, "hevc", true);
            filters.SetValue(EmbyFilterBy.Keys.VideoCodecs, "h264", true);
            var clone = filters.Clone();
            clone.Clear();
            Assert.Equal(2, filters.Count);
            Assert.True(clone.IsBlank);
            var back = System.Text.Json.JsonSerializer.Deserialize<ItemFilters>(System.Text.Json.JsonSerializer.Serialize(filters))!;
            Assert.Equal("hevc,h264", string.Join(',', back.Values(EmbyFilterBy.Keys.VideoCodecs)));
            Assert.Contains("VideoCodecs=hevc%2Ch264", new ItemQuery { Filters = back }.ToQueryString());
        });
        Test("电视集：分辨率按网页端宽度范围提交并互斥", () =>
        {
            var filters = new ItemFilters();
            var field = EmbyFilterBy.Fields.Single(field => field.Id == "resolution");
            foreach (var (label, minimum, maximum) in new[] { ("4K", "3800", ""), ("1080p", "1800", "2200"), ("720p", "1200", "1799"), ("SD", "", "1199") })
            {
                filters.Select(field, field.Choices.Single(choice => choice.Label == label));
                Assert.Equal(1, filters.Count);
                var query = new ItemQuery { Filters = filters }.ToParameters().ToDictionary(pair => pair.Key, pair => pair.Value);
                Assert.Equal(minimum, query.GetValueOrDefault("MinWidth") ?? "");
                Assert.Equal(maximum, query.GetValueOrDefault("MaxWidth") ?? "");
            }
            filters.Select(field, field.Choices[0]);
            Assert.True(filters.IsBlank);
        });
        Test("电视集：动态媒体选项只读限定本库且语言分流", () => RequestsAsync().GetAwaiter().GetResult());
    }

    private static string Labels(LibrarySection section) => string.Join(',',
        LibrarySortCatalog.For("tvshows", section, new Version(4, 10, 1)).Select(option => option.Label));

    private static async Task RequestsAsync()
    {
        using var handler = new Transport();
        using var http = new EmbyHttp(handler);
        var client = new EmbyClient(http, new EmbyConnection(new Uri("https://fixture.invalid/emby/"), "fixture-token", "user", "User", "Fixture", DeviceIdentity.Create("filter-fixture", "1")));
        foreach (var key in EmbyFilterBy.MediaListKeys)
        {
            var values = await client.GetFilterValuesAsync(key, new ItemQuery { ParentId = "library-a", IncludeItemTypes = [EmbyItemType.Episode], Filters = new ItemFilters { Genres = ["动画"] } }, default);
            Assert.Equal("hevc", values.Single());
            var uri = handler.Request!;
            Assert.Contains("ParentId=library-a", uri.Query);
            Assert.Contains("IncludeItemTypes=Episode", uri.Query);
            Assert.DoesNotContain("Genres=", uri.Query);
            Assert.DoesNotContain("fixture-token", uri.AbsoluteUri);
            var languages = key is EmbyFilterBy.Keys.AudioLanguages or EmbyFilterBy.Keys.SubtitleLanguages;
            Assert.Equal("/emby/" + (languages ? "StreamLanguages" : key), uri.AbsolutePath);
            if (languages) Assert.Contains("StreamType=" + (key == EmbyFilterBy.Keys.AudioLanguages ? "Audio" : "Subtitle"), uri.Query);
        }
    }

    private sealed class Transport : HttpMessageHandler
    {
        public Uri? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Request = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"Items\":[{\"Name\":\"hevc\"}],\"TotalRecordCount\":1}", Encoding.UTF8, "application/json") });
        }
    }
}
