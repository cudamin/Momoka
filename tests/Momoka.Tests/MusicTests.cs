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
