using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Interop;
using Momoka.Shell.Media;
using Momoka.Shell.Views;
using Microsoft.UI.Input;

namespace Momoka.Tests;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Run)> Cases = [];

    private static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("实际LibraryPreview完整partial与PosterHoverDwell；时间/指针/UI/播放为惰性边界，HTTP仅内存匿名响应");
        RegisterInputTests();
        RegisterPlaybackTests();
        RegisterOwnershipTests();
        RegisterProbeTests();
        RegisterWiringTests();
        var failures = 0;
        foreach (var (name, run) in Cases)
        {
            try { await run(); Console.WriteLine("  通过  " + name); }
            catch (Exception error) { failures++; Console.WriteLine("  失败  " + name + "\n" + error); }
        }
        Console.WriteLine($"共 {Cases.Count} 项，通过 {Cases.Count - failures}，失败 {failures}，跳过 0");
        return failures == 0 ? 0 : 1;
    }

    private static void Test(string name, Action action) => Cases.Add((name, () => { action(); return Task.CompletedTask; }));
    private static void TestAsync(string name, Func<Task> action) => Cases.Add((name, action));

    private static void RegisterInputTests()
    {
        Test("实际计时器接线：连续静止499ms不显示，500ms显示", () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell; var a = f.Anchors.Movies;
            s.At(a, 0); s.Enter(a); Assert.True(s.OpenTimer.IsRunning);
            Assert.Equal(500d, s.OpenTimer.Interval.TotalMilliseconds);
            s.At(a, 499); s.OpenTimer.Fire(); Assert.False(s.Visible);
            Assert.Equal(1d, s.OpenTimer.Interval.TotalMilliseconds);
            s.At(a, 500); s.OpenTimer.Fire(); Assert.True(s.Visible);
        });
        Test("真正移动重计时，同坐标事件保留剩余时间", () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell; var a = f.Anchors.Movies;
            s.At(a, 0); s.Enter(a);
            var starts = s.OpenTimer.Starts;
            s.At(a, 300); s.Move(a); Assert.Equal(starts, s.OpenTimer.Starts);
            s.At(a, 400, x: 21); s.Move(a); Assert.Equal(starts + 1, s.OpenTimer.Starts);
            s.At(a, 500, x: 21); s.OpenTimer.Fire(); Assert.False(s.Visible);
            Assert.Equal(400d, s.OpenTimer.Interval.TotalMilliseconds);
            s.At(a, 899, x: 21); s.OpenTimer.Fire(); Assert.False(s.Visible);
            s.At(a, 900, x: 21); s.OpenTimer.Fire(); Assert.True(s.Visible);
        });
        Test("旧tick读到尚未送达的鼠标移动后重新等半秒", () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell; var a = f.Anchors.Movies;
            s.At(a, 0); s.Enter(a);
            s.At(a, 500, x: 22); s.OpenTimer.Fire(); Assert.False(s.Visible);
            s.At(a, 999, x: 22); s.OpenTimer.Fire(); Assert.False(s.Visible);
            s.At(a, 1000, x: 22); s.OpenTimer.Fire(); Assert.True(s.Visible);
        });
        Test("触摸与笔不启动鼠标静止预览", () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell; var a = f.Anchors.Movies;
            s.At(a, 0); s.Enter(a, PointerDeviceType.Touch); s.Move(a, PointerDeviceType.Pen);
            Assert.False(s.OpenTimer.IsRunning); s.At(a, 1000); s.OpenTimer.Fire(); Assert.False(s.Visible);
        });
        Test("原生按下路由取消预览，后续Move与同位置Exit/Enter保持抑制", () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell; var a = f.Anchors.Movies;
            s.At(a, 0); s.Enter(a); s.At(a, 300); s.PressSidebar();
            Assert.True(s.Suppressed(a)); Assert.False(s.OpenTimer.IsRunning);
            s.Exit(a); s.Enter(a); s.At(a, 800, x: 21); s.Move(a); s.OpenTimer.Fire();
            Assert.True(s.Suppressed(a)); Assert.False(s.Visible);
        });
        Test("导航调用也抑制尚未Enter的库，真正离开重入才恢复500ms", () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell; var a = f.Anchors.Movies;
            s.At(a, 0); s.InvokeNavigation(a); s.Enter(a);
            s.At(a, 1000); s.OpenTimer.Fire(); Assert.False(s.Visible);
            Native.PointerPosition = new NativePoint { X = 100, Y = 100 }; s.Exit(a);
            Assert.False(s.Suppressed(a));
            s.At(a, 1100); s.Enter(a); s.At(a, 1599); s.OpenTimer.Fire(); Assert.False(s.Visible);
            s.At(a, 1600); s.OpenTimer.Fire(); Assert.True(s.Visible);
        });
        Test("鼠标在栏外的键盘导航不留下抑制，第一次Enter正常等500ms", () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell; var a = f.Anchors.Movies;
            Native.PointerPosition = new NativePoint { X = 200, Y = 200 };
            s.InvokeNavigation(a); Assert.False(s.Suppressed(a));
            s.At(a, 1000); s.Enter(a); s.At(a, 1499); s.OpenTimer.Fire(); Assert.False(s.Visible);
            s.At(a, 1500); s.OpenTimer.Fire(); Assert.True(s.Visible);
        });
        Test("按着鼠标移动只抑制，不重新启动静止计时", () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell; var a = f.Anchors.Movies;
            s.At(a, 0); s.Move(a, pressed: true); Assert.True(s.Suppressed(a));
            s.At(a, 500); s.Move(a); s.OpenTimer.Fire(); Assert.False(s.Visible);
        });
        Test("tick发现指针已离开、读指针失败或侧栏展开时取消", () =>
        {
            foreach (var reason in new[] { "leave", "unavailable", "expanded", "hidden" })
            {
                using var f = new AnonymousFixture(); var s = f.Shell; var a = f.Anchors.Movies;
                s.At(a, 0); s.Enter(a); PreviewClock.Now = 500;
                if (reason == "leave") Native.PointerPosition = new NativePoint { X = 100, Y = 100 };
                if (reason == "unavailable") Native.PointerAvailable = false;
                if (reason == "expanded") s.Sidebar.IsPaneOpen = true;
                if (reason == "hidden") s.SidebarShown = false;
                s.OpenTimer.Fire(); Assert.False(s.Visible, reason); Assert.False(s.OpenTimer.IsRunning, reason);
            }
        });
        TestAsync("跨库候选不改已显示tag，500ms后才换整块预览", async () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell; var a = f.Anchors.Movies; var b = f.Anchors.Series;
            await s.Show(a); Assert.Equal(a.Tag, s.DisplayedTag);
            s.At(b, 0); s.Enter(b);
            Assert.Equal(a.Tag, s.DisplayedTag); Assert.True(ReferenceEquals(a, s.DisplayedAnchor));
            s.At(b, 499); s.OpenTimer.Fire(); Assert.Equal(a.Tag, s.DisplayedTag);
            s.At(b, 500); s.OpenTimer.Fire(); Assert.Equal(b.Tag, s.DisplayedTag);
            Assert.Equal("匿名剧集库", s.LibraryPreviewTitle.Text);
        });
        TestAsync("查看更多使用当前显示库，悬停候选不会偷换目的地", async () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell; var a = f.Anchors.Movies; var b = f.Anchors.Series;
            await s.Show(a); s.At(b, 0); s.Enter(b); s.More();
            Assert.Equal((string)a.Tag!, s.Navigated.Single()); Assert.False(s.Visible);
            Assert.False(s.Suppressed(a)); Assert.True(s.Suppressed(b));
        });
        TestAsync("从图标经过缝隙进面板取消关闭，移出面板才收起", async () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell; var a = f.Anchors.Movies;
            await s.Show(a); Native.PointerPosition = new NativePoint { X = 60, Y = 100 }; s.Exit(a);
            Assert.True(s.CloseTimer.IsRunning); s.EnterPanel(); Assert.False(s.CloseTimer.IsRunning);
            s.ExitPanel(); Assert.True(s.CloseTimer.IsRunning); s.CloseTimer.Fire(); Assert.False(s.Visible);
        });
        TestAsync("面板按钮捕获生成的同坐标Exit不启动收起", async () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell;
            await s.Show(f.Anchors.Movies);
            Native.PointerPosition = new NativePoint
            {
                X = (int)s.LibraryPreviewPanel.Margin.Left + 20,
                Y = (int)s.LibraryPreviewPanel.Margin.Top + 20
            };
            s.ExitPanel(); Assert.False(s.CloseTimer.IsRunning); Assert.True(s.Visible);
        });
        Test("关闭及库重建后迟到tick不能复活旧预览", () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell; var a = f.Anchors.Movies;
            s.At(a, 0); s.Enter(a); s.Hide(); s.At(a, 500); s.OpenTimer.Fire(); Assert.False(s.Visible);
            s.At(a, 600); s.Enter(a); s.RebuildBoundary(); s.At(a, 1100); s.OpenTimer.Fire(); Assert.False(s.Visible);
        });
    }

    private static void RegisterPlaybackTests()
    {
        foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Episode, EmbyItemType.Video })
            TestAsync("原生Button Click直接交给既有PlayAsync且先收起/" + type, async () =>
            {
                using var f = new AnonymousFixture(); var s = f.Shell; var a = f.Anchors.Movies;
                f.Http.Items["movies"] = [AnonymousFixture.Media("play", type)];
                s.At(a, 0);
                await s.Show(a); var button = (Button)s.LibraryPreviewCovers.Children.Single();
                Assert.Equal("SidebarLibraryPreviewPlay_0", button.AutomationId);
                Assert.Contains("播放：匿名媒体 play", button.AutomationName);
                Assert.Equal(Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Control, button.AccessibilityView);
                var item = (EmbyItem)button.Tag!; button.Invoke();
                Assert.True(ReferenceEquals(item, s.Played.Single())); Assert.Equal(type, s.Played.Single().Type);
                Assert.True(ReferenceEquals(item.Source?.UserStateOwner, f.Source.UserStateOwner));
                Assert.True(s.PreviewWasClosedOnPlay); Assert.False(s.Visible); Assert.True(s.Suppressed(a));
            });
        TestAsync("不可播放与展示条目保留普通封面，不创建无效播放Button", async () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell; var a = f.Anchors.Movies;
            var display = AnonymousFixture.Media("display", EmbyItemType.Movie); display.IsDisplayOnly = true;
            f.Http.Items["movies"] = [AnonymousFixture.Media("folder", EmbyItemType.Folder), AnonymousFixture.Media("person", EmbyItemType.Person), display];
            // IsDisplayOnly is local presentation state, not serialized by the server.
            await s.Show(a);
            Assert.True(s.LibraryPreviewCovers.Children.Take(2).All(child => child is Border));
            var frame = s.Create(display, s.CurrentLoad!).Frame; Assert.True(frame is Border);
            Assert.Equal(0, s.Played.Count);
        });
        TestAsync("切换默认来源后封面仍使用原账号，不回退当前账号", async () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell; var a = f.Anchors.Movies;
            await s.Show(a); var button = (Button)s.LibraryPreviewCovers.Children.First();
            var other = f.AddSource("source-b"); Assert.True(f.Session.SelectSource(other));
            Assert.True(f.Source.IsAvailable); button.Invoke();
            Assert.True(ReferenceEquals(s.Played.Single().Source?.UserStateOwner, f.Source.UserStateOwner));
        });
        TestAsync("失效来源Button禁止调用播放", async () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell;
            await s.Show(f.Anchors.Movies); var button = (Button)s.LibraryPreviewCovers.Children.First();
            f.Session.SignOut(); Assert.False(f.Source.IsAvailable); button.Invoke(); Assert.Equal(0, s.Played.Count);
        });
        TestAsync("关闭、替换库或移出Children后的旧Button不得播放", async () =>
        {
            foreach (var action in new[] { "hide", "replace", "remove", "rebuild" })
            {
                using var f = new AnonymousFixture(); var s = f.Shell;
                await s.Show(f.Anchors.Movies); var button = (Button)s.LibraryPreviewCovers.Children.First();
                if (action == "hide") s.Hide();
                if (action == "replace") await s.Show(f.Anchors.Series);
                if (action == "remove") s.LibraryPreviewCovers.Children.Remove(button);
                if (action == "rebuild") s.RebuildBoundary();
                button.Invoke(); Assert.Equal(0, s.Played.Count, action);
            }
        });
    }

    private static void RegisterOwnershipTests()
    {
        TestAsync("查询在飞时关闭会取消load，晚返不能恢复封面", async () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell;
            var reply = new TaskCompletionSource<System.Net.Http.HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken requestToken = default;
            System.Net.Http.HttpRequestMessage? held = null;
            f.Http.Override = (request, token) => { held = request; requestToken = token; return reply.Task; };
            var showing = s.Show(f.Anchors.Movies); var load = s.CurrentLoad!;
            Assert.True(s.Visible); Assert.True(s.LibraryPreviewLoading.IsActive);
            s.Hide(); Assert.True(load.IsCancellationRequested); Assert.True(requestToken.IsCancellationRequested);
            reply.SetResult(f.Http.Reply(held!)); await showing;
            Assert.False(s.Visible); Assert.Equal(0, s.LibraryPreviewCovers.Children.Count);
        });
        TestAsync("旧库查询晚返不能覆盖新库标题、tag或封面", async () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell;
            var reply = new TaskCompletionSource<System.Net.Http.HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            System.Net.Http.HttpRequestMessage? held = null;
            f.Http.Override = (request, _) => { held = request; return reply.Task; };
            var old = s.Show(f.Anchors.Movies); var oldLoad = s.CurrentLoad!;
            f.Http.Override = null; await s.Show(f.Anchors.Series); Assert.True(oldLoad.IsCancellationRequested);
            reply.SetResult(f.Http.Reply(held!)); await old;
            Assert.Equal(f.Anchors.Series.Tag, s.DisplayedTag); Assert.Equal("匿名剧集库", s.LibraryPreviewTitle.Text);
            Assert.True(s.LibraryPreviewCovers.Children.OfType<Button>().All(button => ((EmbyItem)button.Tag!).Id != "movie-a"));
        });
        TestAsync("图片解码在飞时关闭取消且晚返不会填回已释放Image", async () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell;
            await s.Show(f.Anchors.Movies); var button = (Button)s.LibraryPreviewCovers.Children.First();
            var grid = (Grid)button.Content!; var picture = grid.Children.OfType<Image>().Single();
            var placeholder = grid.Children.OfType<FontIcon>().Single(); var item = (EmbyItem)button.Tag!;
            var decode = new TaskCompletionSource<Bitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var decoding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            PosterLoader.Decode = (_, _) => { decoding.TrySetResult(); return decode.Task; };
            var load = s.CurrentLoad!; var loading = s.LoadImage(picture, placeholder, item, f.Source, load, f.Images());
            await decoding.Task.WaitAsync(TimeSpan.FromSeconds(5));
            s.Hide(); Assert.True(load.IsCancellationRequested); decode.SetResult(new()); await loading;
            Assert.Null(picture.Source); Assert.Equal(Visibility.Collapsed, picture.Visibility);
        });
        TestAsync("未取消但已换属主的图片返回也不能填回旧Image", async () =>
        {
            using var f = new AnonymousFixture(); var s = f.Shell;
            await s.Show(f.Anchors.Movies); var button = (Button)s.LibraryPreviewCovers.Children.First();
            var grid = (Grid)button.Content!; var picture = grid.Children.OfType<Image>().Single();
            var placeholder = grid.Children.OfType<FontIcon>().Single(); var item = (EmbyItem)button.Tag!;
            var decode = new TaskCompletionSource<Bitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var decoding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            PosterLoader.Decode = (_, _) => { decoding.TrySetResult(); return decode.Task; };
            using var old = new CancellationTokenSource(); s.ReplaceLoadBoundary(old);
            var loading = s.LoadImage(picture, placeholder, item, f.Source, old, f.Images());
            await decoding.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var current = new CancellationTokenSource(); s.ReplaceLoadBoundary(current);
            Assert.False(old.IsCancellationRequested); decode.SetResult(new()); await loading;
            Assert.Null(picture.Source); Assert.False(s.IsCurrent(old)); Assert.True(s.IsCurrent(current));
            s.Hide();
        });
    }

    private static void RegisterProbeTests()
    {
        TestAsync("实际探针检查原生封面按钮：正向通过且反例逐项拒绝", async () =>
        {
            foreach (var defect in new[] { "valid", "zero", "two", "name", "id", "tag", "invoke" })
            {
                using var f = new AnonymousFixture(); var s = f.Shell;
                await s.Show(f.Anchors.Movies); var grid = s.LibraryPreviewCovers;
                var button = (Button)grid.Children.First();
                if (defect == "zero") grid.Children.Clear();
                if (defect == "two") grid.Children.RemoveAt(2);
                if (defect == "name") button.AutomationName = "错误名称";
                if (defect == "id") button.AutomationId = "WrongId";
                if (defect == "tag") button.Tag = new object();
                if (defect == "invoke") button.InvokeAvailable = false;
                if (defect == "valid") PreviewProbeContract.Validate(grid);
                else Assert.Throws<InvalidOperationException>(() => PreviewProbeContract.Validate(grid), defect);
            }
        });
    }

    private static void RegisterWiringTests()
    {
        Test("侧栏库重建接线：已处理的Moved仍进入静止计时，每项都有独立状态", () =>
        {
            var preview = ReadSource("Views/ShellPage.LibraryPreview.cs");
            var initialize = Between(preview, "    private void InitializeLibraryPreview()", "    private void OnSidebarLibraryPointerEntered(");
            Assert.Contains("Sidebar.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSidebarPreviewPointerPressed), true);", initialize);
            var sidebar = ReadSource("Views/ShellPage.Sidebar.cs");
            var rebuild = Between(sidebar, "    private void RebuildSidebarLibraries()", "    private void OnSidebarItemInvoked(");
            Assert.Contains("item.AddHandler(PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(OnSidebarLibraryPointerMoved), true);", rebuild);
            Assert.Contains("_previewDwells.Add(item, new PosterHoverDwell());", rebuild);
            Assert.Contains("item.PointerEntered += OnSidebarLibraryPointerEntered;", rebuild);
            Assert.Contains("item.PointerExited += OnSidebarLibraryPointerExited;", rebuild);
            Assert.True(rebuild.IndexOf("HideLibraryPreview();", StringComparison.Ordinal)
                < rebuild.IndexOf("_previewDwells.Clear();", StringComparison.Ordinal));
            Assert.True(rebuild.IndexOf("_previewDwells.Add(item, new PosterHoverDwell());", StringComparison.Ordinal)
                < rebuild.IndexOf("Sidebar.MenuItems.Insert(at++, item);", StringComparison.Ordinal));
        });
        Test("导航Invoke和SelectionChanged将实际目标交给点击抑制且早于GoTo", () =>
        {
            var sidebar = ReadSource("Views/ShellPage.Sidebar.cs");
            var invoked = Between(sidebar, "    private void OnSidebarItemInvoked(", "    private void OnSidebarSelectionChanged(");
            Assert.Contains("SuppressLibraryPreview(args.InvokedItemContainer as NavigationViewItem);", invoked);
            Assert.True(invoked.IndexOf("SuppressLibraryPreview(", StringComparison.Ordinal)
                < invoked.IndexOf("GoTo(tag);", StringComparison.Ordinal));
            var selected = Between(sidebar, "    private void OnSidebarSelectionChanged(", "\n}\n");
            Assert.Contains("SuppressLibraryPreview(args.SelectedItem as NavigationViewItem);", selected);
            Assert.True(selected.IndexOf("if (_syncingSidebar", StringComparison.Ordinal)
                < selected.IndexOf("SuppressLibraryPreview(", StringComparison.Ordinal));
            Assert.True(selected.IndexOf("SuppressLibraryPreview(", StringComparison.Ordinal)
                < selected.IndexOf("GoTo(tag);", StringComparison.Ordinal));
        });
        Test("独立预览探针显式属于Shell分区，宽窄检查共用唯一helper", () =>
        {
            var registration = ReadSource("ShellNavigationProbe.cs").Split('\n')
                .Single(line => line.Contains("SidebarLibraryPreviewAsync", StringComparison.Ordinal));
            Assert.Contains("await CaseAsync(\"侧边栏媒体库预览封面按钮与宽窄布局\", SidebarLibraryPreviewAsync, 30, section: ShellProbeSections.Shell);", registration);
            var probe = ReadSource("ShellNavigationProbe.Sidebar.cs");
            var sidebar = Between(probe, "    private async Task SidebarAsync()", "    private async Task SidebarAtStartupAsync()");
            Assert.Contains("await InspectLibraryPreviewAsync(shell, fixture, moviesTag, 760);", sidebar);
            var standalone = Between(probe, "    private async Task SidebarLibraryPreviewAsync()", "    private async Task InspectLibraryPreviewAsync(");
            Assert.Contains("fixture.Settings.Ui.EnableImmersive = false;", standalone);
            Assert.DoesNotContain(".SidebarMode", standalone);
            Assert.Contains("await InspectLibraryPreviewAsync(shell, fixture, tag, width);", standalone);
            var helper = Between(probe, "    private async Task InspectLibraryPreviewAsync(", "\n}\n");
            Assert.Contains("Call(shell, \"ShowLibraryPreviewAsync\", previewItem, moviesTag)", helper);
            Assert.Contains("Call(shell, \"HideLibraryPreview\");", helper);
            Assert.DoesNotContain("OnSidebarLibraryPointerEntered", helper);
            Assert.DoesNotContain("OnSidebarLibraryPointerExited", helper);
        });
    }

    private static string ReadSource(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) continue;
            return File.ReadAllText(Path.Combine(directory.FullName, "src", "Momoka.Shell", relative)).Replace("\r\n", "\n");
        }
        throw new InvalidOperationException("Cannot find this fixture's repository root");
    }

    private static string Between(string source, string start, string end)
    {
        var first = source.IndexOf(start, StringComparison.Ordinal);
        if (first < 0 || source.IndexOf(start, first + start.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Missing or ambiguous source-wiring start: " + start);
        var last = source.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        if (last < 0 || source.IndexOf(end, last + end.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Missing or ambiguous source-wiring end: " + end);
        return source[first..last];
    }
}
