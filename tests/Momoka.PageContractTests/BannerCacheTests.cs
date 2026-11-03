#if CARD_IMAGE_RESIZE_FIXTURE
using System.Reflection;
using System.Text;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Media;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 轮播背景缓存的约定：同一来源、同一张图、同一套处理规则只解码一次；换 tag、清缓存、来源失效都不能
/// 沿用旧画面。这里编译链接真实 <see cref="BannerSlide"/> 与 <see cref="BannerBackdropCache"/>，磁盘缓存
/// 和缓存键走真实 Core；只有 WinUI 解码和位图是边界替身。
/// </summary>
internal static class BannerCacheTests
{
    public static void Register()
    {
        Case("轮播缓存：同图同尺寸第二次构造直接命中，不再解码", async () =>
        {
            using var fixture = new BannerFixture();
            fixture.Seed("banner-tag", "same-picture");
            BannerPictureLoader.Reset();

            var first = fixture.Slide("banner-tag");
            await first.EnsureAsync();
            Assert.NotNull(first.Picture);
            Assert.Equal(1, BannerPictureLoader.Decodes);

            var second = fixture.Slide("banner-tag");
            Assert.NotNull(second.Picture, "构造时就应该从缓存装进来，首帧不等解码");
            await second.EnsureAsync();

            Assert.Equal(1, BannerPictureLoader.Decodes, "同一张图第二次不应再解码");
            Assert.True(ReferenceEquals(first.Picture, second.Picture), "两张幻灯片应共用同一张已解码位图");
        });

        Case("轮播缓存：Release 之后回到这一张仍从内存命中", async () =>
        {
            using var fixture = new BannerFixture();
            fixture.Seed("banner-tag", "same-picture");
            BannerPictureLoader.Reset();

            var slide = fixture.Slide("banner-tag");
            await slide.EnsureAsync();
            Assert.Equal(1, BannerPictureLoader.Decodes);

            slide.Release();
            Assert.Null(slide.Picture);

            await slide.EnsureAsync();
            Assert.NotNull(slide.Picture, "释放只放幻灯片自己的引用，缓存还在");
            Assert.Equal(1, BannerPictureLoader.Decodes, "重新上台不应再解码");
        });

        Case("轮播缓存：tag 改变必须重新解码", async () =>
        {
            using var fixture = new BannerFixture();
            fixture.Seed("banner-tag", "old-picture");
            fixture.Seed("new-tag", "new-picture");
            BannerPictureLoader.Reset();

            var old = fixture.Slide("banner-tag");
            await old.EnsureAsync();
            var oldPicture = old.Picture;

            var changed = fixture.Slide("new-tag");
            Assert.Null(changed.Picture, "换了 tag 不能沿用旧画面");
            await changed.EnsureAsync();

            Assert.Equal(2, BannerPictureLoader.Decodes);
            Assert.True(!ReferenceEquals(oldPicture, changed.Picture));
            Assert.Equal("new-picture", changed.Picture!.FixtureImage);
        });

        Case("轮播缓存：清除图片缓存后重新解码", async () =>
        {
            using var fixture = new BannerFixture();
            fixture.Seed("banner-tag", "same-picture");
            BannerPictureLoader.Reset();

            var first = fixture.Slide("banner-tag");
            await first.EnsureAsync();
            Assert.Equal(1, BannerPictureLoader.Decodes);

            BannerBackdropCache.Clear();
            Assert.Equal(0, BannerBackdropCache.Bytes);

            var afterClear = fixture.Slide("banner-tag");
            Assert.Null(afterClear.Picture, "清除之后不应从内存命中");
            await afterClear.EnsureAsync();
            Assert.Equal(2, BannerPictureLoader.Decodes);
        });

        Case("轮播缓存：来源失效后不命中也不继续画旧图", async () =>
        {
            using var fixture = new BannerFixture();
            fixture.Seed("banner-tag", "same-picture");
            BannerPictureLoader.Reset();

            var loaded = fixture.Slide("banner-tag");
            await loaded.EnsureAsync();
            Assert.Equal(1, BannerPictureLoader.Decodes);

            fixture.Revoke();
            await loaded.EnsureAsync();
            Assert.Null(loaded.Picture, "来源失效后屏上不能还留着这一张");

            var afterRevoke = fixture.Slide("banner-tag");
            Assert.Null(afterRevoke.Picture, "失效来源的缓存不能被装进新幻灯片");
            await afterRevoke.EnsureAsync();
            Assert.Null(afterRevoke.Picture);
            Assert.Equal(1, BannerPictureLoader.Decodes, "失效来源不应再发起解码");
        });

        Case("轮播缓存：键区分来源账号、tag 与请求宽度", () =>
        {
            var connection = new EmbyConnection(
                new Uri("http://banner.invalid/emby/"), "token", "user-a", "user-a", "banner", FixtureDevice);
            var otherUser = connection with { UserId = "user-b", UserName = "user-b" };
            var artwork = new ArtworkRef("banner-item", EmbyImageStore.Backdrop, "tag-a");

            var baseline = BannerBackdropCache.Key(connection, artwork, 1280);
            Assert.Equal(baseline, BannerBackdropCache.Key(connection, artwork, 1280), "同样的输入必须得到同一把键");
            Assert.True(baseline != BannerBackdropCache.Key(otherUser, artwork, 1280), "换账号不能串图");
            Assert.True(baseline != BannerBackdropCache.Key(connection, artwork with { Tag = "tag-b" }, 1280), "换 tag 不能串图");
            // 640 逻辑宽和 1280 逻辑宽都会被 RequestWidth 归到 1280 这一档，共用同一份下载是应有行为；
            // 320 才会落到 640 那一档，用来证明宽度确实进了键。
            Assert.True(baseline != BannerBackdropCache.Key(connection, artwork, 320), "换请求宽度不能串图");
            return Task.CompletedTask;
        });

        Case("轮播缓存：预算按解码像素成本记账", async () =>
        {
            using var fixture = new BannerFixture();
            fixture.Seed("tag-one", "one");
            fixture.Seed("tag-two", "two");
            BannerPictureLoader.Reset();

            await fixture.Slide("tag-one").EnsureAsync();
            await fixture.Slide("tag-two").EnsureAsync();

            const long expected = 1280L * 720 * 4 * 2;
            Assert.Equal(expected, BannerBackdropCache.Bytes);
        });
    }

    private static void Case(string name, Func<Task> run) => Test(name, () => CardImageContext.Run(run));

    private static readonly DeviceIdentity FixtureDevice = new("banner-fixture", "banner-fixture", "banner-fixture", "1");

    internal sealed class BannerFixture : IDisposable
    {
        private readonly string _root;
        private readonly EmbySession _session;
        private readonly EmbySessionScope _scope;
        private readonly string _imageDirectory;

        public BannerFixture()
        {
            _root = Path.Combine(AppContext.BaseDirectory, "fixtures", "banner-cache", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);

            var settings = new AppSettings();
            _session = new EmbySession(settings, new SettingsStore(new AppPaths(_root), new NoSecrets()),
                new CredentialVault(new NoSecrets()), FixtureDevice, new NoHttp());
            var http = (EmbyHttp)typeof(EmbySession)
                .GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!;
            var connection = new EmbyConnection(new Uri("http://banner.invalid/emby/"), "placeholder-only",
                "banner-user", "banner-user", "banner", FixtureDevice);
            var identity = new EmbySessionScope.Identity(new EmbyClient(http, connection),
                new ServerProfile { Id = "banner-server" }, new AccountProfile { Id = "banner-account" });
            _scope = new EmbySessionScope(_session, identity);
            var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession)
                .GetField("_sources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!;
            sources.Add(_scope.Key, _scope);

            _imageDirectory = Path.Combine(_root, "images");
            Images = new EmbyImageStore(_session, _imageDirectory);
        }

        public EmbyImageStore Images { get; }

        public void Seed(string tag, string content)
        {
            var key = EmbyImageStore.CacheKey(_scope.Connection, "banner-item", EmbyImageStore.Backdrop, tag,
                EmbyImageStore.RequestWidth(1280));
            File.WriteAllBytes(Path.Combine(_imageDirectory, key), Encoding.UTF8.GetBytes(content));
        }

        public BannerSlide Slide(string tag) => new(_scope.Bind(new EmbyItem
        {
            Id = "banner-item",
            Name = "banner",
            Type = EmbyItemType.Movie,
            BackdropImageTags = [tag]
        }), Images);

        public void Revoke()
        {
            var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession)
                .GetField("_sources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!;
            sources.Remove(_scope.Key);
            _scope.IdentityState.Lifetime.Cancel();
        }

        public void Dispose()
        {
            BannerBackdropCache.Clear();
            BannerPictureLoader.Reset();
            _session.Dispose();
        }
    }

    private sealed class NoHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new AssertionException("轮播缓存夹具只读预置磁盘缓存，不应发起 HTTP");
    }

    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new AssertionException("禁止凭据 IO");
        public string Unprotect(string cipherText) => throw new AssertionException("禁止凭据 IO");
    }
}
#endif
