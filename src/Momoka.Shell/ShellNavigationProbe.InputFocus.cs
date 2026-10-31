using System.Text;
using Momoka.Shell.Interop;
using Momoka.Shell.Views;
using Momoka.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>真实默认模板与快捷键控件；不读取账号、不提交、不移动鼠标。</summary>
    private async Task InputFocusControlsAsync()
    {
        var before = ThemeHost.Current;
        var text = new TextBox { Name = "InputFocusText", Header = "普通输入框", Text = "猫与龙" };
        var password = new PasswordBox { Name = "InputFocusPassword", Header = "密码框", Password = "offline-placeholder" };
        var search = new AutoSuggestBox { Name = "InputFocusSearch", Header = "搜索框", Text = "*S01E0?*.mkv E02" };
        var number = new NumberBox { Name = "InputFocusNumber", Header = "数字框", Value = 24 };
        var combo = new ComboBox { Name = "InputFocusCombo", Header = "下拉输入框", HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.Items.Add("默认选项");
        combo.Items.Add("另一个选项");
        combo.SelectedIndex = 0;
        var multiline = new TextBox { Name = "InputFocusMultiline", Header = "多行输入框", Text = "第一行\n第二行", AcceptsReturn = true, Height = 78 };
        var shortcut = new ShortcutCaptureBox { Name = "InputFocusShortcut", Combo = "Ctrl+K" };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "输入底色与中性焦点离线验证", Style = (Style)Application.Current.Resources["EgBodyStyle"] });
        foreach (var control in new Control[] { text, password, search, number, combo, multiline, shortcut }) content.Children.Add(control);
        var card = new Border { Margin = new Thickness(24), Padding = new Thickness(24), Child = content, VerticalAlignment = VerticalAlignment.Top };
        _root.Children.Add(card);
        ThemeHost.Register(card);
        var reads = new StringBuilder();
        var topMost = _window!.TopMost;
        try
        {
            _window.TopMost = true;
            foreach (var width in new[] { 1040, 760 })
            {
                ResizeProbeClient(width, 760);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
                    Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_window.Handle));
                var workArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(appWindow.Id,
                    Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
                appWindow.Move(new Windows.Graphics.PointInt32(workArea.X + 24, workArea.Y + 24));
                _window.Activate();
                await LayoutAsync(card);
                await WaitForDiagnosticsFrameAsync();
                Require(Native.GetClientRect(_window.Handle, out var client), "输入探针不能读取客户区");
                var origin = new NativePoint();
                Require(Native.ClientToScreen(_window.Handle, ref origin), "输入探针不能定位客户区");
                Require(Native.GetVisibleFrame(_window.Handle, out var frame), "输入探针不能读取可见外框");
                var geometry = $"width={width}, scale={_root.XamlRoot.RasterizationScale}, client={origin.X},{origin.Y}/{client.Width}x{client.Height}, frame={frame.Left},{frame.Top}/{frame.Width}x{frame.Height}, workArea={workArea}, HWND=0x{_window.Handle:X}";
                await File.AppendAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "input-focus-window.txt"), geometry + "\n");
                Require(frame.Left >= workArea.X && frame.Top >= workArea.Y
                    && frame.Right <= workArea.X + workArea.Width && frame.Bottom <= workArea.Y + workArea.Height,
                    "输入探针窗口不能容纳在所选屏幕工作区：" + geometry);
                foreach (var (column, row) in new[] { (0.05, 0.2), (0.95, 0.2), (0.05, 0.8), (0.95, 0.8) })
                {
                    var point = new NativePoint { X = origin.X + (int)(client.Width * column), Y = origin.Y + (int)(client.Height * row) };
                    var owner = Native.GetAncestor(Native.WindowFromPoint(point), 2);
                    await File.AppendAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "input-focus-window.txt"),
                        $"point={point.X},{point.Y}, owner=0x{owner:X}, expected=0x{_window.Handle:X}\n");
                    Require(owner == _window.Handle, "输入探针取样点不属于当前窗口");
                }
                foreach (var theme in width == 1040 ? UiThemes.All : [UiThemes.Default])
                {
                    ThemeHost.Apply(theme);
                    _root.Background = new SolidColorBrush(ThemeHost.ToColor(theme.Colors.Window));
                    card.Background = new SolidColorBrush(ThemeHost.ToColor(theme.Colors.SurfaceAlt));
                    await LayoutAsync(card);
                    var expected = ThemeHost.ToColor(theme.Colors.BorderStrong);
                    var background = ThemeHost.ToColor(theme.Colors.Window);
                    var glass = ThemeHost.ToColor(InputControlTheme.Glass(theme));
                    Require(background != ThemeHost.ToColor(theme.Colors.SurfaceAlt), "样例卡片与页面底色没有区别");
                    foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
                        "TextControlBackgroundDisabled" })
                        foreach (var dictionary in new[] { "Dark", "Light" })
                            Require(IsInputBackground(ThemeHost.ColorOf(dictionary, key), background),
                                $"{theme.Id}/{dictionary}/{key} 仍使用卡片底色");
                    foreach (var key in new[] { "ComboBoxBackground", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed",
                        "ComboBoxBackgroundFocused", "ComboBoxBackgroundUnfocused", "ComboBoxBackgroundDisabled", "EgInputGlassBrush" })
                        foreach (var dictionary in new[] { "Dark", "Light" })
                            Require(IsInputBackground(ThemeHost.ColorOf(dictionary, key), glass),
                                $"{theme.Id}/{dictionary}/{key} 未使用输入玻璃");
                    Require(!IsInputBackground(ThemeHost.ToColor(theme.Colors.SurfaceAlt), background)
                        && !IsInputBackground(null, background), "探针未拒绝卡片色与缺失底色反例");
                    Require(!IsInputBackground(glass, background), "探针未拒绝玻璃 tint 与侧栏色相同的反例");
                    foreach (var dictionary in new[] { "Dark", "Light" })
                        Require(ThemeHost.ColorOf(dictionary, "TextControlBorderBrushFocused") == expected,
                            $"{theme.Id}/{dictionary} 全局焦点被重新涂成主题色");
                    Require(!IsNeutralInputBorder(new SolidColorBrush(ThemeHost.ToColor(theme.Colors.Accent)), expected),
                        "探针未拒绝强调色描边反例");
                    Require(!IsNeutralInputBorder(null, expected), "探针未拒绝缺失焦点描边反例");
                    var searchInput = Descendants(search).OfType<TextBox>().Single();
                    RequireSearchRadius(search);
                    foreach (var input in new Control[] { text, password, search, number, combo, multiline })
                        RequireInputRadius(input);
                    if (width == 1040 && theme == UiThemes.Default)
                    {
                        foreach (var input in new Control[] { text, password, number, combo, multiline })
                        {
                            var saved = input.CornerRadius;
                            var oldRadiusRejected = false;
                            try
                            {
                                input.CornerRadius = new CornerRadius(4);
                                RequireInputRadius(input);
                            }
                            catch (InvalidOperationException) { oldRadiusRejected = true; }
                            finally { input.CornerRadius = saved; }
                            Require(oldRadiusRejected, $"{input.Name} 旧圆角反例未被拒绝");
                        }
                        var radius = search.CornerRadius;
                        var rejected = false;
                        try
                        {
                            search.CornerRadius = new CornerRadius(20);
                            RequireSearchRadius(search);
                        }
                        catch (InvalidOperationException) { rejected = true; }
                        finally { search.CornerRadius = radius; }
                        Require(rejected, "搜索框恢复旧胶囊圆角的反例未被拒绝");
                        await LayoutAsync(search);
                        RequireSearchRadius(search);
                    }
                    var numberInput = Descendants(number).OfType<TextBox>().Single(control => control.Name == "InputBox");
                    foreach (var outer in new Control[] { text, password, search, number, multiline })
                        Require(outer.Background is SolidColorBrush color && color.Color == background,
                            $"{theme.Id}/{outer.Name} 外层仍用卡片底色");
                    Require(combo.Background is AcrylicBrush comboOuter && comboOuter.TintColor == glass,
                        $"{theme.Id} 下拉输入框外层未使用输入玻璃");
                    foreach (var entry in new (string Name, Control Control)[]
                        { ("text", text), ("password", password), ("search", searchInput), ("number", numberInput), ("multiline", multiline) })
                    {
                        Require(entry.Control.Focus(FocusState.Programmatic), $"{entry.Name} 不能取得焦点");
                        await LayoutAsync(card);
                        var border = Descendants(entry.Control).OfType<Border>().Single(control => control.Name == "BorderElement");
                        Require(IsNeutralInputBorder(border.BorderBrush, expected), $"{theme.Id}/{entry.Name} 的真实模板仍有主题色外圈");
                        Require(border.Background is SolidColorBrush fill && fill.Color == background,
                            $"{theme.Id}/{entry.Name} 聚焦时退回卡片底色");
                        Require(border.BorderThickness.Left > 0 && border.BorderThickness.Right > 0,
                            $"{entry.Name} 移除颜色时把焦点线一起删掉");
                        RequireInputRadius(entry.Control);
                        reads.AppendLine($"{theme.Id}/{width}/{entry.Name}: radius={border.CornerRadius}, background={background}, border={expected}, thickness={border.BorderThickness}");
                        await SaveProbeScreenAsync($"input-focus-{theme.Id}-{width}-{entry.Name}-screen.png");
                    }
                    Require(combo.Focus(FocusState.Keyboard), "下拉输入框不能取得键盘焦点");
                    await LayoutAsync(card);
                    var comboBackground = Descendants(combo).OfType<Border>().Single(control => control.Name == "Background");
                    RequireInputRadius(combo);
                    Require(comboBackground.Background is AcrylicBrush comboFill && comboFill.TintColor == glass,
                        $"{theme.Id} 闭合下拉聚焦时退回卡片底色");
                    await SaveProbeScreenAsync($"input-focus-{theme.Id}-{width}-combo-screen.png");
                    Require(shortcut.Focus(FocusState.Keyboard), "快捷键输入框不能取得键盘焦点");
                    var shortcutFrame = Descendants(shortcut).OfType<Border>().Single(control => control.Name == "ComboBorder");
                    Require(shortcutFrame.CornerRadius == new CornerRadius(10), "快捷键框未使用统一圆角");
                    Require(Get<Button>(shortcut, "ClearButton").CornerRadius == new CornerRadius(10), "快捷键清除框未使用统一圆角");
                    Require(shortcutFrame.Background is AcrylicBrush shortcutGlass && shortcutGlass.TintColor == glass,
                        $"{theme.Id} 快捷键输入框静止时未使用输入玻璃");
                    Call(shortcut, "StartCapture");
                    try
                    {
                        await LayoutAsync(card);
                        var border = Descendants(shortcut).OfType<Border>().Single(control => control.Name == "ComboBorder");
                        Require(IsNeutralInputBorder(border.BorderBrush, expected), $"{theme.Id} 快捷键抓取仍有主题色外圈");
                        Require(border.CornerRadius == new CornerRadius(10), "快捷键抓取时恢复了旧圆角");
                        Require(border.Background is SolidColorBrush shortcutFill && shortcutFill.Color == background,
                            $"{theme.Id} 快捷键抓取仍用卡片底色");
                        Require(IsNeutralInputBorder(shortcut.FocusVisualPrimaryBrush, ThemeHost.ToColor(theme.Colors.Text)),
                            "快捷键的系统键盘焦点仍使用强调色");
                        await SaveProbeScreenAsync($"input-focus-{theme.Id}-{width}-shortcut-screen.png");
                    }
                    finally { Call(shortcut, "EndCapture"); }
                    Require(text.Text == "猫与龙" && search.Text == "*S01E0?*.mkv E02" && number.Value == 24,
                        "切换焦点或主题改变了输入内容");
                }
            }
            await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "input-focus-readings.txt"), reads.ToString());
        }
        finally
        {
            _window.TopMost = topMost;
            _root.Children.Remove(card);
            ThemeHost.Apply(before);
            _root.Background = null;
            ResizeInspect(1420, 980);
        }
    }

    private static bool IsInputBackground(Color? actual, Color expected) => actual == expected;

    private static bool IsNeutralInputBorder(Brush? brush, Color expected) => brush is SolidColorBrush color && color.Color == expected;

    private static void RequireInputRadius(Control input)
    {
        var expected = new CornerRadius(10);
        Require(input.CornerRadius == expected, $"{input.Name} 未使用搜索框的 10 DIP 圆角");
        var inner = input is AutoSuggestBox or NumberBox ? Descendants(input).OfType<TextBox>().Single() : input;
        Require(inner.CornerRadius == expected, $"{input.Name} 内层输入未接收圆角");
        var frameName = input is ComboBox ? "Background" : "BorderElement";
        var frame = Descendants(inner).OfType<Border>().Single(border => border.Name == frameName);
        Require(frame.CornerRadius == expected, $"{input.Name} 真实模板未接收圆角");
    }

    private static void RequireSearchRadius(Control input)
    {
        Require(input.CornerRadius == new CornerRadius(10), $"{input.Name} 搜索框未使用统一的 10 DIP 圆角");
        var text = input is AutoSuggestBox
            ? Descendants(input).OfType<TextBox>().Single()
            : (TextBox)input;
        Require(text.CornerRadius == new CornerRadius(10)
            && Descendants(text).OfType<Border>().Single(border => border.Name == "BorderElement")
                .CornerRadius == new CornerRadius(10), $"{input.Name} 搜索框真实模板未接收圆角");
    }
}
