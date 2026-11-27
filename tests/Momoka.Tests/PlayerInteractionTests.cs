using System.Xml.Linq;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Tests;

internal static class PlayerInteractionTests
{
    private static XElement SkipButtonMarkup()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var document = XDocument.Load(Path.Combine(directory!.FullName, "src", "Momoka.Shell", "Views", "PlayerPage.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Descendants().Single(element => element.Name.LocalName == "Button" && element.Attribute(xaml + "Name")?.Value == "SkipButton");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    internal static void Register()
    {
        TestHarness.Test("集成跳过按钮：与独占一样只有图标和标题的一行", () =>
        {
            var button = SkipButtonMarkup();
            var row = button.Elements().Single(element => element.Name.LocalName == "StackPanel");
            Assert.Equal("Horizontal", row.Attribute("Orientation")?.Value);
            Assert.Equal("7.5", row.Attribute("Spacing")?.Value);
            Assert.Equal("FontIcon,TextBlock", string.Join(',', row.Elements().Select(element => element.Name.LocalName)));
            Assert.Equal("18,21", button.Attribute("Padding")?.Value);
            Assert.Equal("2", button.Attribute("CornerRadius")?.Value);
            Assert.Equal("Bold", row.Elements().Last().Attribute("FontWeight")?.Value);
            Assert.False(button.Descendants().Any(element => element.Name.LocalName == "ProgressBar"));
            Assert.False(button.Descendants().Any(element => element.Attributes().Any(attribute => attribute.Value.Contains("SkipTip", StringComparison.Ordinal))));
        });

        TestHarness.Test("集成跳过按钮：简化后保留动态文案、显隐、命令和悬停画刷", () =>
        {
            var button = SkipButtonMarkup();
            Assert.Equal("{x:Bind ViewModel.TakeSkipCommand}", button.Attribute("Command")?.Value);
            Assert.Equal("{x:Bind ViewModel.SkipVisibility, Mode=OneWay}", button.Attribute("Visibility")?.Value);
            Assert.Equal("{x:Bind ViewModel.SkipCaption, Mode=OneWay}", button.Attribute("AutomationProperties.Name")?.Value);
            var caption = button.Descendants().Single(element => element.Name.LocalName == "TextBlock");
            Assert.Equal("{x:Bind ViewModel.SkipCaption, Mode=OneWay}", caption.Attribute("Text")?.Value);
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            var presentation = button.Name.Namespace;
            var dark = button.Descendants(presentation + "ResourceDictionary")
                .Single(element => (string?)element.Attribute(xaml + "Key") == "Dark");
            var resources = dark.Elements(presentation + "StaticResource")
                .ToDictionary(element => element.Attribute(xaml + "Key")!.Value, element => element.Attribute("ResourceKey")!.Value);
            Assert.Equal("PlayerSkipHoverBrush", resources["ButtonBackgroundPointerOver"]);
            Assert.Equal("PlayerSkipPressedBrush", resources["ButtonBackgroundPressed"]);
        });

        TestHarness.Test("自检媒体库视图：只轮巡 Emby 的七种，不掺入 Jellyfin 专属布局", () =>
        {
            var root = RepositoryRoot();
            var text = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "LibraryPage.xaml.cs"));
            Assert.Contains("foreach (var view in LibraryDisplayCatalog.EmbyViews)", text);
            Assert.DoesNotContain("foreach (var view in Enum.GetValues<LibraryView>())", text);
            Assert.Equal(7, Momoka.Emby.LibraryDisplayCatalog.EmbyViews.Count);
        });
        TestHarness.Test("跳过键位：退出和提示走可重绑目录，预览接线先于焦点控件", () =>
        {
            var code = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "src", "Momoka.Shell", "Views", "PlayerPage.Input.cs"));

            Assert.Contains("ShortcutCatalog.Lookup(ViewModel.ShortcutBindings, stroke, ViewModel.SkipOffered)", code);
            Assert.Contains("[ShortcutCatalog.ExitPlayback] = ExitFullscreenOrStop", code);
            Assert.Contains("[ShortcutCatalog.DismissSkip] = () => ViewModel.DismissSkip()", code);
            Assert.Contains("[ShortcutCatalog.TakeSkip] = () => ViewModel.TakeSkip()", code);
            Assert.DoesNotContain("fixedFallback", code);
            Assert.Contains("CanDispatchVirtualKey", code);
            Assert.Contains("SkipPromptShortcut(e.Key, e.KeyStatus.IsExtendedKey, e.KeyStatus.WasKeyDown)", code);
            Assert.Contains("ReleaseSkipPromptKey(e.Key, e.KeyStatus.IsExtendedKey)", code);
            Assert.Contains("ReferenceEquals(_lastDispatchedKeyEvent, e)", code);
            var page = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "src", "Momoka.Shell", "Views", "PlayerPage.xaml.cs"));
            Assert.Contains("PreviewKeyDown += OnPreviewSkipShortcut;", page);
            Assert.Contains("PreviewKeyUp += OnPreviewSkipKeyUp;", page);
            Assert.Contains("_skipPreviewArmed = true;", page);

            // 焦点掉出 XAML 岛时走的是窗口钩子；页面点头还不够，钩子自己的白名单也得放行 Delete。
            var host = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "src", "Momoka.Shell", "Windowing", "HostWindow.cs"));
            Assert.Contains("vk is > 0 and <= 0xFF", host);
            Assert.Contains("_win32Keys?.WantsKey(vk, extended) == true", host);
        });
        TestHarness.Test("快捷键派发：音量滑条未改绑的方向键仍归滑条自己", () =>
        {
            // Root 上的全局快捷键带 handledEventsToo；若不给焦点滑条/按钮留豁免，焦点停在音量滑条上按
            // ↑↓ 会先被绑在方向键上的播放器动作抢走。豁免只认用户显式改绑，默认键仍遵循控件自己的语义。
            var input = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "src", "Momoka.Shell", "Views", "PlayerPage.Input.cs"));
            Assert.Contains("FocusedElement is ButtonBase or Slider", input);
            Assert.Contains("HasExplicitShortcutBinding", input);

            // WinUI 3 的无参 GetFocusedElement() 恒返回 null（靠 CoreWindow 的 API 桌面岛没有），
            // 只能走 XamlRoot 重载。用错时窗口键盘兜底永远以为「岛内没人接键」，会把播放器快捷键里
            // 的字母从搜索框手里抢走（2026-11-25 用户报搜索框无法直接输入字母）。
            Assert.Contains("FocusManager.GetFocusedElement(root)", input);
            Assert.DoesNotContain("FocusManager.GetFocusedElement() is", input);
            // 播放层不在屏上时一概不接键，浏览页的输入框才拿得回自己的字母。
            Assert.Contains("Visibility != Visibility.Visible", input);

            var page = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "src", "Momoka.Shell", "Views", "PlayerPage.xaml.cs"));
            Assert.DoesNotContain("VolumeSlider.AddHandler", page);
        });
        TestHarness.Test("服务器渲染计数：按实际模板根标识，不依赖框架容器类型", () =>
        {
            var root = RepositoryRoot();
            var xaml = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "ServersPage.xaml"));
            var code = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "ServersPage.xaml.cs"));
            Assert.Contains("<Border Tag=\"{x:Bind}\"", xaml);
            Assert.Contains("<Grid Tag=\"{x:Bind}\">", xaml);
            Assert.Contains("FrameworkElement { Tag: ServerRow }", code);
            Assert.Contains("FrameworkElement { Tag: AccountRow }", code);
            Assert.DoesNotContain("ServerPrimaryAccount", code);
            Assert.DoesNotContain("ItemsSource=\"{x:Bind Others}\"", xaml);
        });
        TestHarness.Test("集页首屏检查：无静止描边且仍检查内容次序与可视范围", () =>
        {
            var root = RepositoryRoot();
            var code = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "DetailPage.xaml.cs"));
            Assert.Contains("var bareFrame = !Rings(EpisodePanel);", code);
            Assert.Contains("ok &= order && below && bareFrame;", code);
            Assert.Contains("ok &= inside || !roomy;", code);
        });
        TestHarness.Test("窄窗控制条：不足以显示倍速条时保留播放和右侧操作", () =>
        {
            var code = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Momoka.Shell", "Views", "PlayerPage.Transport.cs"));
            Assert.Contains("FullscreenTransportCell : WindowTransportCell", code);
            Assert.Contains("var logicalWidth = width / ChromeFactor;", code);
            Assert.Contains("var compact = logicalWidth < 680;", code);
            Assert.Contains("var tiny = logicalWidth < 520;", code);
            Assert.Contains("DanmakuButton.Visibility == Visibility.Visible ? cell + gap : 0", code);
            Assert.Contains("SpeedStrip.Width > speedRoom", code);
            Assert.Contains("SpeedStrip.Visibility = Visibility.Collapsed", code);
        });

        TestHarness.Test("暂停/播放徽标：136 的方框外面套着两档用的 Viewbox", () =>
        {
            // 用户令 2026-11-17「点击屏幕暂停/开始时出现的图标全屏时要放大 1.3 倍」。形状是 PulseArt 里写死的
            // 136 坐标，不吃字号，所以大档只能落在外面那层框上（与置顶那颗图钉的 PinGlyphBox 同一个手法）。
            // 崩起来是静默的：Viewbox 被谁去掉、或者 Canvas 的宽高跟着被挪走，屏上那颗徽标就永远是窗口档那一档
            // 大小，编译、单测与自检以外的任何一条路都看不出来。
            var document = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "Momoka.Shell", "Views", "PlayerPage.xaml"));
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

            var badge = document.Descendants()
                .Single(element => element.Name.LocalName == "Border" && element.Attribute(xaml + "Name")?.Value == "PulseBadge");

            // 属性元素（Border.RenderTransform）也是 XML 的子元素，所以按名字挑那颗 Viewbox。
            var box = badge.Elements().Single(element => element.Name.LocalName == "Viewbox");
            Assert.Equal("PulseZoomBox", box.Attribute(xaml + "Name")?.Value);
            Assert.Equal("Uniform", box.Attribute("Stretch")?.Value);

            // 宽高就是这一档的方框，初值必须是窗口档 —— 只 build 不经过 ApplyPulseScale 的那几条路（探针、
            // --show-osd）也还是对的。
            Assert.Equal(PulseArt.Box.ToString(), box.Attribute("Width")?.Value);
            Assert.Equal(PulseArt.Box.ToString(), box.Attribute("Height")?.Value);

            var canvas = box.Elements().Single(element => element.Name.LocalName == "Canvas");
            Assert.Equal("PulseArtBox", canvas.Attribute(xaml + "Name")?.Value);
            Assert.Equal(PulseArt.Box.ToString(), canvas.Attribute("Width")?.Value);
            Assert.Equal(PulseArt.Box.ToString(), canvas.Attribute("Height")?.Value);
            Assert.Equal("Path", canvas.Elements().Single().Name.LocalName);
        });

        TestHarness.Test("暂停/播放徽标：全屏/最大化档挂在 ApplyTitleScale 上，倍率就是那 1.3", () =>
        {
            var root = RepositoryRoot();
            var code = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "PlayerPage.Chrome.cs"));

            // 两档的方框：窗口档就是 PulseArt 认过的那一档，大档乘 1.3 —— 与浮层其余控件同一把尺。
            Assert.Contains("private const double WindowPulseBox = PulseArt.Box;", code);
            Assert.Contains("private const double FullscreenPulseBox = PulseArt.Box * 1.3;", code);

            // 摆的是 Viewbox，不是 PulseScale（那是 PulseStoryboard 的动画属性，摆上去一帧就被抹掉）。
            Assert.Contains("PulseZoomBox.Width = box;", code);
            Assert.Contains("PulseZoomBox.Height = box;", code);
            Assert.DoesNotContain("PulseScale.ScaleX = box", code);

            // 进出全屏与进页面只有 ApplyTitleScale 一条路 —— 它不带这一档，全屏时徽标就不跟着长。
            Assert.Contains("ApplyPulseScale(fullscreen);", code);
        });

        TestHarness.Test("独占快捷键：默认动作完整且清除旧键与新增键同时下发", () =>
        {
            var defaults = VideoWindowShortcuts.Bindings(new Dictionary<string, string>());
            Assert.Equal(ShortcutCatalog.Actions.Count, defaults.Count);
            Assert.True(ShortcutCatalog.Actions.Where(action => !ShortcutCatalog.IsSkipPrompt(action.Id))
                .All(action => defaults.Values.Contains(action.Id)));
            Assert.Equal("", defaults["ENTER"]);
            Assert.Equal("", defaults["DEL"]);
            Assert.Equal(ShortcutCatalog.ExitPlayback, defaults["ESC"]);
            var offered = VideoWindowShortcuts.Bindings(new Dictionary<string, string>(), skipOffered: true);
            Assert.True(ShortcutCatalog.Actions.All(action => offered.Values.Contains(action.Id)));
            Assert.Equal(ShortcutCatalog.TakeSkip, offered["KP_ENTER"]);
            var changed = VideoWindowShortcuts.Bindings(new Dictionary<string, string>
            {
                ["toggle-pause"] = "Ctrl+F9",
                ["toggle-fullscreen"] = ""
            });
            Assert.Equal("", changed["SPACE"]);
            Assert.Equal("", changed["f"]);
            Assert.Equal("toggle-pause", changed["Ctrl+F9"]);
            Assert.Equal(ShortcutCatalog.ExitPlayback, changed["ESC"]);
            Assert.Equal("", changed["ENTER"]);
            Assert.Equal("", changed["DEL"]);
        });
        TestHarness.Test("独占快捷键：大小写修饰符及标点只走受支持的键名", () =>
        {
            Assert.Equal("Shift+z", VideoWindowShortcuts.Key(new KeyStroke("Z", false, false, true)));
            Assert.Equal("Ctrl+Alt+F12", VideoWindowShortcuts.Key(new KeyStroke("F12", true, true, false)));
            Assert.Equal("[", VideoWindowShortcuts.Key(new KeyStroke("BracketLeft", false, false, false)));
            Assert.Equal("F24", VideoWindowShortcuts.Key(new KeyStroke("F24", false, false, false)));
            Assert.Equal("KP7", VideoWindowShortcuts.Key(new KeyStroke("NumPad7", false, false, false)));
            Assert.Equal("KP_ENTER", VideoWindowShortcuts.Key(new KeyStroke("NumPadEnter", false, false, false)));
            Assert.Equal("VOLUME_UP", VideoWindowShortcuts.Key(new KeyStroke("VolumeUp", false, false, false)));
            Assert.Null(VideoWindowShortcuts.Key(new KeyStroke("VkFF", false, false, false)),
                "当前 Windows mpv 窗口输入不支持通用 VK 别名");
            Assert.Equal("RECORD", VideoWindowShortcuts.Key(new KeyStroke("MediaRecord", false, false, false)));
            Assert.Equal("FORWARD", VideoWindowShortcuts.Key(new KeyStroke("MediaFastForward", false, false, false)));
            Assert.Null(VideoWindowShortcuts.Key(new KeyStroke("x;quit", false, false, false)));
        });
        TestHarness.Test("快捷键令牌：常用特殊键、数字键盘与键盘范围按预期生成", () =>
        {
            Assert.Equal("NumPad7", ShortcutKeyCatalog.Token(0x67));
            Assert.Equal("NumPadAdd", ShortcutKeyCatalog.Token(0x6B));
            Assert.Equal("F24", ShortcutKeyCatalog.Token(0x87));
            Assert.Equal("CapsLock", ShortcutKeyCatalog.Token(0x14));
            Assert.Equal("VolumeUp", ShortcutKeyCatalog.Token(0xAF));
            Assert.Equal("BrowserBack", ShortcutKeyCatalog.Token(0xA6));
            Assert.Equal("Semicolon", ShortcutKeyCatalog.Token(0xBA));
            Assert.Equal("Slash", ShortcutKeyCatalog.Token(0xBF));
            Assert.Equal("Backslash", ShortcutKeyCatalog.Token(0xDC));
            Assert.Equal("Oem8", ShortcutKeyCatalog.Token(0xDF));
            Assert.Equal("OemClear", ShortcutKeyCatalog.Token(0xFE));
            Assert.Equal("Vk88", ShortcutKeyCatalog.Token(0x88));
            Assert.Equal("VkE3", ShortcutKeyCatalog.Token(0xE3));
            Assert.Equal("NumPadLeft", ShortcutKeyCatalog.Token(0x25));
            Assert.Equal("Left", ShortcutKeyCatalog.Token(0x25, extended: true));
            Assert.Equal("NumPadEnter", ShortcutKeyCatalog.Token(0x0D, extended: true));
            Assert.Equal("Enter", ShortcutKeyCatalog.Token(0x0D));
            Assert.Null(ShortcutKeyCatalog.Token(0));
            Assert.True(Enumerable.Range(1, 0xFF).All(code => ShortcutKeyCatalog.Token(code) is not null),
                "每个可见的 Windows VirtualKey 都必须能录制成一个 token");
            Assert.Equal("NumPadSeparator", ShortcutKeyCatalog.Token(0x6C));
        });

        TestHarness.Test("独占快捷键：宿主只接受已知动作不接受任意命令", () =>
        {
            foreach (var action in ShortcutCatalog.Actions)
                Assert.NotNull(VideoWindowContract.Parse([VideoWindowContract.Shortcut, action.Id]));
            Assert.Null(VideoWindowContract.Parse([VideoWindowContract.Shortcut, "quit"]));
            Assert.Null(VideoWindowContract.Parse([VideoWindowContract.Shortcut, "seek 20"]));
        });

        TestHarness.Test("播放器菜单：同一行号不得跨菜单或重开复用", () =>
        {
            var first = new VideoMenuSnapshot<string>(["A1", "A2"]);
            var second = new VideoMenuSnapshot<string>(["B1", "B2"]);
            var value = first.ValueAt(1);
            Assert.True(first.TryResolve(value, out var selected));
            Assert.Equal("A2", selected);
            Assert.False(second.TryResolve(value, out _));
            Assert.False(first.TryResolve("2", out _));
        });

        TestHarness.Test("播放器菜单：列表原地替换不改旧菜单的条目快照", () =>
        {
            var items = new List<string> { "A1", "A2" };
            var menu = new VideoMenuSnapshot<string>(items);
            var value = menu.ValueAt(1);
            items[1] = "B2";
            Assert.True(menu.TryResolve(value, out var selected));
            Assert.Equal("A2", selected);
            Assert.False(menu.TryResolve(value[..33] + "3", out _));
            Assert.False(menu.TryResolve(value[..33] + "0", out _));
            Assert.False(VideoMenuSnapshot<string>.IsSelection(value + ";quit"));
        });

        TestHarness.Test("播放器菜单：契约接受带快照的选择且拒绝损坏的值", () =>
        {
            var menu = new VideoMenuSnapshot<string>(["A"]);
            var value = menu.ValueAt(0);
            foreach (var key in new[] { VideoWindowContract.EpisodeIndex, VideoWindowContract.VersionIndex, VideoWindowContract.MenuIndex })
            {
                Assert.NotNull(VideoWindowContract.Parse([key, value]));
                Assert.Null(VideoWindowContract.Parse([key, "not-a-snapshot:1"]));
                Assert.Null(VideoWindowContract.Parse([key, value + " extra"]));
            }
        });

        TestHarness.Test("播放器输入：静止位置重报不延后首次隐藏", () =>
        {
            var chrome = new ChromeReveal();
            chrome.Pointer(500, 1000, ChromePart.None, -1, 1000);
            for (var now = 1100; now <= 2000; now += 100)
                chrome.Pointer(500, 1000, ChromePart.None, -1, now, moved: false);
            Assert.True(chrome.CursorHidden);
            Assert.Equal(1000L, chrome.IdleAgo(2000));
        });

        TestHarness.Test("播放器输入：静止重排不撤掉键盘反馈宽限", () =>
        {
            var chrome = new ChromeReveal();
            chrome.Pointer(500, 1000, ChromePart.None, -1, 1000);
            chrome.WakeFully(2000);
            chrome.Pointer(400, 800, ChromePart.None, -1, 2100, moved: false);
            Assert.Equal(new ChromeState(true, true, true), chrome.State);
            chrome.Tick(2000 + ChromeReveal.GraceMilliseconds);
            Assert.Equal(new ChromeState(false, false, false), chrome.State);
        });

        TestHarness.Test("播放器输入：真移动仍取消键盘反馈并唤醒光标", () =>
        {
            var chrome = new ChromeReveal();
            chrome.Pointer(500, 1000, ChromePart.None, -1, 1000);
            chrome.Tick(2000);
            Assert.True(chrome.CursorHidden);
            chrome.WakeFully(2100);
            chrome.Pointer(500, 1000, ChromePart.None, -1, 2200, moved: true);
            Assert.False(chrome.CursorHidden);
            Assert.Equal(new ChromeState(false, false, false), chrome.State);
        });

        TestHarness.Test("播放器输入：控件本体保留光标但唤出带不保留", () =>
        {
            var chrome = new ChromeReveal();
            chrome.Pointer(950, 1000, ChromePart.Bar, -1, 1000);
            chrome.Pointer(950, 1000, ChromePart.Bar, -1, 3000, moved: false);
            Assert.False(chrome.CursorHidden);
            Assert.True(chrome.State.Bar);
            chrome.Pointer(850, 1000, ChromePart.None, -1, 3001, moved: false);
            Assert.True(chrome.CursorHidden);
            Assert.True(chrome.State.Bar);
        });
    }
}
