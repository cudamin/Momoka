#if CARD_IMAGE_RESIZE_FIXTURE
using System.Runtime.InteropServices;
using System.Text;
using Momoka.Emby;
using Momoka.Shell.Media;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>实际图片存储与卡片源链接回归；只用内存图片 GET、假解码和本树缓存文件。</summary>
internal static class ImageRecoveryTests
{
    private const int Width = 400;
    public static void Register()
    {
        foreach (var code in new uint[] { 0x88982F61, 0x88982F62, 0x88982F63, 0x88982F70 })
            Case($"R-014：明确坏数据 {code:X8} 精确失效再取同键", () => RecoveryAsync(new COMException("synthetic data", unchecked((int)code)), destructive: true));
        Case("R-014：明确 InvalidDataException 精确失效并保存有效新图", () => RecoveryAsync(new InvalidDataException("synthetic corrupt"), destructive: true));
        foreach (var code in new uint[] { 0x88982F60, 0x88982F07 })
            Case($"R-014：模糊数据/格式 {code:X8} 无损有界重取，失败不删原文件", () => RecoveryAsync(new COMException("synthetic ambiguous", unchecked((int)code)), destructive: false));

        Case("R-014：一次 HTTP200 坏字节后只重取一次，有效图写回原键", async () =>
        {
            await using var fixture = new CardImageFixture();
            var pending = Decode(fixture, text => text == "bad" ? throw new InvalidDataException("bad bytes") : text);
            (await fixture.Handler.AtAsync(0)).Complete("bad");
            var retry = await fixture.Handler.AtAsync(1);
            retry.Complete("good");
            Assert.Equal("good", await pending);
            Assert.Equal(2, fixture.Handler.Count);
            Assert.Equal("good", await File.ReadAllTextAsync(fixture.CacheFile(Width)));
        });
        Case("R-014：两次坏数据停止，不无限请求也不保留确认坏的新字节", async () =>
        {
            await using var fixture = new CardImageFixture();
            await File.WriteAllTextAsync(fixture.CacheFile(Width), "bad");
            var pending = Decode(fixture, _ => throw new InvalidDataException("bad bytes"));
            (await fixture.Handler.AtAsync(0)).Complete("bad again");
            await ExpectAsync<InvalidDataException>(pending);
            Assert.Equal(1, fixture.Handler.Count);
            Assert.False(File.Exists(fixture.CacheFile(Width)));
        });
        Case("R-014：BADIMAGE 连续失败不删原件、不缓存未经解码的新字节", async () =>
        {
            await using var fixture = new CardImageFixture();
            await File.WriteAllTextAsync(fixture.CacheFile(Width), "original");
            var pending = Decode(fixture, _ => throw new COMException("ambiguous", unchecked((int)0x88982F60)));
            (await fixture.Handler.AtAsync(0)).Complete("unverified replacement");
            await ExpectAsync<COMException>(pending);
            Assert.Equal(1, fixture.Handler.Count);
            Assert.Equal("original", await File.ReadAllTextAsync(fixture.CacheFile(Width)));
        });
        foreach (var (name, error) in new (string, Exception)[]
        {
            ("取消", new OperationCanceledException()), ("权限", new UnauthorizedAccessException()),
            ("内存", new OutOfMemoryException()), ("一般IO", new IOException()),
            ("程序错误", new InvalidOperationException()), ("未知码", new COMException("generic", unchecked((int)0x80004005))),
            ("codec缺失", new COMException("missing codec", unchecked((int)0x88982F50))),
            ("明确低内存码", new COMException("memory", unchecked((int)0x8007000E)))
        })
            Case("R-014：非坏数据错误不失效或追加GET：" + name, async () =>
            {
                await using var fixture = new CardImageFixture();
                await File.WriteAllTextAsync(fixture.CacheFile(Width), "original");
                var thrown = await CatchAsync(Decode(fixture, _ => throw error));
                Assert.True(ReferenceEquals(error, thrown));
                Assert.Equal(0, fixture.Handler.Count);
                Assert.Equal("original", await File.ReadAllTextAsync(fixture.CacheFile(Width)));
            });
        Case("R-014：正常/空缓存和404沿用原读取契约", async () =>
        {
            await using var fixture = new CardImageFixture();
            await File.WriteAllTextAsync(fixture.CacheFile(Width), "good");
            Assert.Equal("good", await Decode(fixture, text => text));
            Assert.Equal(0, fixture.Handler.Count);
            await File.WriteAllTextAsync(fixture.CacheFile(Width), "");
            var miss = Decode(fixture, text => text);
            (await fixture.Handler.AtAsync(0)).Missing();
            Assert.Null(await miss);
            Assert.Equal(1, fixture.Handler.Count);
        });
        Case("R-014：解码器返回null不是明确坏数据，不删除或重试", async () =>
        {
            await using var fixture = new CardImageFixture();
            await File.WriteAllTextAsync(fixture.CacheFile(Width), "original");
            Assert.Null(await Decode(fixture, _ => null));
            Assert.Equal(0, fixture.Handler.Count);
            Assert.Equal("original", await File.ReadAllTextAsync(fixture.CacheFile(Width)));
        });
        Case("R-014：旧解码失败不能删除或覆盖已换新的同键缓存", async () =>
        {
            await using var fixture = new CardImageFixture();
            await File.WriteAllTextAsync(fixture.CacheFile(Width), "bad old");
            var failed = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = fixture.Images.GetDecodedAsync(fixture.Scope, fixture.Item.Id, "Primary", "image-tag", Width, bytes =>
            {
                if (Encoding.UTF8.GetString(bytes) != "bad old") return Task.FromResult<string?>("valid");
                entered.SetResult();
                return failed.Task;
            }, default);
            await entered.Task;
            await File.WriteAllTextAsync(fixture.CacheFile(Width), "new already good");
            failed.SetException(new InvalidDataException("old decode failed"));
            (await fixture.Handler.AtAsync(0)).Complete("fresh good");
            Assert.Equal("valid", await pending);
            Assert.Equal("new already good", await File.ReadAllTextAsync(fixture.CacheFile(Width)));
        });
        Case("R-014：同键模糊失败共用一次重取，取消一等待者不影响其它", async () =>
        {
            await using var fixture = new CardImageFixture();
            await File.WriteAllTextAsync(fixture.CacheFile(Width), "ambiguous");
            using var canceled = new CancellationTokenSource();
            string? DecodeText(string text) => text == "ambiguous" ? throw new COMException("badimage", unchecked((int)0x88982F60)) : text;
            var first = Decode(fixture, DecodeText, canceled.Token);
            var retry = await fixture.Handler.AtAsync(0);
            var second = Decode(fixture, DecodeText);
            await Task.Delay(20);
            Assert.Equal(1, fixture.Handler.Count);
            canceled.Cancel();
            await ExpectAsync<OperationCanceledException>(first);
            retry.Complete("good shared");
            Assert.Equal("good shared", await second);
            Assert.Equal("good shared", await File.ReadAllTextAsync(fixture.CacheFile(Width)));
        });
        Case("R-014：明确坏缓存失效后新同键调用共用恢复下载", async () =>
        {
            await using var fixture = new CardImageFixture();
            await File.WriteAllTextAsync(fixture.CacheFile(Width), "bad");
            string? DecodeText(string text) => text == "bad" ? throw new InvalidDataException("bad bytes") : text;
            var first = Decode(fixture, DecodeText);
            var retry = await fixture.Handler.AtAsync(0);
            var second = Decode(fixture, DecodeText);
            await Task.Delay(30);
            Assert.Equal(1, fixture.Handler.Count, "缓存已失效的后续调用不能另开普通下载");
            retry.Complete("good shared");
            Assert.Equal("good shared", await first);
            Assert.Equal("good shared", await second);
            Assert.Equal("good shared", await File.ReadAllTextAsync(fixture.CacheFile(Width)));
        });
        Case("R-014：有效恢复写入仍计入图片缓存周期清理预算", async () =>
        {
            await using var fixture = new CardImageFixture();
            await File.WriteAllTextAsync(fixture.CacheFile(Width), "bad");
            var count = typeof(EmbyImageStore).GetField("_sincePrune",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var pending = Decode(fixture, text => text == "bad" ? throw new InvalidDataException("bad bytes") : text);
            (await fixture.Handler.AtAsync(0)).Complete("good");
            Assert.Equal("good", await pending);
            Assert.Equal(1, (int)count.GetValue(fixture.Images)!, "成功恢复不能绕过现有清理计数");
            Assert.Equal("good", await Decode(fixture, text => text));
            Assert.Equal(1, (int)count.GetValue(fixture.Images)!, "普通缓存命中不重复记下载");
        });
        Case("R-014：来源撤销或同刻取消不删除原缓存、不发重取请求", async () =>
        {
            await using var fixture = new CardImageFixture();
            await File.WriteAllTextAsync(fixture.CacheFile(Width), "bad");
            var pending = Decode(fixture, _ =>
            {
                fixture.RevokeSource();
                throw new InvalidDataException("bad while revoked");
            });
            await ExpectAsync<OperationCanceledException>(pending);
            Assert.Equal(0, fixture.Handler.Count);
            Assert.Equal("bad", await File.ReadAllTextAsync(fixture.CacheFile(Width)));
        });
        Case("R-014：实际卡片坏缓存自动恢复并显示有效新位图边界", async () =>
        {
            await using var fixture = new CardImageFixture();
            await File.WriteAllTextAsync(fixture.CacheFile(Width), "bad");
            PosterLoader.ErrorFor = text => text == "bad" ? new InvalidDataException("synthetic corrupt") : null;
            var initial = fixture.Begin();
            (await fixture.Handler.AtAsync(0)).Complete("good card");
            await initial;
            Assert.Equal("good card", fixture.Card.Poster!.FixtureImage);
            Assert.Equal(170, fixture.Card.Poster.DecodePixelWidth);
            Assert.Equal("good card", await File.ReadAllTextAsync(fixture.CacheFile(Width)));
            Assert.Equal(1, fixture.Handler.Count);
        });
        Case("R-014：实际卡片旧非格式解码错误不污染换宽任务冷却", async () =>
        {
            await using var fixture = new CardImageFixture();
            await File.WriteAllTextAsync(fixture.CacheFile(Width), "old");
            PosterLoader.Hold = true;
            var initial = fixture.Begin();
            await CardImageFixture.UntilAsync(() => PosterLoader.Calls.Count == 1, "old decode not started");
            var old = PosterLoader.Calls[0];
            fixture.Card.Resize(250);
            (await fixture.Handler.AtAsync(0)).Complete("new width");
            await CardImageFixture.UntilAsync(() => PosterLoader.Calls.Count == 2, "new decode not started");
            old.Fail(new IOException("obsolete decode IO"));
            await initial;
            var failed = typeof(Momoka.Shell.Views.CardItem).GetField("_failedAt",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert.Equal(default(DateTime), (DateTime)failed.GetValue(fixture.Card)!, "已取消旧任务不应更新失败冷却");
            PosterLoader.Calls[1].Complete();
            await CardImageFixture.UntilAsync(() => fixture.Card.Poster is not null, "new poster missing");
            Assert.Equal(250, fixture.Card.Poster!.DecodePixelWidth);
            Assert.Equal(1, fixture.Handler.Count);
        });
        Case("R-014：实际卡片旧坏解码在换宽取消后不重取旧键", async () =>
        {
            await using var fixture = new CardImageFixture();
            await File.WriteAllTextAsync(fixture.CacheFile(Width), "bad old");
            PosterLoader.Hold = true;
            var initial = fixture.Begin();
            await CardImageFixture.UntilAsync(() => PosterLoader.Calls.Count == 1, "decode not started");
            var old = PosterLoader.Calls[0];
            fixture.Card.Resize(250);
            var current = await fixture.Handler.AtAsync(0);
            current.Complete("new width");
            await CardImageFixture.UntilAsync(() => PosterLoader.Calls.Count == 2, "new decode not started");
            old.Fail(new InvalidDataException("obsolete corrupt"));
            await initial;
            PosterLoader.Calls[1].Complete();
            await CardImageFixture.UntilAsync(() => fixture.Card.Poster is not null, "new poster missing");
            Assert.Equal(1, fixture.Handler.Count);
            Assert.Equal(250, fixture.Card.Poster!.DecodePixelWidth);
            Assert.Equal("bad old", await File.ReadAllTextAsync(fixture.CacheFile(Width)));
        });
    }

    private static async Task RecoveryAsync(Exception error, bool destructive)
    {
        await using var fixture = new CardImageFixture();
        await File.WriteAllTextAsync(fixture.CacheFile(Width), "bad");
        var other = fixture.CacheFile(560);
        await File.WriteAllTextAsync(other, "other size");
        var pending = Decode(fixture, text => text == "bad" ? throw error : text);
        var retry = await fixture.Handler.AtAsync(0);
        Assert.Equal(!destructive, File.Exists(fixture.CacheFile(Width)));
        Assert.Equal("other size", await File.ReadAllTextAsync(other));
        retry.Complete("good");
        Assert.Equal("good", await pending);
        Assert.Equal("good", await File.ReadAllTextAsync(fixture.CacheFile(Width)));
        Assert.Equal("good", await Decode(fixture, text => text));
        Assert.Equal(1, fixture.Handler.Count);
    }

    private static Task<string?> Decode(CardImageFixture fixture, Func<string, string?> decode, CancellationToken token = default) =>
        fixture.Images.GetDecodedAsync(fixture.Scope, fixture.Item.Id, "Primary", "image-tag", Width,
            bytes => Task.FromResult(decode(Encoding.UTF8.GetString(bytes))), token);
    private static async Task<Exception> CatchAsync(Task task)
    {
        try { await task; }
        catch (Exception error) { return error; }
        throw new AssertionException("expected failure");
    }
    private static async Task ExpectAsync<T>(Task task) where T : Exception => Assert.True(await CatchAsync(task) is T);
    private static void Case(string name, Func<Task> run) => Test(name, () => CardImageContext.Run(run));
}
#endif
