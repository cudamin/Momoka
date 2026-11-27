using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.AniBt;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.Interop;
using Momoka.Shell.Platform;
using Momoka.Shell.Windowing;
using Windows.Graphics;
using Windows.UI;

namespace Momoka.Shell.Views;

/// <summary>
/// 「在 MoviePilot 搜索其他版本」的独立窗口 —— 用户在「更多」菜单点那一条时弹出来（<c>ShellPage.SearchMoviePilotVersions</c>）。
/// <para>
/// 照 <see cref="SettingsWindow"/> 的路子办：使用 <see cref="MicaBackdrop"/>（<c>MicaKind.Base</c>）与扩展标题栏，
/// 透明效果关闭或高对比度下回退到实色窗口底。标题栏的 X 只隐藏不真关闭，避免最后一个 <see cref="Window"/>
/// 关闭触发进程退出。
/// </para>
/// </summary>
internal sealed class MoviePilotWindow
{
    private const string Category = "moviepilot窗口";

    /// <summary>Logical pixels, scaled to the owner's DPI in <see cref="Place"/>.</summary>
    private const int LogicalWidth = 900;

    private const int LogicalHeight = 680;

    private readonly Window _window;
    private readonly MoviePilotVersionsView _view;
    private readonly AniBtSearchPanel _anime;
    private readonly SolidColorBrush _canvas;
    private readonly TextBlock _titleLabel;
    private readonly bool _micaSupported;
    private bool _mica;

    /// <summary>换主题或透明效果时重画标题栏与云母；存下来是为了 <see cref="Close"/> 里退订。</summary>
    private readonly Action<Momoka.Theming.UiTheme> _repaint;

    /// <summary>Set only by <see cref="Close"/>, so <see cref="OnClosing"/> knows to let one through.</summary>
    private bool _closing;

    private bool _open;

    private MoviePilotWindow(
        Window window,
        MoviePilotVersionsView view,
        AniBtSearchPanel anime,
        SolidColorBrush canvas,
        TextBlock titleLabel,
        bool micaSupported)
    {
        _window = window;
        _view = view;
        _anime = anime;
        _canvas = canvas;
        _titleLabel = titleLabel;
        _micaSupported = micaSupported;

        _repaint = _ =>
        {
            ApplyBackdrop();
            PaintCaption();
        };
        ThemeHost.Changed += _repaint;
        window.Activated += OnWindowActivated;
        window.AppWindow.Closing += OnClosing;
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args) => PaintCaption();

    /// <summary>Whether the window is on screen. Tracked here, like <see cref="SettingsWindow"/>.</summary>
    internal bool IsOpen => _open;

    /// <summary>Outer size in physical pixels. Read by the self-check if it ever wants it.</summary>
    internal SizeInt32 Size => _window.AppWindow.Size;

    /// <summary>探针要真的把这扇窗摆到某个尺寸、再拍一张含标题栏的实屏：它得拿到 AppWindow。</summary>
    internal AppWindow AppWindow => _window.AppWindow;

    /// <summary>探针读窗口里那一份内容的状态（搜完没有），不把窗口的内部结构复制一遍。</summary>
    internal MoviePilotVersionsView View => _view;

    /// <summary>把它带到前面（探针摆好尺寸之后要保证没被别的窗口挡住才拍得到）。</summary>
    internal void Activate() => _window.Activate();

    /// <summary>
    /// Builds the window, or returns null having logged why. Null is not a loud failure: the caller
    /// (<c>ShellPage.SearchMoviePilotVersions</c>) just reports it and does nothing else — a second XAML
    /// window can fail to create, and there is no in-frame fallback worth building for this.
    /// </summary>
    internal static MoviePilotWindow? TryCreate(Windowing.HostWindow? owner)
    {
        try
        {
            var micaSupported = MicaController.IsSupported();
            var canvas = new SolidColorBrush(WindowColor(ThemeHost.Current));
            var view = new MoviePilotVersionsView { Background = new SolidColorBrush(Colors.Transparent) };
            var anime = new AniBtSearchPanel { Padding = new Thickness(16), Visibility = Visibility.Collapsed };

            var root = new Grid { Background = canvas };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleLabel = new TextBlock
            {
                Text = "在 MoviePilot 搜索版本",
                Margin = new Thickness(16, 0, 140, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var caption = new Border { Name = "MoviePilotWindowTitleBar", Background = new SolidColorBrush(Colors.Transparent) };
            caption.Child = titleLabel;
            root.Children.Add(caption);

            var body = new Grid();
            Grid.SetRow(body, 1);
            body.Children.Add(view);
            body.Children.Add(anime);
            root.Children.Add(body);

            // 深浅跟着当前主题走（这个窗口里是框架控件 + 我们的 Eg* 样式，只认 ElementTheme）。
            ThemeHost.Register(root);

            if (Application.Current.Resources.TryGetValue("EgUiFontFamily", out var font) && font is FontFamily family)
            {
                view.FontFamily = family;
                anime.FontFamily = family;
                titleLabel.FontFamily = family;
            }

            var window = new Window { ExtendsContentIntoTitleBar = true, Content = root, Title = "在 MoviePilot 搜索版本" };
            window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
            window.SetTitleBar(caption);

            Place(window, owner);
            var created = new MoviePilotWindow(window, view, anime, canvas, titleLabel, micaSupported);
            created.ApplyBackdrop();
            created.PaintCaption();

            Log.Info(Category, "MoviePilot 版本窗口已创建");
            return created;
        }
        catch (Exception error)
        {
            Log.Warn(Category, "创建 MoviePilot 版本窗口失败", error);
            return null;
        }
    }

    /// <summary>
    /// Shows the window for one item: seeds the view with the keyword the item computes to
    /// (<see cref="MoviePilotVersionQuery.Keyword"/>) and runs the search, then brings the window up. For an
    /// episode the target episode rides along (<see cref="MoviePilotVersionQuery.TargetEpisode"/>) so the results
    /// get filtered to it automatically.
    /// </summary>
    internal void Show(MoviePilotService service, EmbyItem item, ISystemLauncher launcher) =>
        Show(service, MoviePilotVersionQuery.Keyword(item), launcher, MoviePilotVersionQuery.TargetEpisode(item));

    /// <summary>
    /// 按现成的关键词开窗。订阅详情里「搜索这一集的资源」走这一支 —— 那边只有订阅快照里的剧名与季集号，
    /// 没有 Emby 条目可算词（见 <see cref="MoviePilotVersionQuery.Keyword(string, int?, int?)"/>）。
    /// <paramref name="episode"/> 是集页面带来的目标集，交给视图搜完自动按它筛；订阅与遗漏集那两支不给。
    /// </summary>
    internal void Show(MoviePilotService service, string keyword, ISystemLauncher launcher, int? episode = null)
    {
        Release();
        _anime.Visibility = Visibility.Collapsed;
        _view.Visibility = Visibility.Visible;
        SetWindowTitle("在 MoviePilot 搜索版本");
        _view.Search(service, keyword, launcher, episode);
        ApplyBackdrop();
        PaintCaption();
        _window.AppWindow.Show();
        _window.Activate();
        _open = true;
    }

    internal void Show(AniBtService animeService, MoviePilotService service, AniBtSearchQuery query, ISystemLauncher launcher)
    {
        Release();
        _view.Visibility = Visibility.Collapsed;
        _anime.Visibility = Visibility.Visible;
        SetWindowTitle("在 AniBT 搜索版本");
        _anime.Search(animeService, service, launcher, query);
        ApplyBackdrop();
        PaintCaption();
        _window.AppWindow.Show();
        _window.Activate();
        _open = true;
    }

    private void SetWindowTitle(string title)
    {
        _window.Title = title;
        _titleLabel.Text = title;
    }

    private void ApplyBackdrop()
    {
        var system = CaptionAppearance.ReadSystem();
        if (!_micaSupported || !ThemeHost.TransparencyEnabled || system.HighContrast)
        {
            try { _window.SystemBackdrop = null; }
            catch (Exception error) { Log.Warn(Category, "撤除 MoviePilot 窗口 Mica 失败", error); }
            _mica = false;
            return;
        }

        if (_mica) return;
        try
        {
            _window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
            _mica = true;
        }
        catch (Exception error)
        {
            Log.Warn(Category, "设置 MoviePilot 窗口 Mica 失败，改用纯色底", error);
            _mica = false;
        }
    }

    private void Release()
    {
        _view.Release();
        _anime.Release();
    }

    /// <summary>Off screen, still built. What the X does. The in-flight search is cancelled on the way out.</summary>
    internal void Hide()
    {
        if (!_open) return;

        _open = false;
        Release();
        _window.AppWindow.Hide();
    }

    /// <summary>
    /// The real close, from <c>ShellPage.Shutdown</c> only — a hidden window is still an open one, and left
    /// to the framework it would keep the app alive after the main window had gone.
    /// </summary>
    internal void Close()
    {
        _closing = true;
        _open = false;
        ThemeHost.Changed -= _repaint;
        _window.Activated -= OnWindowActivated;
        Release();

        try
        {
            _window.Close();
        }
        catch (Exception error)
        {
            Log.Warn(Category, "关闭 MoviePilot 版本窗口失败", error);
        }
    }

    /// <summary>Hides on the X instead of closing; see the note on the class for why that matters here.</summary>
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        if (_closing) return;

        e.Cancel = true;
        _open = false;
        Release();
        sender.Hide();
    }

    /// <summary>
    /// Centred on the owner's monitor at <see cref="LogicalWidth"/>×<see cref="LogicalHeight"/> logical
    /// pixels. <c>MoveAndResize</c> speaks physical ones, so the numbers are scaled by the owner's DPI and
    /// clamped to the work area. Copied from <see cref="SettingsWindow"/>; same reasoning.
    /// </summary>
    private static void Place(Window window, Windowing.HostWindow? owner)
    {
        var self = Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
        var reference = owner?.Handle is { } handle && handle != IntPtr.Zero ? handle : self;

        var dpi = Native.GetDpiForWindow(reference);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;

        var area = DisplayArea.GetFromWindowId(
            Win32Interop.GetWindowIdFromWindow(reference), DisplayAreaFallback.Nearest);
        var work = area.WorkArea;

        var width = Fit(LogicalWidth, 640, work.Width, scale);
        var height = Fit(LogicalHeight, 480, work.Height, scale);

        window.AppWindow.MoveAndResize(new RectInt32(
            work.X + ((work.Width - width) / 2),
            work.Y + ((work.Height - height) / 2),
            width,
            height));
    }

    /// <summary>One axis: the wanted size in physical pixels, short of filling the monitor, never below the minimum.</summary>
    private static int Fit(int logical, int minimum, int available, double scale) =>
        Math.Max(
            (int)Math.Round(minimum * scale),
            Math.Min((int)Math.Round(logical * scale), available - (int)Math.Round(48 * scale)));

    /// <summary>
    /// 统一主窗口与设置窗口的标题栏着墨、圆角、空白小图标和高对比度回退。
    /// </summary>
    private void PaintCaption()
    {
        try
        {
            var theme = ThemeHost.Current;
            var system = CaptionAppearance.ReadSystem();
            var canvasColor = system.HighContrast ? system.Background : WindowColor(theme);
            canvasColor.A = !system.HighContrast && _mica && system.AdvancedEffects ? (byte)0x00 : (byte)0xFF;
            _canvas.Color = canvasColor;
            _titleLabel.Foreground = new SolidColorBrush(system.HighContrast ? system.Foreground : ThemeHost.ToColor(theme.Colors.Text));

            var handle = Win32Interop.GetWindowFromWindowId(_window.AppWindow.Id);
            CaptionAppearance.ApplyDwmChrome(handle, theme, _mica && system.AdvancedEffects, fullscreen: false, system.HighContrast);

            // 发空句柄摘不掉标题栏图标 —— 换上一颗全透明的、把那一档占住；大档不动。
            Native.SendMessage(handle, Native.WmSetIcon, (IntPtr)0, BlankCaptionIcon.Handle);

            var titleBar = _window.AppWindow.TitleBar;
            if (titleBar is null) return;

            CaptionAppearance.PaintTitleBar(titleBar, theme, system, onScrim: false, transparentSurface: true, dimInactive: true);
        }
        catch (Exception error)
        {
            Log.Warn(Category, "MoviePilot 版本窗口标题栏配色失败", error);
        }
    }

    private static Color WindowColor(Momoka.Theming.UiTheme theme) => ThemeHost.ToColor(theme.Colors.Window);
}
