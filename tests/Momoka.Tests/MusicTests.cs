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
        Test("音乐播放栏：保留单行布局、轻薄亚克力与原生按钮状态", () =>
        {
            var root = FindRoot();
            var path = Path.Combine(root, "src", "Momoka.Shell", "Views", "MusicPlayerBar.xaml");
            Assert.True(File.Exists(path), "音乐播放栏尚未实现");
            var document = XDocument.Load(path);
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var names = document.Descendants().Select(element => (string?)element.Attribute(x + "Name")).ToHashSet();
            foreach (var name in new[] { "PreviousButton", "PauseButton", "StopButton", "NextButton", "QueueButton", "VolumeSlider", "SeekSlider", "CoverButton", "TitleButton" })
                Assert.True(names.Contains(name), name);
            XElement Named(string name) => document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == name);
            foreach (var name in new[] { "CoverButton", "TitleButton" })
            {
                Assert.Equal("Button", Named(name).Name.LocalName);
                Assert.Equal("OnExpand", (string?)Named(name).Attribute("Click"));
            }
            Assert.Equal("Button", Named("CoverFrame").Parent!.Name.LocalName);
            var surface = Named("Surface");
            Assert.Equal("Center", (string?)surface.Attribute("HorizontalAlignment"));
            Assert.True(surface.Attribute("BorderBrush") is null && surface.Attribute("BorderThickness") is null, "播放栏不应叠加卡片描边");
            Assert.Equal("{ThemeResource EgActionGlassBrush}", (string?)Named("SurfaceGlass").Attribute("Background"));
            var grid = Named("BarGrid");
            Assert.True(new[] { "Auto", "Auto", "*", "Auto" }.SequenceEqual(grid.Elements()
                .Single(element => element.Name.LocalName == "Grid.ColumnDefinitions").Elements()
                .Select(element => (string)element.Attribute("Width")!)), "原布局列宽被改变");
            Assert.True(new[] { "PreviousButton", "PauseButton", "StopButton", "NextButton" }.SequenceEqual(Named("Transport")
                .Elements().Select(element => (string)element.Attribute(x + "Name")!)), "主播放按钮顺序被改变");
            Assert.True(new[] { "ShuffleButton", "RepeatButton", "FavoriteButton", "QueueButton", "VolumeButton" }.SequenceEqual(Named("Extras")
                .Elements().Select(element => (string)element.Attribute(x + "Name")!)), "辅助按钮顺序被改变");
            Assert.Equal("{StaticResource MusicButtonStyle}", (string?)Named("PauseButton").Attribute("Style"));
            Assert.Equal("ToggleButton", Named("ShuffleButton").Name.LocalName);
            Assert.Equal("{x:Bind ViewModel.Shuffle, Mode=OneWay}", (string?)Named("ShuffleButton").Attribute("IsChecked"));
            Assert.False(document.Descendants().Any(element => element.Name.LocalName == "CommandBar"), "本批不应重排命令位置");
            Assert.Equal("42", (string?)Named("CoverFrame").Attribute("Width"));
            Assert.Equal("{StaticResource EgSmallArtworkCornerRadius}", (string?)Named("CoverFrame").Attribute("CornerRadius"));
            Assert.Equal("2", (string?)Named("TrackInfo").Attribute("Grid.Column"));
            Assert.Equal("360", (string?)Named("TrackInfo").Attribute("MaxWidth"));
            Assert.Equal("360", (string?)Named("SeekSlider").Attribute("MaxWidth"));
            Assert.Equal("Left", (string?)Named("SeekSlider").Attribute("HorizontalAlignment"));
            foreach (var name in new[] { "ShuffleButton", "RepeatButton", "FavoriteButton" })
                Assert.Equal("PathIcon", Named(name).Elements().Single().Name.LocalName);
            var musicPage = XDocument.Load(Path.Combine(root, "src", "Momoka.Shell", "Views", "MusicLibraryPage.xaml"));
            Assert.False(musicPage.Descendants().Any(element => (string?)element.Attribute("Text") == "音乐库"), "音乐页不再重复一行眉标");
            var title = musicPage.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "MusicTitle");
            Assert.Equal("{StaticResource EgPageTitleStyle}", (string?)title.Attribute("Style"));
            Assert.Equal("Grid", title.Parent!.Parent!.Name.LocalName);
            var code = File.ReadAllText(Path.ChangeExtension(path, ".xaml.cs"));
            Assert.False(code.Contains("ShuffleButton.Opacity", StringComparison.Ordinal), "随机应使用原生选中状态");
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
        Test("音乐播放页：封面/歌名入口、整页控件与歌词状态接线", () =>
        {
            var root = FindRoot();
            var pagePath = Path.Combine(root, "src", "Momoka.Shell", "Views", "MusicPlayerPage.xaml");
            Assert.True(File.Exists(pagePath), "音乐播放页尚未实现");
            var page = XDocument.Load(pagePath);
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var names = page.Descendants().Select(element => (string?)element.Attribute(x + "Name")).ToHashSet();
            foreach (var name in new[]
            {
                "CollapseButton", "FullscreenButton", "CoverFrame", "SeekSlider", "PlayButton", "PreviousButton",
                "NextButton", "ShuffleButton", "FavoriteButton", "VolumeButton", "QueueButton", "LyricsList"
            })
                Assert.True(names.Contains(name), name);

            var bar = XDocument.Load(Path.Combine(Path.GetDirectoryName(pagePath)!, "MusicPlayerBar.xaml"));
            foreach (var view in new[] { page, bar })
            {
                var seek = view.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "SeekSlider");
                var thumb = seek.Descendants().Single(element => (string?)element.Attribute(x + "Key") == "SliderThumbCornerRadius");
                Assert.Equal("StaticResource", thumb.Name.LocalName);
                Assert.Equal("EgMusicSliderThumbCornerRadius", (string?)thumb.Attribute("ResourceKey"));
            }
            var cover = page.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "CoverFrame");
            Assert.Equal("{StaticResource EgLargeArtworkCornerRadius}", (string?)cover.Attribute("CornerRadius"));

            var userControl = page.Root!;
            Assert.Equal("Dark", (string?)userControl.Attribute("RequestedTheme"));
            Assert.True(page.Descendants().Any(element => element.Name.LocalName == "KeyboardAccelerator"
                && (string?)element.Attribute("Key") == "Escape"), "播放页没有 Escape 收起");

            // 封面与氛围图必须各自绑到播放页的大图/小图，不能拿播放栏那张 48 的卡冒充。
            var artwork = page.Descendants()
                .Where(element => element.Name.LocalName == "Image")
                .Select(element => (string?)element.Attribute(XName.Get("CardArtwork.Item", "using:Momoka.Shell.Views")))
                .Where(value => value is not null).ToArray();
            Assert.True(artwork.Any(value => value!.Contains("CoverArtwork", StringComparison.Ordinal)), "缺少大封面接线");
            Assert.True(artwork.Any(value => value!.Contains("AmbientArtwork", StringComparison.Ordinal)), "缺少氛围底图接线");
            Assert.True(page.Descendants().Any(element => element.Name.LocalName == "ListView"
                && (string?)element.Attribute("ItemsSource") == "{x:Bind ViewModel.Lyrics, Mode=OneWay}"), "歌词列表没有接线");

            var code = File.ReadAllText(Path.ChangeExtension(pagePath, ".xaml.cs"));
            Assert.True(code.Contains("CollapseRequested", StringComparison.Ordinal), "播放页没有收起事件");
            Assert.True(code.Contains("BeginScrub", StringComparison.Ordinal), "播放页没有拖动进度");
            Assert.True(code.Contains("ScrollIntoView", StringComparison.Ordinal), "歌词没有随播放滚动");
            Assert.True(code.Contains("Fullscreen = !_window.Fullscreen", StringComparison.Ordinal), "全屏键没有接窗口");
            Assert.True(code.Contains("Environment.TickCount64", StringComparison.Ordinal), "用户滚动后没有暂停自动跟随");

            var shell = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "ShellPage.xaml"));
            Assert.True(shell.Contains("<local:MusicPlayerPage x:Name=\"MusicPlayer\"", StringComparison.Ordinal), "外壳没有挂播放页");
            var shellMusic = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "ShellPage.Music.cs"));
            Assert.True(shellMusic.Contains("ExpandRequested += OpenMusicPage", StringComparison.Ordinal), "播放栏入口没有接线");
            Assert.True(shellMusic.Contains("CloseMusicPageCore(focusBar: false)", StringComparison.Ordinal), "导航时没有收起播放页");
            Assert.True(shellMusic.Contains("Player.Visibility != Visibility.Visible", StringComparison.Ordinal),
                "收起播放页时没有给视频播放让位（内容区会在 mpv 子窗口上重画）");
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
