using System.Reflection;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Shell.Media;
using static Momoka.Tests.Program;

namespace Momoka.Tests;

internal static class DetailHeroCacheTests
{
    internal static void Register()
    {
        Test("详情背景缓存：相同半径的盒式与真高斯隔离且清除旧票双模式均拒绝", async () =>
        {
            await using var fixture = new StoreFixture();
            using var cache = new DetailHeroCache(fixture.Session);
            BitmapImage.ExpectedThread = Environment.CurrentManagedThreadId;
            var pixels = new HeroPictureLoader.HeroPixels(new byte[64], 4, 4);
            var ticket = cache.Begin(fixture.Scope, new ArtworkRef("a", "Backdrop", "tag"), 1280);
            cache.RememberPixels(ticket, pixels);
            cache.RememberPicture(ticket, pixels, 12, new BitmapImage());
            Assert.True(cache.TryGetPicture(ticket, pixels, 12, out _));
            Assert.False(cache.TryGetPicture(ticket, pixels, 12, out _, gaussian: true));
            cache.RememberPicture(ticket, pixels, 12, new BitmapImage(), gaussian: true);
            Assert.True(cache.TryGetPicture(ticket, pixels, 12, out _, gaussian: true));
            Assert.False(cache.TryGetPicture(ticket, pixels, 12, out _));
            Assert.Equal(128L, cache.Bytes);
            cache.Clear();
            cache.RememberPicture(ticket, pixels, 12, new BitmapImage(), gaussian: true);
            cache.RememberPicture(ticket, pixels, 12, new BitmapImage());
            Assert.False(cache.TryGetPicture(ticket, pixels, 12, out _, gaussian: true));
            Assert.False(cache.TryGetPicture(ticket, pixels, 12, out _));
            Assert.Equal(0L, cache.Bytes);
        });
        Test("详情背景缓存：清除发生在失败候选后，整条旧候选链不能借新代次回填", async () =>
        {
            await using var fixture = new StoreFixture();
            using var cache = new DetailHeroCache(fixture.Session);
            var operation = cache.Generation;
            cache.Begin(fixture.Scope, new ArtworkRef("a", "Backdrop", "tag"), 1280, operation);
            cache.Clear();
            var fallback = cache.Begin(fixture.Scope, new ArtworkRef("a", "Thumb", "tag"), 1280, operation);
            cache.RememberPixels(fallback, new HeroPictureLoader.HeroPixels(new byte[64], 4, 4));
            Assert.Equal(0L, cache.Bytes);
        });
        Test("详情背景缓存：清除后读旧票不干扰新票，旧标签和在途结果无法复活", async () =>
        {
            await using var fixture = new StoreFixture();
            using var cache = new DetailHeroCache(fixture.Session);
            BitmapImage.ExpectedThread = Environment.CurrentManagedThreadId;
            var pixels = new HeroPictureLoader.HeroPixels(new byte[64], 4, 4);
            var old = cache.Begin(fixture.Scope, new ArtworkRef("a", "Backdrop", "old"), 1280);
            var fresh = cache.Begin(fixture.Scope, new ArtworkRef("a", "Backdrop", "new"), 1280);
            cache.RememberPixels(fresh, pixels);
            cache.RememberPicture(old, pixels, 0, new BitmapImage());
            Assert.Equal(64L, cache.Bytes);
            Assert.False(cache.TryGetPixels(old, out _));
            Assert.True(cache.TryGetPixels(fresh, out _));
        });
        Test("详情背景缓存：不同登录owner隔离，同owner新scope与令牌轮换可复用", async () =>
        {
            await using var fixture = new StoreFixture();
            using var cache = new DetailHeroCache(fixture.Session);
            var pixels = new HeroPictureLoader.HeroPixels(new byte[64], 4, 4);
            var first = cache.Begin(fixture.Scope, new ArtworkRef("a", "Backdrop", "tag"), 1280);
            cache.RememberPixels(first, pixels);
            var same = new EmbySessionScope(fixture.Session, fixture.Scope.IdentityState);
            Assert.True(cache.TryGetPixels(cache.Begin(same, new ArtworkRef("a", "Backdrop", "tag"), 1280), out _));
            var connection = fixture.Scope.Connection with { AccessToken = "rotated-placeholder" };
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fixture.Session)!;
            fixture.Scope.IdentityState.Client = new EmbyClient(http, connection);
            Assert.True(cache.TryGetPixels(cache.Begin(new EmbySessionScope(fixture.Session, fixture.Scope.IdentityState),
                new ArtworkRef("a", "Backdrop", "tag"), 1280), out _));
            var identity = new EmbySessionScope.Identity(new EmbyClient(http, connection),
                fixture.Scope.IdentityState.Server, fixture.Scope.IdentityState.Account);
            var replacement = new EmbySessionScope(fixture.Session, identity);
            var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fixture.Session)!;
            sources[replacement.Key] = replacement;
            fixture.Scope.IdentityState.Lifetime.Cancel();
            typeof(EmbySession).GetMethod("NotifySourcesChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(fixture.Session, null);
            Assert.Equal(0L, cache.Bytes);
            Assert.False(cache.TryGetPixels(cache.Begin(replacement, new ArtworkRef("a", "Backdrop", "tag"), 1280), out _));
            identity.Lifetime.Cancel();
        });
        Test("详情背景缓存：精确图身份和半径复用，原像素与位图共用字节预算", async () =>
        {
            await using var fixture = new StoreFixture();
            using var cache = new DetailHeroCache(fixture.Session, 128);
            BitmapImage.ExpectedThread = Environment.CurrentManagedThreadId;
            var pixels = new HeroPictureLoader.HeroPixels(new byte[64], 4, 4);
            var picture = new BitmapImage();
            var request = cache.Begin(fixture.Scope, new ArtworkRef("a", "Backdrop", "tag"), 1280);
            Assert.False(cache.TryGetPixels(request, out _));
            cache.RememberPixels(request, pixels);
            cache.RememberPicture(request, pixels, 0, picture);
            Assert.Equal(128L, cache.Bytes);
            Assert.True(cache.TryGetPixels(request, out var kept) && ReferenceEquals(pixels, kept));
            Assert.True(cache.TryGetPicture(request, pixels, 0, out var keptPicture) && ReferenceEquals(picture, keptPicture));
            Assert.False(cache.TryGetPicture(request, pixels, 6, out _));
            Assert.False(cache.TryGetPixels(cache.Begin(fixture.Scope, new ArtworkRef("a", "Backdrop", "tag"), 640), out _));
            Assert.False(cache.TryGetPixels(cache.Begin(fixture.Scope, new ArtworkRef("b", "Backdrop", "tag"), 1280), out _));
            Assert.False(cache.TryGetPixels(cache.Begin(fixture.Scope, new ArtworkRef("a", "Primary", "tag"), 1280), out _));
        });
        Test("详情背景缓存：换标签立即丢旧图，容量淘汰含图像和原料，超限不留", async () =>
        {
            await using var fixture = new StoreFixture();
            using var cache = new DetailHeroCache(fixture.Session, 128);
            BitmapImage.ExpectedThread = Environment.CurrentManagedThreadId;
            var pixels = new HeroPictureLoader.HeroPixels(new byte[64], 4, 4);
            var old = cache.Begin(fixture.Scope, new ArtworkRef("a", "Backdrop", "old"), 1280);
            cache.RememberPixels(old, pixels);
            cache.RememberPicture(old, pixels, 0, new BitmapImage());
            var changed = cache.Begin(fixture.Scope, new ArtworkRef("a", "Backdrop", "new"), 1280);
            Assert.Equal(0L, cache.Bytes);
            Assert.False(cache.TryGetPixels(changed, out _));
            cache.RememberPixels(changed, pixels);
            cache.RememberPicture(changed, pixels, 0, new BitmapImage());
            var second = cache.Begin(fixture.Scope, new ArtworkRef("b", "Backdrop", "tag"), 1280);
            cache.RememberPixels(second, pixels);
            cache.RememberPicture(second, pixels, 6, new BitmapImage());
            Assert.Equal(128L, cache.Bytes);
            Assert.False(cache.TryGetPixels(changed, out _));
            cache.RememberPixels(second, new HeroPictureLoader.HeroPixels(new byte[256], 8, 8));
            Assert.Equal(0L, cache.Bytes);
        });
        Test("详情背景缓存：清除贯穿原料和后续重糊，旧票永不重新填充", async () =>
        {
            await using var fixture = new StoreFixture();
            using var cache = new DetailHeroCache(fixture.Session);
            BitmapImage.ExpectedThread = Environment.CurrentManagedThreadId;
            var pixels = new HeroPictureLoader.HeroPixels(new byte[64], 4, 4);
            var old = cache.Begin(fixture.Scope, new ArtworkRef("a", "Backdrop", "tag"), 1280);
            cache.RememberPixels(old, pixels);
            cache.Clear();
            cache.RememberPicture(old, pixels, 6, new BitmapImage());
            cache.RememberPixels(old, pixels);
            Assert.Equal(0L, cache.Bytes);
            var fresh = cache.Begin(fixture.Scope, new ArtworkRef("a", "Backdrop", "tag"), 1280);
            cache.RememberPixels(fresh, pixels);
            Assert.Equal(64L, cache.Bytes);
            Assert.False(cache.TryGetPixels(old, out _));
        });
        Test("详情背景缓存：来源退出后台剔除，旧scope不能命中或填回，Dispose退订", async () =>
        {
            await using var fixture = new StoreFixture();
            var subscribers = typeof(EmbySession).GetField("SourcesChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var before = ((Delegate?)subscribers.GetValue(fixture.Session))?.GetInvocationList().Length ?? 0;
            var cache = new DetailHeroCache(fixture.Session);
            var pixels = new HeroPictureLoader.HeroPixels(new byte[64], 4, 4);
            var request = cache.Begin(fixture.Scope, new ArtworkRef("a", "Backdrop", "tag"), 1280);
            cache.RememberPixels(request, pixels);
            fixture.Scope.IdentityState.Lifetime.Cancel();
            await Task.Run(() => typeof(EmbySession).GetMethod("NotifySourcesChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(fixture.Session, null));
            Assert.Equal(0L, cache.Bytes);
            Assert.False(cache.TryGetPixels(request, out _));
            cache.RememberPixels(request, pixels);
            Assert.Equal(0L, cache.Bytes);
            cache.Dispose();
            Assert.Equal(before, ((Delegate?)subscribers.GetValue(fixture.Session))?.GetInvocationList().Length ?? 0);
        });
    }
}
