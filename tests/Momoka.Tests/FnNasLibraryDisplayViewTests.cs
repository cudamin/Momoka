using System.Reflection;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;
using static Momoka.Tests.LibraryResumePagingTests;

namespace Momoka.Tests;

internal static class FnNasLibraryDisplayViewTests
{
    internal static int Run(string shellPath, string root)
    {
        var shell = new ShellFixture(shellPath, root);
        Test("飞牛布局实际VM：Other有列表且换布局后单列，电影电视不接受列表", () =>
        {
            using var fixture = New(shell);
            foreach (var collection in new[] { "homevideos", "movies", "tvshows" })
            {
                var vm = fixture.CreateLibraryView(collectionType: collection);
                try
                {
                    var options = Views(shell, vm);
                    Assert.Equal(collection == "homevideos" ? "Poster,Thumb,List" : "Poster,Thumb", string.Join(',', options));
                    Choose(shell, vm, LibraryView.List);
                    Assert.Equal(collection == "homevideos" ? LibraryView.List : LibraryView.Poster, shell.Get<LibraryView>(vm, "View"));
                    if (collection == "homevideos") Assert.Equal(1, shell.Get<int>(vm, "GridColumns"));
                }
                finally { ((IDisposable)vm).Dispose(); }
            }
        });
        Test("飞牛布局实际VM：目录按身份父ID记忆且重新进入恢复列表", () =>
        {
            using var fixture = New(shell);
            var vm = fixture.CreateLibraryView(collectionType: null, parentType: EmbyItemType.Folder);
            var key = BrowsingPreferences.LibraryKey(fixture.Source("a").Connection, "library");
            try
            {
                Assert.Equal("Poster,List", string.Join(',', Views(shell, vm)));
                Choose(shell, vm, LibraryView.List);
                Assert.True(fixture.Settings.Settings.Ui.Display.ContainsKey(key), "目录首选布局必须保存");
                Assert.Equal(LibraryView.List, fixture.Settings.Settings.Ui.Display[key].View);
            }
            finally { ((IDisposable)vm).Dispose(); }
            var returned = fixture.CreateLibraryView(collectionType: null, parentType: EmbyItemType.Folder);
            try { Assert.Equal(LibraryView.List, shell.Get<LibraryView>(returned, "View")); }
            finally { ((IDisposable)returned).Dispose(); }
        });
        Test("飞牛布局实际VM：横幅旧偏好进列表，条件失效回横幅并恢复首选列表", () =>
        {
            using var fixture = New(shell);
            var key = BrowsingPreferences.LibraryKey(fixture.Source("a").Connection, "library");
            fixture.Settings.Settings.Ui.Views[key] = LibraryView.Thumb;
            var first = fixture.CreateLibraryView(collectionType: "homevideos");
            try
            {
                Assert.Equal(LibraryView.Thumb, shell.Get<LibraryView>(first, "View"));
                Choose(shell, first, LibraryView.List);
            }
            finally { ((IDisposable)first).Dispose(); }
            var restricted = fixture.CreateLibraryView(collectionType: "movies");
            try
            {
                Assert.Equal(LibraryView.Thumb, shell.Get<LibraryView>(restricted, "View"));
                Assert.Equal(LibraryView.List, fixture.Settings.Settings.Ui.Display[key].View);
                Assert.Equal("Poster,Thumb", string.Join(',', Views(shell, restricted)));
            }
            finally { ((IDisposable)restricted).Dispose(); }
            var restored = fixture.CreateLibraryView(collectionType: "homevideos");
            try { Assert.Equal(LibraryView.List, shell.Get<LibraryView>(restored, "View")); }
            finally { ((IDisposable)restored).Dispose(); }
        });
        Test("飞牛布局实际VM：来源失效隐藏菜单并拒绝改变已存偏好", () =>
        {
            using var fixture = New(shell);
            var vm = fixture.CreateLibraryView(collectionType: "homevideos");
            try
            {
                Choose(shell, vm, LibraryView.List);
                var key = BrowsingPreferences.LibraryKey(fixture.Source("a").Connection, "library");
                fixture.RevokeHomeSource("a");
                Assert.Equal("Collapsed", shell.Get<object>(vm, "LayoutVisibility").ToString());
                Assert.Equal(0, Views(shell, vm).Count);
                Choose(shell, vm, LibraryView.Poster);
                Assert.Equal(LibraryView.List, fixture.Settings.Settings.Ui.Display[key].View);
            }
            finally { ((IDisposable)vm).Dispose(); }
        });
        Test("飞牛布局实际VM：同页类型改变通知实际布局，暂时回退后原列表仍恢复", () =>
        {
            using var fixture = New(shell);
            var vm = fixture.CreateLibraryView(collectionType: "homevideos");
            try
            {
                Choose(shell, vm, LibraryView.List);
                var item = fixture.Source("a").Bind(new EmbyItem { Id = "card", Name = "卡片", Type = EmbyItemType.Video });
                var listCard = shell.Invoke(vm, "NewCard", item)!;
                var cards = (System.Collections.IList)shell.Get<object>(vm, "Cards");
                cards.Add(listCard);
                var changes = new List<string?>();
                ((System.ComponentModel.INotifyPropertyChanged)vm).PropertyChanged += (_, args) => changes.Add(args.PropertyName);
                var request = vm.GetType().GetField("_request", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)!;
                shell.Set(request, "CollectionType", "movies");
                shell.Invoke(vm, "RefreshBrowseChrome");
                Assert.Equal(LibraryView.Poster, shell.Get<LibraryView>(vm, "View"));
                Assert.True(changes.Contains("View"), "同页条件变化必须通知真实布局切回");
                Assert.Equal(Math.Round(shell.Get<double>(cards[0]!, "CardWidth") * 1.5), shell.Get<double>(cards[0]!, "PosterHeight"),
                    "回退海报必须更新实际卡片的图片比例");
                shell.Set(request, "CollectionType", "homevideos");
                shell.Invoke(vm, "RefreshBrowseChrome");
                Assert.Equal(LibraryView.List, shell.Get<LibraryView>(vm, "View"));
                Assert.Equal(Math.Round(shell.Get<double>(cards[0]!, "CardWidth") * 9d / 16d), shell.Get<double>(cards[0]!, "PosterHeight"),
                    "恢复列表必须更新实际卡片的图片比例");
            }
            finally { ((IDisposable)vm).Dispose(); }
        });
        Test("飞牛布局实际VM：释放与重新激活会通知菜单隐藏和恢复", () =>
        {
            using var fixture = New(shell);
            var vm = fixture.CreateLibraryView(collectionType: "homevideos");
            try
            {
                var changes = new List<string?>();
                ((System.ComponentModel.INotifyPropertyChanged)vm).PropertyChanged += (_, args) => changes.Add(args.PropertyName);
                shell.Invoke(vm, "Cancel");
                Assert.Equal("Collapsed", shell.Get<object>(vm, "LayoutVisibility").ToString());
                Assert.True(changes.Contains("LayoutVisibility"), "释放必须通知布局菜单收起");
                changes.Clear();
                shell.Invoke(vm, "Resume", false);
                Assert.Equal("Visible", shell.Get<object>(vm, "LayoutVisibility").ToString());
                Assert.True(changes.Contains("LayoutVisibility"), "返回必须通知布局菜单恢复");
            }
            finally { ((IDisposable)vm).Dispose(); }
        });
        return TestHarness.Run();
    }

    private static Fixture New(ShellFixture shell)
    {
        var fixture = new Fixture(shell, 1, firstKind: MediaServerKind.FnNas);
        fixture.Transport.FnNasHomeReply = uri => Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StringContent(uri.AbsolutePath.EndsWith("/item/list", StringComparison.Ordinal)
                ? "{\"code\":0,\"data\":{\"total\":1,\"list\":[{\"guid\":\"video\",\"title\":\"视频\",\"type\":\"Video\"}]}}"
                : "{\"code\":0,\"data\":[]}")
        });
        return fixture;
    }

    private static IReadOnlyList<LibraryView> Views(ShellFixture shell, object vm)
    {
        var method = vm.GetType().GetMethod("FnNasViews", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.True(method is not null, "缺少按飞牛页面条件生成布局选项的入口");
        return (IReadOnlyList<LibraryView>)method!.Invoke(vm, null)!;
    }

    private static void Choose(ShellFixture shell, object vm, LibraryView view)
    {
        var method = vm.GetType().GetMethod("ChooseFnNasView", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.True(method is not null, "缺少飞牛列表布局选择入口");
        method!.Invoke(vm, [view]);
    }
}
