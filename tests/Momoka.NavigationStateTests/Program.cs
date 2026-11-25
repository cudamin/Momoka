using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("源链接音乐页面与Shell导航；Frame/ViewModel是边界，无窗口、登录或媒体请求");
        foreach (var mode in new[] { NavigationMode.New, NavigationMode.Back, NavigationMode.Forward })
        {
            Test("音乐根页实际导航恢复目的地/" + mode, () =>
            {
                using var fixture = new Fixture(); var page = new MusicLibraryPage();
                var request = fixture.MusicRequest("library:music-root");
                page.VisitForTest(request, mode);
                Assert.Equal(request.Tag, page.Tag as string); Assert.True(ReferenceEquals(request, page.RequestForTest));
                Assert.Equal(1, page.ViewModel.Attaches); Assert.Equal(1, page.ViewModel.Reloads); Assert.False(page.ReleasedForTest);
            });
        }
        Test("音乐Release保留历史Tag，重入容器清掉旧根页Tag", () =>
        {
            using var fixture = new Fixture(); var page = new MusicLibraryPage();
            page.VisitForTest(fixture.MusicRequest("library:music-root"));
            page.LeaveForTest(); Assert.True(page.ReleasedForTest); Assert.Null(page.RequestForTest);
            Assert.Equal("library:music-root", page.Tag as string);
            page.VisitForTest(fixture.MusicRequest(""), NavigationMode.Forward);
            Assert.Equal("", page.Tag as string); Assert.False(page.ReleasedForTest);
        });
        Test("音乐导航离页每次结束墙与曲目图片需求，返回历史重新附加不重建", () =>
        {
            using var fixture = new Fixture();
            var shell = fixture.Shell;
            var request = fixture.MusicRequest("library:music-root");
            shell.AddLibraryForTest(request);
            shell.GoTo(request.Tag);
            var page = (MusicLibraryPage)shell.ContentFrame.Content!;
            Assert.Equal(0, page.ArtworkDetachesForTest);
            Assert.Equal(0, page.TrackArtworkStopsForTest);
            shell.GoTo("home");
            Assert.Equal(1, page.ArtworkDetachesForTest);
            Assert.Equal(1, page.TrackArtworkStopsForTest);
            Assert.Equal(1, page.ViewModel.Releases);
            shell.BackForTest();
            Assert.True(ReferenceEquals(page, shell.ContentFrame.Content));
            Assert.False(page.ReleasedForTest);
            Assert.Equal(request.Tag, page.Tag as string);
            shell.GoTo("home");
            Assert.Equal(2, page.ArtworkDetachesForTest);
            Assert.Equal(2, page.TrackArtworkStopsForTest);
            Assert.Equal(2, page.ViewModel.Releases);
        });
        Test("无有效导航参数不覆盖旧目的地/music", () =>
        {
            using var fixture = new Fixture(); Page page = new MusicLibraryPage();
            page.Tag = "existing"; page.VisitForTest(null); Assert.Equal("existing", page.Tag as string);
        });
        Test("音乐根页真实Shell返回后再次GoTo不重建", () =>
        {
            using var fixture = new Fixture(); var shell = fixture.Shell;
            var request = fixture.MusicRequest("library:music-root"); shell.AddLibraryForTest(request);
            shell.GoTo(request.Tag); var music = (MusicLibraryPage)shell.ContentFrame.Content!;
            shell.LeaveForHomeForTest(); shell.BackForTest();
            Assert.Equal(request.Tag, shell.CurrentForTest);
            var navigations = shell.ContentFrame.Navigations; var backs = shell.ContentFrame.BackStack.Count;
            var forwards = shell.ContentFrame.ForwardStack.Count; var reloads = music.ViewModel.Reloads;
            shell.GoTo(request.Tag);
            Assert.True(ReferenceEquals(music, shell.ContentFrame.Content));
            Assert.Equal(navigations, shell.ContentFrame.Navigations); Assert.Equal(backs, shell.ContentFrame.BackStack.Count);
            Assert.Equal(forwards, shell.ContentFrame.ForwardStack.Count); Assert.Equal(reloads, music.ViewModel.Reloads);
        });
        Test("音乐根页前进恢复后再次GoTo不重建", () =>
        {
            using var fixture = new Fixture(); var shell = fixture.Shell;
            var request = fixture.MusicRequest("library:music-root"); shell.AddLibraryForTest(request);
            shell.GoTo("home"); shell.GoTo(request.Tag); var music = shell.ContentFrame.Content;
            shell.BackForTest(); shell.ForwardForTest(); var count = shell.ContentFrame.Navigations;
            Assert.Equal(request.Tag, shell.CurrentForTest); shell.GoTo(request.Tag);
            Assert.Equal(count, shell.ContentFrame.Navigations); Assert.True(ReferenceEquals(music, shell.ContentFrame.Content));
        });
        Test("音乐容器真实Shell入口仍下钻，返回/前进不冒充根页", () =>
        {
            using var fixture = new Fixture(); var shell = fixture.Shell;
            var request = fixture.MusicRequest("library:music-root"); shell.AddLibraryForTest(request);
            shell.GoTo(request.Tag); var rootPage = shell.ContentFrame.Content;
            shell.OpenMusicContainer(new EmbyItem { Id = "album", Name = "专辑", Type = EmbyItemType.MusicAlbum }, "music-root");
            var childPage = shell.ContentFrame.Content; shell.BackForTest();
            Assert.Equal(request.Tag, shell.CurrentForTest); Assert.True(ReferenceEquals(rootPage, shell.ContentFrame.Content));
            shell.ForwardForTest(); Assert.Null(shell.CurrentForTest); Assert.True(ReferenceEquals(childPage, shell.ContentFrame.Content));
            var count = shell.ContentFrame.Navigations; shell.GoTo(request.Tag);
            Assert.Equal(count + 1, shell.ContentFrame.Navigations);
            Assert.Equal(request.Tag, shell.CurrentForTest);
        });
        Test("Shell实际AdoptContentTag仅接受非空字符串Tag", () =>
        {
            using var fixture = new Fixture(); var shell = fixture.Shell;
            shell.GoTo("home");
            foreach (var tag in new object?[] { null, "", 3 })
            { shell.ContentFrame.Content!.Tag = tag; shell.AdoptForTest(); Assert.Null(shell.CurrentForTest); }
        });
        return TestHarness.Run();
    }

    private sealed class Fixture : IServiceProvider, IDisposable
    {
        private readonly SettingsStore _store;
        private readonly EmbySession _session;
        private readonly EmbyImageStore _images;
        private readonly ActionsBoundary _actions = new();
        internal ShellPage Shell { get; }
        internal Fixture()
        {
            var path = new AppPaths(Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N")));
            var settings = new AppSettings(); settings.Servers.Clear();
            _store = new SettingsStore(path, PassthroughSecretProtector.Instance);
            var constructor = typeof(EmbySession).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                [typeof(AppSettings), typeof(SettingsStore), typeof(CredentialVault), typeof(DeviceIdentity), typeof(HttpMessageHandler)], null)
                ?? throw new InvalidOperationException("Missing anonymous HTTP session boundary");
            _session = (EmbySession)constructor.Invoke([settings, _store, new CredentialVault(PassthroughSecretProtector.Instance),
                DeviceIdentity.Create("navigation-fixture", "test"), new RejectNetwork()]);
            _images = new EmbyImageStore(_session, Path.Combine(path.Root, "images"));
            Shell = new ShellPage(this, _session);
        }
        internal LibraryRequest MusicRequest(string tag) => new() { Services = this, Title = "匿名音乐库", ParentId = "music-root", CollectionType = "music", Tag = tag };
        public object? GetService(Type type) => type == typeof(EmbySession) ? _session : type == typeof(EmbyImageStore) ? _images
            : type == typeof(IShellActions) ? _actions : null;
        public void Dispose() => _session.Dispose();
        private sealed class ActionsBoundary : IShellActions { public void SetTitleStrip(TitleStrip strip) { } }
        private sealed class RejectNetwork : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
                throw new InvalidOperationException("Navigation state fixture refuses every HTTP request");
        }
    }
}
