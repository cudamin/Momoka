using System.Text;
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
        try
        {
            foreach (var width in new[] { 1040, 760 })
            {
                ResizeProbeClient(width, 760);
                await LayoutAsync(card);
                foreach (var theme in width == 1040 ? UiThemes.All : [UiThemes.Default])
                {
                    ThemeHost.Apply(theme);
                    _root.Background = new SolidColorBrush(ThemeHost.ToColor(theme.Colors.Window));
                    card.Background = new SolidColorBrush(ThemeHost.ToColor(theme.Colors.SurfaceAlt));
                    await LayoutAsync(card);
                    var expected = ThemeHost.ToColor(theme.Colors.BorderStrong);
                    var background = ThemeHost.ToColor(theme.Colors.Window);
                    Require(background != ThemeHost.ToColor(theme.Colors.SurfaceAlt), "样例卡片与页面底色没有区别");
                    foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
                        "TextControlBackgroundDisabled", "ComboBoxBackground", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed",
                        "ComboBoxBackgroundFocused", "ComboBoxBackgroundUnfocused", "ComboBoxBackgroundDisabled" })
                        foreach (var dictionary in new[] { "Dark", "Light" })
                            Require(IsInputBackground(ThemeHost.ColorOf(dictionary, key), background),
                                $"{theme.Id}/{dictionary}/{key} 仍使用卡片底色");
                    Require(!IsInputBackground(ThemeHost.ToColor(theme.Colors.SurfaceAlt), background)
                        && !IsInputBackground(null, background), "探针未拒绝卡片色与缺失底色反例");
                    foreach (var dictionary in new[] { "Dark", "Light" })
                        Require(ThemeHost.ColorOf(dictionary, "TextControlBorderBrushFocused") == expected,
                            $"{theme.Id}/{dictionary} 全局焦点被重新涂成主题色");
                    Require(!IsNeutralInputBorder(new SolidColorBrush(ThemeHost.ToColor(theme.Colors.Accent)), expected),
                        "探针未拒绝强调色描边反例");
                    Require(!IsNeutralInputBorder(null, expected), "探针未拒绝缺失焦点描边反例");
                    var searchInput = Descendants(search).OfType<TextBox>().Single();
                    var numberInput = Descendants(number).OfType<TextBox>().Single(control => control.Name == "InputBox");
                    foreach (var outer in new Control[] { text, password, search, number, combo, multiline })
                        Require(outer.Background is SolidColorBrush color && color.Color == background,
                            $"{theme.Id}/{outer.Name} 外层仍用卡片底色");
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
                        reads.AppendLine($"{theme.Id}/{width}/{entry.Name}: background={background}, border={expected}, thickness={border.BorderThickness}");
                        await SaveProbeScreenAsync($"input-focus-{theme.Id}-{width}-{entry.Name}-screen.png");
                    }
                    Require(combo.Focus(FocusState.Keyboard), "下拉输入框不能取得键盘焦点");
                    await LayoutAsync(card);
                    var comboBackground = Descendants(combo).OfType<Border>().Single(control => control.Name == "Background");
                    Require(comboBackground.Background is SolidColorBrush comboFill && comboFill.Color == background,
                        $"{theme.Id} 闭合下拉聚焦时退回卡片底色");
                    await SaveProbeScreenAsync($"input-focus-{theme.Id}-{width}-combo-screen.png");
                    Require(shortcut.Focus(FocusState.Keyboard), "快捷键输入框不能取得键盘焦点");
                    Call(shortcut, "StartCapture");
                    try
                    {
                        await LayoutAsync(card);
                        var border = Descendants(shortcut).OfType<Border>().Single(control => control.Name == "ComboBorder");
                        Require(IsNeutralInputBorder(border.BorderBrush, expected), $"{theme.Id} 快捷键抓取仍有主题色外圈");
                        Require(border.Background is SolidColorBrush shortcutFill && shortcutFill.Color == background,
                            $"{theme.Id} 快捷键输入仍用卡片底色");
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
            _root.Children.Remove(card);
            ThemeHost.Apply(before);
            _root.Background = null;
            ResizeInspect(1420, 980);
        }
    }

    private static bool IsInputBackground(Color? actual, Color expected) => actual == expected;

    private static bool IsNeutralInputBorder(Brush? brush, Color expected) => brush is SolidColorBrush color && color.Color == expected;
}
