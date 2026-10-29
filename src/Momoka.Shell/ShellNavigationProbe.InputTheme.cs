using System.Text;
using Momoka.Emby;
using Momoka.Shell.Views;
using Momoka.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 复用设置搜索的隔离窗口，验证真实模板的背景和焦点线，并拍元数据表单。
    /// 只聚焦控件，不发送鼠标输入、不点保存、不调用真实服务器。
    /// </summary>
    private async Task InputThemeControlsAsync(Window window, SettingsPage page, IntPtr handle, UiTheme theme, int width)
    {
        var reads = new StringBuilder();
        var background = ThemeHost.ToColor(theme.Colors.Window);
        var border = ThemeHost.ToColor(theme.Colors.BorderStrong);
        foreach (var entry in InputControlTheme.Brushes)
            foreach (var dictionary in new[] { "Dark", "Light" })
                Require(ThemeHost.ColorOf(dictionary, entry.Key) == ThemeHost.ToColor(entry.Pick(theme)),
                    $"{theme.Id}/{dictionary}/{entry.Key} 未由真实 ThemeHost 更新");

        page.SelectedCategory = "主页";
        await LayoutAsync(page);
        await WaitForSettingsSettledAsync(page);
        var search = Get<AutoSuggestBox>(page, "SettingsSearchBox");
        AssertBackground(search.Background, "设置搜索外层");
        var searchInput = Descendants(search).OfType<TextBox>().Single();
        await FocusAsync(searchInput, "设置搜索");
        AssertTextBorder(searchInput, "设置搜索");
        await CaptureAsync($"settings-search-focus-{theme.Id}-{width}", page);

        var combo = Descendants(page).OfType<ComboBox>().First(control => control.ActualWidth > 0);
        AssertBackground(combo.Background, "闭合下拉框");
        await FocusAsync(combo, "闭合下拉框");
        var comboBorder = Descendants(combo).OfType<Border>().Single(control => control.Name == "Background");
        AssertBackground(comboBorder.Background, "聚焦下拉框");
        var comboHighlight = Descendants(combo).OfType<Border>().Single(control => control.Name == "HighlightBackground");
        Require(comboHighlight.BorderBrush is SolidColorBrush focusedCombo && focusedCombo.Color == border,
            $"{theme.Id} 下拉框键盘焦点仍使用主题色");

        var number = Descendants(page).OfType<NumberBox>().First(control => control.ActualWidth > 0);
        AssertBackground(number.Background, "数字框外层");
        var numberInput = Descendants(number).OfType<TextBox>().Single(control => control.Name == "InputBox");
        await FocusAsync(numberInput, "数字框");
        AssertTextBorder(numberInput, "数字框");
        await CaptureAsync($"settings-inputs-{theme.Id}-{width}", page);

        var name = "猫与龙";
        var dialog = new MetadataDialog(new EmbyItem { Id = "offline-input-theme", Name = name, Type = EmbyItemType.Movie },
            new ItemMetadataEdit { Name = name })
        { XamlRoot = page.XamlRoot };
        ThemeHost.Register(dialog);
        var showing = dialog.ShowAsync();
        try
        {
            await UntilAsync(() => Get<TextBox>(dialog, "NameBox").ActualWidth > 0);
            var nameBox = Get<TextBox>(dialog, "NameBox");
            await FocusAsync(nameBox, "元数据名称");
            AssertTextBorder(nameBox, "元数据名称");
            Require(nameBox.Text == name, "聚焦更换主题改变了表单值");
            await CaptureAsync($"metadata-inputs-{theme.Id}-{width}", dialog);
        }
        finally
        {
            dialog.Hide();
            await showing;
        }

        // 同一正式控件用于快捷键设置；调用实际抓键入口，不发送组合键或持久化任何偏好。
        var capture = new ShortcutCaptureBox();
        var password = new PasswordBox { Header = "密码", Password = "offline-placeholder" };
        var inputs = new StackPanel { Spacing = 12 };
        inputs.Children.Add(password);
        inputs.Children.Add(capture);
        var inputDialog = new ContentDialog
        {
            Title = "离线输入样式",
            Content = inputs,
            CloseButtonText = "关闭",
            XamlRoot = page.XamlRoot
        };
        ThemeHost.Register(inputDialog);
        var inputShowing = inputDialog.ShowAsync();
        try
        {
            await UntilAsync(() => password.ActualWidth > 0);
            await FocusAsync(password, "密码框");
            var passwordBorder = Descendants(password).OfType<Border>().Single(control => control.Name == "BorderElement");
            AssertBackground(passwordBorder.Background, "密码框");
            Require(passwordBorder.BorderBrush is SolidColorBrush passwordFocus && passwordFocus.Color == border,
                $"{theme.Id} 密码框仍包主题色");
            await FocusAsync(capture, "快捷键抓键框");
            Call(capture, "StartCapture");
            await LayoutAsync(capture);
            var captureBorder = Get<Border>(capture, "ComboBorder");
            AssertBackground(captureBorder.Background, "快捷键抓键框");
            Require(captureBorder.BorderBrush is SolidColorBrush captureFocus && captureFocus.Color == border,
                $"{theme.Id} 快捷键抓键框仍包主题色");
            await CaptureAsync($"shortcut-inputs-{theme.Id}-{width}", inputDialog);
        }
        finally
        {
            inputDialog.Hide();
            await inputShowing;
        }
        await FocusAsync(searchInput, "返回设置搜索");
        File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, $"input-theme-{theme.Id}-{width}.txt"), reads.ToString());

        void AssertBackground(Brush brush, string name)
        {
            Require(brush is SolidColorBrush solid && solid.Color == background, $"{theme.Id}/{name} 未使用侧边栏底色");
            reads.AppendLine($"{name}：背景={background}");
        }
        void AssertTextBorder(TextBox control, string name)
        {
            var frame = Descendants(control).OfType<Border>().Single(element => element.Name == "BorderElement");
            AssertBackground(frame.Background, name);
            Require(frame.BorderBrush is SolidColorBrush solid && solid.Color == border,
                $"{theme.Id}/{name} 焦点线不是中性强边线");
            Require(frame.BorderThickness == new Thickness(1), $"{theme.Id}/{name} 焦点线四边厚度不是 1 DIP");
            reads.AppendLine($"{name}：焦点={control.FocusState}；边线={border}；厚度={frame.BorderThickness}");
        }
        async Task FocusAsync(Control control, string name)
        {
            Require(control.Focus(FocusState.Keyboard), $"{theme.Id}/{name} 未收到键盘焦点");
            await LayoutAsync(control);
            Require(ReferenceEquals(FocusManager.GetFocusedElement(control.XamlRoot), control),
                $"{theme.Id}/{name} 焦点被其他控件截走");
        }
        async Task CaptureAsync(string name, FrameworkElement element)
        {
            await SaveReturnFrameAsync(element, $"{name}-render.png");
            await WaitForDiagnosticsFrameAsync();
            await CompositionPlaybackProbe.SaveScreenAsync(handle, Path.Combine(_options.Paths.LogDirectory, $"{name}-screen.png"));
        }
    }
}
