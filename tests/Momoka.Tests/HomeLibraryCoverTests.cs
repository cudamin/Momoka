using System.Collections;
using System.Collections.Specialized;
using System.Reflection;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.LibraryResumePagingTests;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class HomeLibraryCoverTests
{
    internal static void Register()
    {
        HomeLibraryCoverStaticTests.Register();
        Test("主页媒体库封面：纯文字预设不枚举或读取图片候选", () =>
        {
            var library = Library();
            library.PrimaryImageTag = "original";
            var samples = Enumerable.Range(0, 1).Select<int, EmbyItem>(_ =>
                throw new AssertionException("纯文字预设不应读取图片候选"));
            Assert.Equal(0, HomeLibraryCoverArtwork.Select(library, samples, HomeLibraryCoverPresets.Gradient).Count);
        });
        Test("主页媒体库封面：叠卡优先海报且只取一张", () =>
        {
            var samples = Enumerable.Range(0, 12).Select(index => Sample("sample-" + index)).ToArray();
            var stack = HomeLibraryCoverArtwork.Select(Library(), samples, HomeLibraryCoverPresets.Stack);
            Assert.Equal(new ArtworkRef("sample-0", EmbyImageStore.Primary, "poster-sample-0"), stack.Single());
        });
        Test("主页媒体库封面：九宫格按既有顺序取九个不重复引用", () =>
        {
            var samples = Enumerable.Range(0, 15).Select(index => Sample("sample-" + index)).ToArray();
            var duplicated = samples.SelectMany(item => new[] { item, item }).ToArray();
            var pictures = HomeLibraryCoverArtwork.Select(Library(), duplicated, HomeLibraryCoverPresets.Wall);
            Assert.Equal(9, pictures.Count);
            Assert.Equal(9, pictures.Distinct().Count());
            Assert.True(pictures.Select(picture => picture.ItemId)
                .SequenceEqual(Enumerable.Range(0, 9).Select(index => "sample-" + index)));
        });
        Test("主页媒体库封面：只接受同账号同登录代次，捕获作用域可以不同", () =>
        {
            var identity = Identity("server", "account");
            var source = new EmbySessionScope(null!, identity);
            var sameLogin = new EmbySessionScope(null!, identity);
            var staleLogin = new EmbySessionScope(null!, Identity("server", "account"));
            var otherAccount = new EmbySessionScope(null!, Identity("server", "other-account"));
            var samples = new[]
            {
                Sample("other-account", otherAccount), Sample("stale-login", staleLogin),
                Sample("unbound"), Sample("same-login", sameLogin)
            };
            var pictures = HomeLibraryCoverArtwork.Select(Library(source), samples, HomeLibraryCoverPresets.Wall);
            Assert.Equal("same-login", pictures.Single().ItemId);
            Assert.Equal(source.Key, staleLogin.Key, "同存档键也不能跨新登录复用样图");
        });
        Test("主页媒体库封面：无来源库不借用任何已登录账号的样图", () =>
        {
            var source = new EmbySessionScope(null!, Identity("server", "account"));
            var pictures = HomeLibraryCoverArtwork.Select(Library(),
                new[] { Sample("remote", source), Sample("local") }, HomeLibraryCoverPresets.Wall);
            Assert.Equal("local", pictures.Single().ItemId);
        });
        Test("主页媒体库封面：空库沿用已有库图片，过滤重复和空路径", () =>
        {
            var library = Library();
            library.PosterPaths = ["", " ", "path-1", "path-1", "path-2"];
            library.PrimaryImageTag = "library-poster";
            library.PrimaryImageItemId = "poster-owner";
            var pictures = HomeLibraryCoverArtwork.Select(library, [], HomeLibraryCoverPresets.Wall);
            Assert.True(pictures.SequenceEqual(new[]
            {
                new ArtworkRef("library", EmbyImageStore.Primary, "path-1"),
                new ArtworkRef("library", EmbyImageStore.Primary, "path-2"),
                new ArtworkRef("poster-owner", EmbyImageStore.Primary, "library-poster")
            }));
            Assert.Equal(0, HomeLibraryCoverArtwork.Select(Library(), [], HomeLibraryCoverPresets.Wall).Count);
        });
        Test("主页媒体库封面：缺图跳过空标签，继承封面仍向真实图片属主取", () =>
        {
            var sample = Sample("child");
            sample.PrimaryImageTag = "";
            sample.ImageTags = new() { [EmbyImageStore.Thumb] = "thumb-only" };
            sample.BackdropImageTags = [];
            var fallback = HomeLibraryCoverArtwork.Select(Library(), new[] { sample }, HomeLibraryCoverPresets.Stack);
            Assert.Equal(new ArtworkRef("child", EmbyImageStore.Thumb, "thumb-only"), fallback.Single());

            sample.PrimaryImageTag = "inherited";
            sample.PrimaryImageItemId = "real-owner";
            Assert.Equal(new ArtworkRef("real-owner", EmbyImageStore.Primary, "inherited"),
                HomeLibraryCoverArtwork.Select(Library(), new[] { sample }, HomeLibraryCoverPresets.Stack).Single());
        });
        Test("主页媒体库封面：四后端只产生显示引用，不改库或样本记录", () =>
        {
            foreach (var backend in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin, MediaServerKind.FnNas, MediaServerKind.Plex })
            {
                var source = new EmbySessionScope(null!, Identity("server", "account", backend));
                var library = Library(source);
                library.PrimaryImageTag = "original-library";
                library.PosterPaths = ["original-path"];
                var sample = Sample("sample", source);
                var beforeLibrary = JsonSerializer.Serialize(library);
                var beforeSample = JsonSerializer.Serialize(sample);
                foreach (var preset in Presets)
                    HomeLibraryCoverArtwork.Select(library, new[] { sample }, preset);
                Assert.Equal(beforeLibrary, JsonSerializer.Serialize(library));
                Assert.Equal(beforeSample, JsonSerializer.Serialize(sample));
                Assert.True(ReferenceEquals(source, library.Source) && ReferenceEquals(source, sample.Source));
            }
        });
        Test("主页媒体库封面：同一身份的渐变配色跨运行保持稳定并在五组内", () =>
        {
            Assert.Equal(4, HomeLibraryCoverArtwork.PaletteIndex("library-a"));
            Assert.Equal(0, HomeLibraryCoverArtwork.PaletteIndex("library-b"));
            Assert.Equal(2, HomeLibraryCoverArtwork.PaletteIndex("媒体库"));
            foreach (var identity in Enumerable.Range(0, 100).Select(index => "account:library-" + index))
                Assert.True(HomeLibraryCoverArtwork.PaletteIndex(identity) is >= 0 and < 5);
        });
    }

    internal static int Run(string shellPath, string root)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Register();
        var shell = new ShellFixture(shellPath, root);
        Case("主页媒体库封面VM：仅库入口原地切换，保留其它货架且不重读元数据", async () =>
        {
            using var fixture = CoverFixture(shell);
            var home = fixture.CreateLoadingHome();
            try
            {
                await Load(shell, home);
                var shelves = Values(shell, home, "Shelves");
                var library = shell.Get<object>(home, "LibraryShelf");
                var libraryCards = Values(shell, library, "Cards");
                var otherCards = shelves.Where(shelf => !ReferenceEquals(shelf, library))
                    .SelectMany(shelf => Values(shell, shelf, "Cards")).ToArray();
                Assert.Equal(2, libraryCards.Length);
                Assert.True(otherCards.Length > 0);
                Assert.True(libraryCards.Concat(otherCards).All(card => shell.Get<string?>(card, "HomeLibraryCoverPreset") is null));
                var originals = libraryCards.Select(card => JsonSerializer.Serialize(shell.Get<EmbyItem>(card, "Item"))).ToArray();
                var requests = fixture.RequestCount;
                var rowChanges = 0;
                var cardChanges = 0;
                ((INotifyCollectionChanged)shell.Get<object>(home, "Shelves")).CollectionChanged += (_, _) => rowChanges++;
                ((INotifyCollectionChanged)shell.Get<object>(library, "Cards")).CollectionChanged += (_, _) => cardChanges++;
                var ui = fixture.Settings.Settings.Ui;
                foreach (var preset in Presets)
                {
                    ui.HomeLibraryCoverOverride = true;
                    ui.HomeLibraryCoverPreset = preset;
                    shell.Invoke(home, "ApplyLibraryCovers", ui);
                    Assert.True(libraryCards.All(card => shell.Get<string?>(card, "HomeLibraryCoverPreset") == preset));
                    Assert.True(otherCards.All(card => shell.Get<string?>(card, "HomeLibraryCoverPreset") is null));
                    Assert.True(libraryCards.SequenceEqual(Values(shell, library, "Cards")));
                    Assert.True(shelves.SequenceEqual(Values(shell, home, "Shelves")));
                }
                ui.HomeLibraryCoverOverride = false;
                shell.Invoke(home, "ApplyLibraryCovers", ui);
                Assert.True(libraryCards.All(card => shell.Get<string?>(card, "HomeLibraryCoverPreset") is null));
                Assert.Equal(HomeLibraryCoverPresets.Gradient, ui.HomeLibraryCoverPreset, "关闭不清除已选风格");
                Assert.Equal(requests, fixture.RequestCount);
                Assert.Equal(0, rowChanges);
                Assert.Equal(0, cardChanges);
                Assert.True(originals.SequenceEqual(libraryCards.Select(card => JsonSerializer.Serialize(shell.Get<EmbyItem>(card, "Item")))));
            }
            finally { ((IDisposable)home).Dispose(); }
        });
        Case("主页媒体库封面VM：聚合同名库按本库来源选样图，外来设置和离页通知无效", async () =>
        {
            using var fixture = CoverFixture(shell);
            var ui = fixture.Settings.Settings.Ui;
            ui.HomeLibraryCoverOverride = true;
            ui.HomeLibraryCoverPreset = HomeLibraryCoverPresets.Wall;
            var home = fixture.CreateLoadingHome();
            try
            {
                await Load(shell, home);
                var cards = Values(shell, shell.Get<object>(home, "LibraryShelf"), "Cards");
                foreach (var card in cards)
                {
                    var item = shell.Get<EmbyItem>(card, "Item");
                    var pictures = (IReadOnlyList<ArtworkRef>)Field(card, "_homeLibraryCoverArtwork")!;
                    Assert.Equal(9, pictures.Count);
                    Assert.True(pictures.All(picture => picture.ItemId.StartsWith(item.Source!.Connection.ApiBase.Host[0] + "-", StringComparison.Ordinal)));
                }
                var requests = fixture.RequestCount;
                shell.Invoke(home, "ApplyLibraryCovers", new UiSettings());
                Assert.True(cards.All(card => shell.Get<string?>(card, "HomeLibraryCoverPreset") == HomeLibraryCoverPresets.Wall));
                shell.Invoke(home, "Cancel");
                ui.HomeLibraryCoverOverride = false;
                shell.Invoke(home, "ApplyLibraryCovers", ui);
                Assert.True(cards.All(card => shell.Get<string?>(card, "HomeLibraryCoverPreset") == HomeLibraryCoverPresets.Wall));
                Assert.Equal(requests, fixture.RequestCount);
            }
            finally { ((IDisposable)home).Dispose(); }
        });
        Case("主页媒体库封面VM：暖缓存按当前风格恢复，不联网并允许恢复原图", async () =>
        {
            using var fixture = CoverFixture(shell);
            var ui = fixture.Settings.Settings.Ui;
            var store = new BrowseSnapshotStore();
            ui.HomeLibraryCoverOverride = true;
            ui.HomeLibraryCoverPreset = HomeLibraryCoverPresets.Stack;
            var first = fixture.CreateLoadingHome(snapshots: store);
            try { await Load(shell, first); }
            finally { ((IDisposable)first).Dispose(); }
            var requests = fixture.RequestCount;
            ui.HomeLibraryCoverPreset = HomeLibraryCoverPresets.Wall;
            var second = fixture.CreateLoadingHome(snapshots: store);
            try
            {
                Assert.True((bool)shell.Invoke(second, "RestoreSnapshot")!);
                var cards = Values(shell, shell.Get<object>(second, "LibraryShelf"), "Cards");
                Assert.True(cards.All(card => shell.Get<string?>(card, "HomeLibraryCoverPreset") == HomeLibraryCoverPresets.Wall));
                Assert.Equal(requests, fixture.RequestCount);
                ui.HomeLibraryCoverOverride = false;
                shell.Invoke(second, "ApplyLibraryCovers", ui);
                Assert.True(cards.All(card => shell.Get<string?>(card, "HomeLibraryCoverPreset") is null));
                Assert.Equal(requests, fixture.RequestCount);
            }
            finally { ((IDisposable)second).Dispose(); }
        });
        Case("主页媒体库封面VM：纯文字预设即使原封面可读也不下载图片", async () =>
        {
            using var fixture = CoverFixture(shell);
            fixture.Settings.Settings.Ui.HomeLibraryCoverOverride = true;
            fixture.Settings.Settings.Ui.HomeLibraryCoverPreset = HomeLibraryCoverPresets.Gradient;
            var home = fixture.CreateLoadingHome();
            try
            {
                await Load(shell, home);
                var cards = Values(shell, shell.Get<object>(home, "LibraryShelf"), "Cards");
                var requests = fixture.RequestCount;
                foreach (var card in cards)
                {
                    shell.Get<EmbyItem>(card, "Item").PrimaryImageTag = "available-original";
                    await (Task)shell.Invoke(card, "EnsurePosterAsync", Task.CompletedTask)!;
                    Assert.Equal(0, Values(shell, card, "HomeLibraryCoverImages").Length);
                }
                Assert.Equal(requests, fixture.RequestCount, "纯文字不读取服务器图片");
            }
            finally { ((IDisposable)home).Dispose(); }
        });
        Case("主页媒体库封面VM：切风格、弃卡和账号退出撤销旧图片需求，不消费迟到任务", async () =>
        {
            using var fixture = CoverFixture(shell);
            var ui = fixture.Settings.Settings.Ui;
            ui.HomeLibraryCoverOverride = true;
            ui.HomeLibraryCoverPreset = HomeLibraryCoverPresets.Wall;
            var home = fixture.CreateLoadingHome();
            try
            {
                await Load(shell, home);
                var card = Values(shell, shell.Get<object>(home, "LibraryShelf"), "Cards")
                    .Single(value => shell.Get<EmbyItem>(value, "Item").Source!.Key == fixture.Source("a").Key);
                var pictures = (IReadOnlyList<ArtworkRef>)Field(card, "_homeLibraryCoverArtwork")!;
                var requests = fixture.RequestCount;
                foreach (var change in new[] { "preset", "abandon", "source" })
                {
                    shell.Invoke(card, "AbandonPoster");
                    shell.Invoke(card, "ApplyHomeLibraryCover", HomeLibraryCoverPresets.Wall, pictures);
                    var presentation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var loading = (Task)shell.Invoke(card, "EnsurePosterAsync", presentation.Task)!;
                    Assert.False(loading.IsCompleted, "真实需求应等待图片呈现闸门");
                    if (change == "preset")
                        shell.Invoke(card, "ApplyHomeLibraryCover", HomeLibraryCoverPresets.Gradient, Array.Empty<ArtworkRef>());
                    else if (change == "abandon") shell.Invoke(card, "AbandonPoster");
                    else fixture.RevokeHomeSource("a");
                    await loading.WaitAsync(TimeSpan.FromSeconds(2));
                    presentation.TrySetResult();
                    Assert.Equal(0, Values(shell, card, "HomeLibraryCoverImages").Length);
                    Assert.Equal(requests, fixture.RequestCount, "已撤销的呈现需求不能发出图片请求");
                }
            }
            finally { ((IDisposable)home).Dispose(); }
        });
        return TestHarness.Run();
    }

    private static readonly string[] Presets =
    [HomeLibraryCoverPresets.Stack, HomeLibraryCoverPresets.Wall, HomeLibraryCoverPresets.Gradient];

    private static EmbyItem Library(EmbySessionScope? source = null) => new()
    {
        Id = "library",
        Name = "电影",
        Type = EmbyItemType.CollectionFolder,
        CollectionType = "movies",
        Source = source
    };

    private static EmbyItem Sample(string id, EmbySessionScope? source = null) => new()
    {
        Id = id,
        Name = id,
        Type = EmbyItemType.Movie,
        Source = source,
        PrimaryImageTag = "poster-" + id,
        ImageTags = new() { [EmbyImageStore.Thumb] = "thumb-" + id },
        BackdropImageTags = ["wide-" + id]
    };

    private static EmbySessionScope.Identity Identity(string server, string account, MediaServerKind backend = MediaServerKind.Emby)
    {
        var connection = new EmbyConnection(new Uri("https://fixture.invalid/"), "placeholder", "user", "user",
            "fixture", DeviceIdentity.Create("home-cover-fixture", "test"))
        { ServerKind = backend };
        return new EmbySessionScope.Identity(new EmbyClient(null!, connection),
            new ServerProfile { Id = server, Name = "fixture" }, new AccountProfile { Id = account, Username = "user" });
    }

    private static Fixture CoverFixture(ShellFixture shell)
    {
        var fixture = new Fixture(shell, 3, secondSource: true);
        fixture.Transport.HomeLatestCount = 12;
        fixture.Transport.LatestReply = uri => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(Enumerable.Range(0, 12)
                .Select(index => Sample(uri.Host[0] + "-sample-" + index))))
        };
        return fixture;
    }

    private static async Task Load(ShellFixture shell, object home)
    {
        await (Task)shell.Invoke(home, "ReloadAsync")!;
        await (Task)Field(home, "_supplement")!;
    }

    private static object[] Values(ShellFixture shell, object owner, string name) =>
        ((IEnumerable)shell.Get<object>(owner, name)).Cast<object>().ToArray();

    private static object? Field(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);

    private static void Case(string name, Func<Task> body) => Test(name, () => SingleThread.Run(body));
}
