using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Windows.Input;
using Momoka.Emby;
using Momoka.Playback;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;

namespace Momoka.Tests;

internal static class PlexAffordanceTests
{
    private static string _root = "";
    private static ShellAssembly _shell = null!;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args is ["agreements"])
        {
            AgreementsTests.Register();
            return TestHarness.Run();
        }
        if (args is not [var assembly, var root])
        {
            Console.Error.WriteLine("需要本树当前 Release Shell DLL 和本树根目录；不启动应用");
            return 2;
        }
        try
        {
            _root = Path.GetFullPath(root);
            _shell = new ShellAssembly(assembly, _root);
            Console.WriteLine("R-029/R-030 实际卡片命令／详情VM／题材页面契约；假HTTP、无native或真实UI");
            RegisterCards();
            RegisterDetails();
            RegisterGenres();
            return TestHarness.Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Case(string name, Func<Task> action) => TestHarness.Test(name, () => AffordanceContext.Run(action));
    private static PlexAffordanceFixture New() => new(_root);
    private static VisibilityContract CardButton(CardItem card, string handle) =>
        new(VisibilityContract.Element(_root, "PosterCard.xaml", handle), card);
    private static VisibilityContract DetailButton(object vm, string handle) =>
        new(VisibilityContract.Element(_root, "DetailPage.xaml", handle), vm);
    private static async Task<object> Detail(PlexAffordanceFixture fixture, string source, string type,
        PlexActionsProxy actions, object? previous = null)
    {
        var vm = previous ?? _shell.NewViewModel();
        _shell.Attach(vm, fixture, fixture.Item(source, type), actions);
        await (Task)ShellAssembly.Invoke(vm, "ReloadAsync")!;
        Assert.Null(vm.GetType().GetField("_versionLoad", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm),
            "本项同步假读取后不能留后台版本任务，避免回归计数先结束而队列还在工作");
        Assert.False(ShellAssembly.Get<bool>(vm, "NoticeOpen"), "假详情读取必须实际成功，不能拿失败页冒充显隐");
        return vm;
    }

    private static void RegisterCards()
    {
        foreach (var source in new[] { "plex", "emby", "jellyfin" })
            foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Episode })
                foreach (var layout in new[] { "主页续播", "列表卡" })
                {
                    var name = $"R-029 {source} {type} {layout}收藏单独显隐，已看保留";
                    Case(name, () =>
                    {
                        using var f = New();
                        f.Default(source == "plex" ? "emby" : "plex");
                        var card = f.Card(f.Item(source, type), wide: layout == "主页续播", sourceLine: layout == "主页续播");
                        using var favorite = CardButton(card, "FavoriteButton");
                        using var watched = CardButton(card, "WatchedButton");
                        Assert.Equal(source == "plex" ? "Collapsed" : "Visible", favorite.Value);
                        Assert.Equal("Visible", watched.Value);
                        Assert.True(card.Item.TracksUserState);
                        Assert.Equal(0, f.Transport.Requests.Count);
                        return Task.CompletedTask;
                    });
                }
        Case("R-029 聚合混来源／管理默认来回切换，不把Plex能力施给Emby/Jellyfin", () =>
        {
            using var f = New();
            var cards = new[] { "plex", "emby", "jellyfin" }.Select(source => f.Card(f.Item(source), sourceLine: true)).ToArray();
            using var plex = CardButton(cards[0], "FavoriteButton");
            using var emby = CardButton(cards[1], "FavoriteButton");
            using var jellyfin = CardButton(cards[2], "FavoriteButton");
            foreach (var selected in new[] { "plex", "jellyfin", "emby", "plex" })
            {
                f.Default(selected);
                Assert.Equal("Collapsed", plex.Value);
                Assert.Equal("Visible", emby.Value);
                Assert.Equal("Visible", jellyfin.Value);
                Assert.True(FavoriteAccounts.Applicable(f.Session.Sources).All(scope => !scope.Connection.IsPlex));
            }
            Assert.Equal(0, f.Transport.Requests.Count);
            return Task.CompletedTask;
        });
        foreach (var type in new[] { EmbyItemType.CollectionFolder, EmbyItemType.Person })
            Case("R-029 无UserState类型仍收起两按钮：" + type, () =>
            {
                using var f = New();
                var card = f.Card(f.Item("emby", type));
                using var favorite = CardButton(card, "FavoriteButton");
                using var watched = CardButton(card, "WatchedButton");
                Assert.Equal("Collapsed", favorite.Value);
                Assert.Equal("Collapsed", watched.Value);
                return Task.CompletedTask;
            });
        foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Episode })
            foreach (var entry in new[] { "海报旧快捷", "集行旧快捷", "直接Toggle", "旧菜单Run" })
                Case($"R-029 Plex {type} {entry}零请求／零状态账／零通知", async () =>
                {
                    using var f = New();
                    var card = f.Card(f.Item("plex", type, favorite: true));
                    var original = card.Item.UserData;
                    var identity = card.Item.IdentityKey;
                    var changes = 0;
                    card.PropertyChanged += (_, _) => changes++;
                    if (entry == "直接Toggle") ItemCommands.ToggleFavorite(f.Session, f.SourceActions, card);
                    else if (entry == "旧菜单Run")
                    {
                        ItemCommands.Run(ItemCommand.Favorite, f.Session, f.SourceActions, new FrameworkElement(), card);
                        ItemCommands.Run(ItemCommand.Unfavorite, f.Session, f.SourceActions, new FrameworkElement(), card);
                    }
                    else
                    {
                        object sender = entry == "海报旧快捷" ? new PosterCard { Card = card } : new EpisodeRow { Card = card };
                        ItemCommands.Handle(f.Session, f.SourceActions, sender, new CardActionEventArgs(CardAction.Favorite, new FrameworkElement()));
                    }
                    await AffordanceContext.Until(() => PlexAffordanceFixture.Pending(card) == 0);
                    Assert.Equal(0, f.Actions.Messages.Count, "旧入口不能报误导性收藏失败");
                    Assert.Equal(0, f.Transport.Requests.Count);
                    Assert.Equal(0, f.Transport.Writes);
                    Assert.Equal(0, f.Actions.FavoriteRefreshes.Count);
                    Assert.Equal(0, changes);
                    Assert.Equal(identity, card.Item.IdentityKey);
                    Assert.True(ReferenceEquals(original, card.Item.UserData));
                    Assert.True(card.IsFavorite);
                    Assert.False(PlexAffordanceFixture.HasStateOrder(card), "拒绝发生在创建用户状态操作账之前");
                });
        Case("R-029 旧收藏低层入口也在副作用前守卫", async () =>
        {
            using var f = New();
            var card = f.Card(f.Item("plex"));
            var method = typeof(ItemCommands).GetMethod("SetFavorite", BindingFlags.Static | BindingFlags.NonPublic)!;
            method.Invoke(null, [f.Scope("plex"), f.SourceActions, card, true]);
            await AffordanceContext.Until(() => PlexAffordanceFixture.Pending(card) == 0);
            Assert.Equal(0, f.Actions.Messages.Count);
            Assert.Equal(0, f.Transport.Requests.Count);
            Assert.False(PlexAffordanceFixture.HasStateOrder(card));
        });
        foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Episode })
            foreach (var senderKind in new[] { "海报", "集行" })
                Case($"R-029 Plex {type} {senderKind}已看旧快捷仍写所属来源", async () =>
                {
                    using var f = New();
                    var card = f.Card(f.Item("plex", type));
                    object sender = senderKind == "海报" ? new PosterCard { Card = card } : new EpisodeRow { Card = card };
                    ItemCommands.Handle(f.Session, f.SourceActions, sender, new CardActionEventArgs(CardAction.Watched, new FrameworkElement()));
                    await AffordanceContext.Until(() => PlexAffordanceFixture.Pending(card) == 0);
                    Assert.True(card.Watched);
                    Assert.Equal(1, f.Transport.Writes);
                    Assert.True(f.Transport.Requests.All(request => request.Host == "r029-plex.invalid"));
                    Assert.Equal(2, f.Transport.Requests.Count, "Plex已看仍执行确认写加实际元数据读回");
                    Assert.Equal(1, f.Actions.WatchedReports.Count);
                    Assert.Equal(0, f.Actions.Messages.Count);
                });
        foreach (var source in new[] { "emby", "jellyfin" })
            foreach (var entry in new[] { "快捷", "菜单" })
                Case($"R-029 {source} {entry}收藏保留原scope和既有错误语义", async () =>
                {
                    using var f = New();
                    f.Default("plex");
                    var card = f.Card(f.Item(source));
                    if (entry == "快捷") ItemCommands.ToggleFavorite(f.Session, f.SourceActions, card);
                    else ItemCommands.Run(ItemCommand.Favorite, f.Session, f.SourceActions, new FrameworkElement(), card);
                    await AffordanceContext.Until(() => PlexAffordanceFixture.Pending(card) == 0);
                    Assert.True(card.IsFavorite);
                    Assert.Equal(1, f.Transport.Writes);
                    Assert.Equal("r029-" + source + ".invalid", f.Transport.Requests.Single().Host);
                    Assert.Equal(1, f.Actions.FavoriteRefreshes.Count);
                    Assert.Equal(0, f.Actions.Messages.Count);
                    f.Transport.FailWrites = true;
                    ItemCommands.ToggleFavorite(f.Session, f.SourceActions, card);
                    await AffordanceContext.Until(() => PlexAffordanceFixture.Pending(card) == 0);
                    Assert.True(card.IsFavorite);
                    Assert.Equal(2, f.Transport.Writes);
                    Assert.Equal(1, f.Actions.Messages.Count, "真实支持来源的提交失败不能被隐藏门掩盖");
                });
    }

    private static void RegisterDetails()
    {
        foreach (var source in new[] { "plex", "emby", "jellyfin" })
            foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Episode })
                foreach (var width in new[] { 1320.0, 760.0 })
                    Case($"R-029 真详情 {source} {type} {width}宽窄收藏／已看分离", async () =>
                    {
                        using var f = New();
                        var actions = _shell.Actions();
                        var vm = await Detail(f, source, type, actions);
                        try
                        {
                            ShellAssembly.Set(vm, "PageWidth", width);
                            using var favorite = DetailButton(vm, width > 1024 ? "ColumnFavoriteButton" : "CompactFavoriteButton");
                            using var watched = DetailButton(vm, width > 1024 ? "ColumnWatchedButton" : "CompactWatchedButton");
                            Assert.Equal(source == "plex" ? "Collapsed" : "Visible", favorite.Value);
                            Assert.Equal("Visible", watched.Value);
                            Assert.True(ShellAssembly.Get<ICommand>(vm, "ToggleWatchedCommand").CanExecute(null));
                            Assert.Equal(source != "plex", ShellAssembly.Get<ICommand>(vm, "ToggleFavoriteCommand").CanExecute(null));
                            Assert.Equal(0, f.Transport.Writes);
                            Assert.Equal(0, actions.Messages.Count);
                        }
                        finally { ((IDisposable)vm).Dispose(); }
                    });
        foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Episode })
            foreach (var entry in new[] { "直接方法", "旧生成命令" })
                Case($"R-029 真Plex详情 {type} {entry}拒绝添加和取消收藏，零写零通知", async () =>
                {
                    foreach (var favorite in new[] { false, true })
                    {
                        using var f = New();
                        var actions = _shell.Actions();
                        var vm = await Detail(f, "plex", type, actions);
                        try
                        {
                            var item = (EmbyItem)vm.GetType().GetField("_detail", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
                            item.UserData!.IsFavorite = favorite;
                            ShellAssembly.Set(vm, "Favorite", favorite);
                            var original = item.UserData;
                            var before = f.Transport.Requests.Count;
                            if (entry == "直接方法") await (Task)ShellAssembly.Invoke(vm, "ToggleFavoriteAsync")!;
                            else await (Task)ShellAssembly.Invoke(ShellAssembly.Get<object>(vm, "ToggleFavoriteCommand"), "ExecuteAsync", [null])!;
                            Assert.Equal(0, actions.Messages.Count, "不把本地能力拒绝变成收藏更新失败");
                            Assert.Equal(before, f.Transport.Requests.Count);
                            Assert.Equal(0, f.Transport.Writes);
                            Assert.Equal(0, actions.FavoriteRefreshes.Count);
                            Assert.Equal(favorite, ShellAssembly.Get<bool>(vm, "Favorite"));
                            Assert.True(ReferenceEquals(original, item.UserData));
                            Assert.Equal(favorite, item.UserData.IsFavorite);
                        }
                        finally { ((IDisposable)vm).Dispose(); }
                    }
                });
        foreach (var source in new[] { "emby", "jellyfin" })
            Case($"R-029 真{source}详情收藏在Plex默认下仍向自身来源写入", async () =>
            {
                using var f = New();
                f.Default("plex");
                var actions = _shell.Actions();
                var vm = await Detail(f, source, EmbyItemType.Movie, actions);
                try
                {
                    await (Task)ShellAssembly.Invoke(vm, "ToggleFavoriteAsync")!;
                    Assert.True(ShellAssembly.Get<bool>(vm, "Favorite"));
                    Assert.Equal(1, f.Transport.Writes);
                    Assert.Equal("r029-" + source + ".invalid", f.Transport.Requests.Last().Host);
                    Assert.Equal(1, actions.FavoriteRefreshes.Count);
                    Assert.Equal(0, actions.Messages.Count);
                }
                finally { ((IDisposable)vm).Dispose(); }
            });
        foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Episode })
            Case($"R-029 真详情 {type} 同VM切来源／宽窄／Cancel后重入的绑定恢复", async () =>
            {
                using var f = New();
                var actions = _shell.Actions();
                var vm = await Detail(f, "emby", type, actions);
                using var wide = DetailButton(vm, "ColumnFavoriteButton");
                using var narrow = DetailButton(vm, "CompactFavoriteButton");
                try
                {
                    foreach (var source in new[] { "plex", "jellyfin", "plex", "emby" })
                    {
                        vm = await Detail(f, source, type, actions, vm);
                        foreach (var width in new[] { 760.0, 1320.0 })
                        {
                            ShellAssembly.Set(vm, "PageWidth", width);
                            Assert.Equal(source == "plex" ? "Collapsed" : "Visible", wide.Value);
                            Assert.Equal(wide.Value, narrow.Value);
                        }
                    }
                    ShellAssembly.Invoke(vm, "Cancel");
                    Assert.Equal("Collapsed", wide.Value);
                    Assert.False(ShellAssembly.Get<ICommand>(vm, "ToggleFavoriteCommand").CanExecute(null));
                    vm = await Detail(f, "emby", type, actions, vm);
                    Assert.Equal("Visible", wide.Value);
                    Assert.Equal("Visible", narrow.Value);
                    Assert.Equal(0, f.Transport.Writes);
                }
                finally { ((IDisposable)vm).Dispose(); }
            });
        foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Episode })
            Case($"R-029 真Plex详情 {type} 已看命令保留，刷新不触发收藏", async () =>
            {
                using var f = New();
                var actions = _shell.Actions();
                var vm = await Detail(f, "plex", type, actions);
                try
                {
                    var before = f.Transport.Writes;
                    await (Task)ShellAssembly.Invoke(vm, "ToggleWatchedAsync")!;
                    Assert.Equal(before + 1, f.Transport.Writes);
                    Assert.True(ShellAssembly.Get<bool>(vm, "Watched"));
                    Assert.Equal(1, actions.WatchedReports.Count);
                    Assert.Equal(0, actions.FavoriteRefreshes.Count);
                    Assert.Equal(0, actions.Messages.Count);
                    Assert.Equal("Collapsed", ShellAssembly.Get<object>(vm, "FavoriteActionVisibility").ToString());
                }
                finally { ((IDisposable)vm).Dispose(); }
            });
        foreach (var detailSource in new[] { "plex", "emby" })
            Case($"R-029/R-030 真详情 {detailSource} 切异源媒体版本，不误把播放源当详情scope", async () =>
            {
                using var f = New();
                var actions = _shell.Actions();
                var vm = await Detail(f, detailSource, EmbyItemType.Movie, actions);
                using var page = new GenrePageContract(vm);
                using var favorite = DetailButton(vm, "ColumnFavoriteButton");
                try
                {
                    page.Paint();
                    var playbackSource = detailSource == "plex" ? "emby" : "plex";
                    var alternate = f.Item(playbackSource, id: "alternate");
                    var media = new MediaSource { Id = "source", ItemId = alternate.Id, Name = "假媒体版本" };
                    var versions = new[] { new MediaVersion(alternate, media) };
                    vm.GetType().GetField("_versions", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, versions);
                    ShellAssembly.Set(vm, "SelectedSource", new SourceRow(media, "异源占位版本"));
                    Assert.True(ReferenceEquals(alternate, ShellAssembly.Get<EmbyItem>(vm, "PlayTarget")),
                        "真实OnSelectedSourceChanged执行，但本项不运行播放");
                    f.Default(playbackSource);
                    Assert.Equal(detailSource == "plex" ? "Collapsed" : "Visible", favorite.Value);
                    var before = f.Transport.Requests.Count;
                    ShellAssembly.Invoke(vm, "OpenGenre", "动画");
                    await (Task)ShellAssembly.Invoke(vm, "ToggleFavoriteAsync")!;
                    if (detailSource == "plex")
                    {
                        Assert.Equal(before, f.Transport.Requests.Count);
                        Assert.Equal(0, actions.Navigation.Count);
                        Assert.Equal(0, page.Links.Count);
                        Assert.Equal(0, f.Transport.Writes);
                    }
                    else
                    {
                        Assert.Equal(2, page.Links.Count);
                        Assert.True(ReferenceEquals(f.Scope("emby"), actions.Navigation.Single().Scope));
                        Assert.Equal(1, f.Transport.Writes);
                        Assert.Equal("r029-emby.invalid", f.Transport.Requests.Last().Host);
                    }
                    Assert.Equal(0, actions.Messages.Count);
                }
                finally { ((IDisposable)vm).Dispose(); }
            });
        Case("R-029 详情切回受支持scope但新详情未到时，旧Plex条目不能借新源写入", async () =>
        {
            using var f = New();
            var actions = _shell.Actions();
            var vm = await Detail(f, "plex", EmbyItemType.Movie, actions);
            try
            {
                _shell.Attach(vm, f, f.Item("emby"), actions);
                var before = f.Transport.Requests.Count;
                await (Task)ShellAssembly.Invoke(vm, "ToggleFavoriteAsync")!;
                Assert.Equal(before, f.Transport.Requests.Count);
                Assert.Equal(0, f.Transport.Writes);
                Assert.Equal(0, actions.Messages.Count);
                Assert.Equal(0, actions.FavoriteRefreshes.Count);
            }
            finally { ((IDisposable)vm).Dispose(); }
        });
    }

    private static void RegisterGenres()
    {
        Case("R-030 真Plex详情题材文字可读，无Hyperlink且旧入口零导航零请求", async () =>
        {
            using var f = New();
            var actions = _shell.Actions();
            var vm = await Detail(f, "plex", EmbyItemType.Movie, actions);
            using var page = new GenrePageContract(vm);
            try
            {
                page.Paint();
                Assert.Equal("动画  ·  剧情", page.Text);
                Assert.Equal(0, page.Links.Count);
                var read = page.Read();
                Assert.True(read.Ok, read.Detail);
                var before = f.Transport.Requests.Count;
                ShellAssembly.Invoke(vm, "OpenGenre", "动画");
                Assert.Equal(0, actions.Navigation.Count);
                Assert.Equal(before, f.Transport.Requests.Count);
                Assert.Equal(0, actions.Messages.Count);
            }
            finally { ((IDisposable)vm).Dispose(); }
        });
        foreach (var source in new[] { "emby", "jellyfin" })
            Case($"R-030 真{source}详情链接仍导航原scope并保留跨库路由", async () =>
            {
                using var f = New();
                f.Default("plex");
                var actions = _shell.Actions();
                var vm = await Detail(f, source, EmbyItemType.Movie, actions);
                using var page = new GenrePageContract(vm);
                try
                {
                    page.Paint();
                    Assert.Equal(2, page.Links.Count);
                    Assert.True(page.Read().Ok, page.Read().Detail);
                    page.Links[0].ClickForFixture();
                    var nav = actions.Navigation.Single();
                    Assert.Equal("动画", nav.Genre);
                    Assert.True(ReferenceEquals(f.Scope(source), nav.Scope));
                    var request = _shell.GenreRequest(nav.Genre, nav.Scope);
                    Assert.Equal("动画", ShellAssembly.Get<string>(request, "Genre"));
                    Assert.True(ReferenceEquals(f.Scope(source), ShellAssembly.Get<EmbySessionScope>(request, "Source")));
                    Assert.Null(ShellAssembly.Get<object?>(request, "ParentId"), "不随意填某个库来伪装跨库支持");
                    await nav.Scope!.ExecuteAsync((client, token) => client.GetItemsAsync(new ItemQuery { Genre = nav.Genre }, token), default);
                    Assert.Equal("r029-" + source + ".invalid", f.Transport.Requests.Last().Host);
                }
                finally { ((IDisposable)vm).Dispose(); }
            });
        Case("R-030 同详情scope切换但题材集合不变，实时重画／旧链接守卫／重入恢复", async () =>
        {
            using var f = New();
            var actions = _shell.Actions();
            var vm = await Detail(f, "emby", EmbyItemType.Movie, actions);
            using var page = new GenrePageContract(vm);
            try
            {
                page.Paint();
                var old = page.Links[0];
                var genres = ShellAssembly.Get<IReadOnlyList<string>>(vm, "SublineGenres");
                _shell.Attach(vm, f, f.Item("plex"), actions);
                Assert.True(ReferenceEquals(genres, ShellAssembly.Get<IReadOnlyList<string>>(vm, "SublineGenres")));
                Assert.Equal(0, page.Links.Count, "只有能力变时也得重画，不能等题材setter");
                Assert.Equal("动画  ·  剧情", page.Text);
                old.ClickForFixture();
                Assert.Equal(0, actions.Navigation.Count);
                Assert.Equal(0, actions.Messages.Count);
                ShellAssembly.Invoke(vm, "Cancel");
                old.ClickForFixture();
                Assert.Equal(0, actions.Navigation.Count);
                _shell.Attach(vm, f, f.Item("jellyfin"), actions);
                Assert.Equal(2, page.Links.Count);
                page.Links[1].ClickForFixture();
                Assert.Equal("剧情", actions.Navigation.Single().Genre);
                Assert.True(ReferenceEquals(f.Scope("jellyfin"), actions.Navigation.Single().Scope));
                Assert.Equal(0, f.Transport.Writes);
            }
            finally { ((IDisposable)vm).Dispose(); }
        });
        Case("R-030 来源撤销或详情Cancel后，旧题材事件不借默认源导航", async () =>
        {
            using var f = New();
            var actions = _shell.Actions();
            var vm = await Detail(f, "emby", EmbyItemType.Movie, actions);
            using var page = new GenrePageContract(vm);
            try
            {
                page.Paint();
                var old = page.Links[0];
                f.Revoke("emby");
                f.Default("jellyfin");
                var before = f.Transport.Requests.Count;
                old.ClickForFixture();
                ShellAssembly.Invoke(vm, "Cancel");
                old.ClickForFixture();
                Assert.Equal(0, actions.Navigation.Count);
                Assert.Equal(before, f.Transport.Requests.Count);
            }
            finally { ((IDisposable)vm).Dispose(); }
        });
        Case("R-030 页面离开取消订阅后重入，详情读取失败仍立即去掉旧Plex链接", async () =>
        {
            using var f = New();
            var actions = _shell.Actions();
            var vm = await Detail(f, "emby", EmbyItemType.Movie, actions);
            using var page = new GenrePageContract(vm);
            try
            {
                page.Paint();
                var old = page.Links[0];
                var genres = ShellAssembly.Get<IReadOnlyList<string>>(vm, "SublineGenres");
                page.Leave();
                f.Transport.FailDetailRead = true;
                page.Enter(_shell.Request(f.Item("plex")), actions, new FixtureServices(f.Settings), f.Session, f.Images);
                await page.ViewModel.LastLoad;
                Assert.True(ReferenceEquals(genres, ShellAssembly.Get<IReadOnlyList<string>>(vm, "SublineGenres")));
                Assert.True(ShellAssembly.Get<bool>(vm, "NoticeOpen"), "真实Load失败，不能借题材setter重画");
                Assert.Equal(0, page.Links.Count, "实际进入尾段必须在重新订阅后显式刷新题材能力");
                Assert.Equal("动画  ·  剧情", page.Text);
                Assert.True(page.PaintStateReset);
                old.ClickForFixture();
                Assert.Equal(0, actions.Navigation.Count);
                page.Leave();
                f.Transport.FailDetailRead = false;
                page.Enter(_shell.Request(f.Item("emby")), actions, new FixtureServices(f.Settings), f.Session, f.Images);
                await page.ViewModel.LastLoad;
                Assert.Equal(2, page.Links.Count);
                page.Links[0].ClickForFixture();
                Assert.True(ReferenceEquals(f.Scope("emby"), actions.Navigation.Single().Scope));
                Assert.Equal(0, f.Transport.Writes);
            }
            finally { ((IDisposable)vm).Dispose(); }
        });
        foreach (var defect in new[] { "文字错误", "残留链接", "隐藏题材行" })
            Case("R-030 真实GenreRead不把只读题材缺陷洗绿：" + defect, async () =>
            {
                using var f = New();
                var actions = _shell.Actions();
                var vm = await Detail(f, "plex", EmbyItemType.Movie, actions);
                using var page = new GenrePageContract(vm);
                try
                {
                    page.Paint();
                    if (defect == "文字错误") page.GenreLine.Inlines.OfType<Microsoft.UI.Xaml.Documents.Run>().First().Text = "错误题材";
                    else if (defect == "残留链接") page.GenreLine.Inlines.Add(new Microsoft.UI.Xaml.Documents.Hyperlink());
                    else page.GenreLine.Visibility = Visibility.Collapsed;
                    Assert.False(page.Read().Ok, "真实读数必须保留失败判断，不改基线或把失败降级成信息");
                }
                finally { ((IDisposable)vm).Dispose(); }
            });
        Case("R-030 无题材详情不制造空链接区域，真读取仍通过", async () =>
        {
            using var f = New();
            var actions = _shell.Actions();
            var vm = _shell.NewViewModel();
            _shell.Attach(vm, f, f.Item("plex", genres: false), actions);
            await (Task)ShellAssembly.Invoke(vm, "ReloadAsync")!;
            using var page = new GenrePageContract(vm);
            try
            {
                page.Paint();
                Assert.Equal("", page.Text);
                Assert.Equal(0, page.Links.Count);
                Assert.Equal("Collapsed", ShellAssembly.Get<object>(vm, "GenreVisibility").ToString());
                Assert.True(page.Read().Ok);
            }
            finally { ((IDisposable)vm).Dispose(); }
        });
    }
}
