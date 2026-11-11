using System.Xml.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Emby;
using Momoka.Music;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MusicTests
{
    public static void Register()
    {
        Test("音乐查询：艺术家、专辑顺序、分页与视频库隔离", () =>
        {
            var artist = MusicCatalog.Query("music", "fnmusic:artist:artist", EmbyItemType.MusicArtist, MusicSection.Tracks, 80, null);
            Assert.Equal("artist", artist.ArtistIds.Single());
            Assert.Equal(80, artist.StartIndex);
            Assert.Equal(80, artist.Limit);
            Assert.Equal(EmbyItemType.Audio, artist.IncludeItemTypes.Single());
            var album = MusicCatalog.Query("root", "album", EmbyItemType.MusicAlbum, MusicSection.Tracks, 0, "");
            Assert.Equal(EmbySortBy.Number, album.SortBy);
            Assert.Equal("", MusicCatalog.Query("root", "list", EmbyItemType.Playlist, MusicSection.Tracks, 0, null).SortBy);
            Assert.False(MusicCatalog.IsAudio(EmbyItemType.MusicVideo));
        });
        Test("音乐协议：艺术家目录和歌单保持专用路由与账号", () =>
        {
            using var handler = new MusicTransport();
            using var http = new EmbyHttp(handler);
            var client = new EmbyClient(http, new EmbyConnection(new Uri("https://music.invalid/emby/"), "fixture", "listener", "听众", "音乐", DeviceIdentity.Create("music-tests", "test")));
            client.GetItemsAsync(MusicCatalog.Query("root", null, null, MusicSection.Artists, 0, null), default).GetAwaiter().GetResult();
            Assert.Equal("/emby/Artists", handler.Last!.AbsolutePath);
            Assert.Contains("UserId=listener", handler.Last.Query);
            client.GetItemsAsync(MusicCatalog.Query("root", "artist", EmbyItemType.MusicArtist, MusicSection.Tracks, 0, null), default).GetAwaiter().GetResult();
            Assert.Contains("ArtistIds=artist", handler.Last.Query);
            Assert.False(handler.Last.Query.Contains("ParentId=artist", StringComparison.Ordinal));
            client.GetItemsAsync(MusicCatalog.Query("root", "playlist", EmbyItemType.Playlist, MusicSection.Tracks, 0, null), default).GetAwaiter().GetResult();
            Assert.Equal("/emby/Playlists/playlist/Items", handler.Last.AbsolutePath);
        });
        Test("音乐队列：重复歌曲按槽位前进，末尾停止", () =>
        {
            var queue = new MusicQueue();
            queue.Replace([Track("a"), Track("b"), Track("a")], 2);
            Assert.Equal(2, queue.Index);
            Assert.Equal(-1, queue.Next(automatic: true));
            Assert.Equal(1, queue.Previous());
            Assert.Equal(0, queue.Previous());
        });
        Test("音乐队列：单曲循环只拦自动前进，列表循环回到开头", () =>
        {
            var queue = new MusicQueue();
            queue.Replace([Track("a"), Track("b")], 0);
            queue.Repeat = MusicRepeat.One;
            Assert.Equal(0, queue.Next(automatic: true));
            Assert.Equal(1, queue.Next(automatic: false));
            queue.Repeat = MusicRepeat.All;
            Assert.Equal(0, queue.Next(automatic: true));
        });
        Test("音乐队列：随机不连续重复，上一曲返回实际播放顺序", () =>
        {
            var queue = new MusicQueue();
            queue.Replace([Track("a"), Track("b"), Track("c")], 0);
            queue.Shuffle = true;
            var next = queue.Next(automatic: false);
            Assert.True(next is 1 or 2);
            Assert.Equal(0, queue.Previous());
            queue.Clear();
            Assert.Equal(-1, queue.Next(automatic: true));
            Assert.Equal(0, queue.Items.Count);
        });
        Test("音乐播放栏：亚克力材质、停止及键盘定位入口", () =>
        {
            var root = FindRoot();
            var path = Path.Combine(root, "src", "Momoka.Shell", "Views", "MusicPlayerBar.xaml");
            Assert.True(File.Exists(path), "音乐播放栏尚未实现");
            var document = XDocument.Load(path);
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var names = document.Descendants().Select(element => (string?)element.Attribute(x + "Name")).ToHashSet();
            foreach (var name in new[] { "PreviousButton", "PauseButton", "StopButton", "NextButton", "QueueButton", "VolumeSlider", "SeekSlider" })
                Assert.True(names.Contains(name), name);
            Assert.True(document.Descendants().Any(element => ((string?)element.Attribute("Background"))?.Contains("EgFrostBrush", StringComparison.Ordinal) == true));
        });
        Test("音乐界面：分区决定曲目表与封面墙，读数与第二行各有说法", () =>
        {
            Assert.True(MusicCatalog.UsesCoverWall(MusicSection.Albums));
            Assert.True(MusicCatalog.UsesCoverWall(MusicSection.Artists));
            Assert.True(MusicCatalog.UsesCoverWall(MusicSection.Playlists));
            Assert.False(MusicCatalog.UsesCoverWall(MusicSection.Tracks));
            Assert.False(MusicCatalog.UsesCoverWall(MusicSection.Favorites));
            Assert.Equal("492 首", MusicCatalog.CountLabel(MusicSection.Tracks, 492));
            Assert.Equal("12 张", MusicCatalog.CountLabel(MusicSection.Albums, 12));
            Assert.Equal("3 位", MusicCatalog.CountLabel(MusicSection.Artists, 3));
            Assert.Equal("5 个", MusicCatalog.CountLabel(MusicSection.Playlists, 5));
            Assert.Equal(MusicCatalog.WallTileWidth + Momoka.Infrastructure.CardSize.Chrome, MusicCatalog.WallTileHeight);

            var track = new EmbyItem { Id = "t", Name = "红豆", Type = EmbyItemType.Audio, Artists = ["王菲"], Album = "匆匆那年" };
            Assert.Equal("王菲 · 匆匆那年", MusicCatalog.RowSubtitle(track, insideAlbum: false));
            Assert.Equal("王菲", MusicCatalog.RowSubtitle(track, insideAlbum: true));

            var album = new EmbyItem
            {
                Id = "a",
                Name = "匆匆那年",
                Type = EmbyItemType.MusicAlbum,
                AlbumArtists = [new EmbyNamedId { Id = "x", Name = "王菲" }]
            };
            Assert.Equal("王菲", MusicCatalog.WallSubtitle(album));
            // 艺术家与歌单的第二行留空：ChildCount 在各家协议里含义不同，不拿它编一个数字。
            Assert.Equal("", MusicCatalog.WallSubtitle(new EmbyItem
            { Id = "p", Name = "王菲", Type = EmbyItemType.MusicArtist, ChildCount = 9 }));
            Assert.Equal("", MusicCatalog.WallSubtitle(new EmbyItem
            { Id = "l", Name = "夜路", Type = EmbyItemType.Playlist, ChildCount = 9 }));
        });
        Test("音乐界面：曲目表与封面墙两个模板，分区不再用单选圆点", () =>
        {
            var path = Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "MusicLibraryPage.xaml");
            var document = XDocument.Load(path);
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var keys = document.Descendants().Select(element => (string?)element.Attribute(x + "Key"))
                .Where(key => key is not null).ToHashSet();
            Assert.True(keys.Contains("MusicTrackTemplate"), "缺少曲目表模板");
            Assert.True(keys.Contains("MusicTileTemplate"), "缺少封面墙模板");
            Assert.True(document.Descendants().Any(element => element.Name.LocalName == "UniformGridLayout"),
                "封面墙没有换行网格");
            Assert.False(document.Descendants().Any(element => element.Name.LocalName == "RadioButton"),
                "分区仍在使用单选圆点");
        });
    }

    private static EmbyItem Track(string id) => new() { Id = id, Name = id, Type = EmbyItemType.Audio };

    private sealed class MusicTransport : HttpMessageHandler
    {
        public Uri? Last { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(new ItemsResult()), Encoding.UTF8, "application/json") });
        }
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("找不到当前工作树");
    }
}
