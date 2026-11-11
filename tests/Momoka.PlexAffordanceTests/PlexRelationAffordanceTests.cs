using Microsoft.UI.Xaml.Documents;
using Momoka.Emby;
using Momoka.Plex;

namespace Momoka.Tests;

internal static partial class PlexAffordanceTests
{
    private static void RegisterPlexRelations()
    {
        foreach (var nextSource in new[] { "emby", "plex" })
            Case("Plex关联：新详情读取完成后旧链接仍不能借新访问执行 " + nextSource, async () =>
            {
                using var f = New();
                f.Transport.PlexRelations = true;
                var actions = _shell.Actions();
                var vm = await Detail(f, "plex", EmbyItemType.Movie, actions);
                using var page = new GenrePageContract(vm);
                try
                {
                    page.Paint();
                    var old = page.Links.Single();
                    f.Transport.PlexRelationSection = "8";
                    f.Transport.PlexRelationGenreId = "99";
                    _shell.Attach(vm, f, f.Item(nextSource, id: "next-detail"), actions);
                    await (Task)ShellAssembly.Invoke(vm, "ReloadAsync")!;
                    page.Paint();
                    old.ClickForFixture();
                    Assert.Equal(0, actions.ItemsNavigation.Count);
                    Assert.Equal(0, actions.Navigation.Count);
                    var current = page.Links[0];
                    current.ClickForFixture();
                    if (nextSource == "plex")
                        Assert.Equal(new PlexBrowseRelation("8", 1, "genre", "99"), actions.ItemsNavigation.Single().PlexRelation);
                    else Assert.True(ReferenceEquals(f.Scope("emby"), actions.Navigation.Single().Scope));
                    ShellAssembly.Invoke(vm, "Cancel");
                    current.ClickForFixture();
                    Assert.Equal(1, actions.ItemsNavigation.Count + actions.Navigation.Count);
                }
                finally { ((IDisposable)vm).Dispose(); }
            });
        Case("Plex关联：有服务器id的题材才可点，来源切换和取消后旧链接拒绝", async () =>
        {
            using var f = New();
            f.Transport.PlexRelations = true;
            var actions = _shell.Actions();
            var vm = await Detail(f, "plex", EmbyItemType.Movie, actions);
            using var page = new GenrePageContract(vm);
            try
            {
                page.Paint();
                Assert.Equal("动画  ·  剧情", page.Text);
                Assert.Equal(1, page.Links.Count);
                Assert.True(page.Read().Ok, page.Read().Detail);
                var old = page.Links.Single();
                old.ClickForFixture();
                var navigation = actions.ItemsNavigation.Single();
                Assert.True(ReferenceEquals(f.Scope("plex"), navigation.Source));
                Assert.Equal(new PlexBrowseRelation("3", 1, "genre", "41"), navigation.PlexRelation);
                var request = _shell.RelationRequest(navigation);
                Assert.Equal("动画 · 来源库 3", ShellAssembly.Get<string>(request, "Title"));
                var query = _shell.RelationQuery(request, 12, 7);
                Assert.Equal(new PlexBrowseRelation("3", 1, "genre", "41"), query.PlexRelation);
                Assert.Equal("3", query.ParentId);
                Assert.Equal(12, query.StartIndex);
                Assert.Equal(7, query.Limit);
                ShellAssembly.Invoke(vm, "OpenGenre", "剧情");
                Assert.Equal(1, actions.ItemsNavigation.Count);
                _shell.Attach(vm, f, f.Item("emby"), actions);
                old.ClickForFixture();
                Assert.Equal(1, actions.ItemsNavigation.Count);
                Assert.Equal(0, actions.Navigation.Count);
                ShellAssembly.Invoke(vm, "Cancel");
                old.ClickForFixture();
                Assert.Equal(1, actions.ItemsNavigation.Count);
            }
            finally { ((IDisposable)vm).Dispose(); }
        });
        Case("Plex关联：演员导演编剧可读，缺身份卡没有导航且旧来源被拒绝", async () =>
        {
            using var f = New();
            f.Transport.PlexRelations = true;
            var actions = _shell.Actions();
            var vm = await Detail(f, "plex", EmbyItemType.Movie, actions);
            try
            {
                var item = ShellAssembly.Get<EmbyItem>(vm, "CurrentItem");
                var cast = ItemDetail.Cast(item);
                Assert.Equal(4, cast.Count);
                var shelf = ShellAssembly.Get<object>(vm, "CastShelf");
                var cards = ((System.Collections.IEnumerable)ShellAssembly.Get<object>(shelf, "Cards")).Cast<object>().ToArray();
                var card = cards[0];
                ShellAssembly.Invoke(vm, "OpenCard", card);
                Assert.Equal(new PlexBrowseRelation("3", 1, "actor", "42"), actions.ItemsNavigation.Single().PlexRelation);
                var display = f.Card(cast[1].Card);
                Assert.Equal("Collapsed", display.PersonNavigationVisibility.ToString());
                Assert.Equal("Visible", display.PersonDisplayVisibility.ToString());
                ShellAssembly.Invoke(vm, "OpenCard", cards[1]);
                Assert.Equal(1, actions.ItemsNavigation.Count);
                _shell.Attach(vm, f, f.Item("emby"), actions);
                ShellAssembly.Invoke(vm, "OpenCard", card);
                Assert.Equal(1, actions.ItemsNavigation.Count);
                Assert.Equal(0, f.Transport.Writes);
            }
            finally { ((IDisposable)vm).Dispose(); }
        });
    }
}
