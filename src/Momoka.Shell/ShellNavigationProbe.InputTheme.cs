using System.Text;
using Momoka.Emby;
using Momoka.Shell.Views;
using Momoka.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
        try
        {
            var background = ThemeHost.ToColor(theme.Colors.Window);
            var glass = ThemeHost.ToColor(InputControlTheme.Glass(theme));
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
            AssertRadius(search, "设置搜索外层");
            AssertRadius(searchInput, "设置搜索内层");
            await FocusAsync(searchInput, "设置搜索");
            await AssertTextBorderAsync(searchInput, "设置搜索");
            await CaptureAsync($"settings-search-focus-{theme.Id}-{width}", page);

            // 窄窗还存在分类 ComboBox；按业务行名称选同一控件，不能随布局改成验证分类选择器。
            var combo = Descendants(page).OfType<ComboBox>()
                .Single(control => AutomationProperties.GetName(control) == "轮播图用什么媒体");
            AssertGlass(combo.Background, "闭合下拉框");
            await FocusAsync(combo, "闭合下拉框");
            var comboBorder = Descendants(combo).OfType<Border>().Single(control => control.Name == "Background");
            AssertRadius(combo, "闭合下拉框");
            Require(comboBorder.CornerRadius == new CornerRadius(10), "下拉框真实模板未接收设置圆角");
            AssertGlass(comboBorder.Background, "聚焦下拉框");
            var comboHighlight = Descendants(combo).OfType<Border>().Single(control => control.Name == "HighlightBackground");
            Require(comboHighlight.BorderBrush is SolidColorBrush focusedCombo && focusedCombo.Color == border,
                $"{theme.Id} 下拉框键盘焦点仍使用主题色");

            Require(!TextFocusSettled(searchInput, TextFrame(searchInput)), "焦点检查没有拒绝已经离焦的搜索框");
            var number = Descendants(page).OfType<NumberBox>()
                .Single(control => AutomationProperties.GetName(control) == "轮播图张数");
            AssertGlass(number.Background, "数字框外层");
            var numberInput = Descendants(number).OfType<TextBox>().Single(control => control.Name == "InputBox");
            AssertRadius(number, "数字框外层");
            AssertRadius(numberInput, "数字框内层");
            await FocusAsync(numberInput, "数字框");
            await AssertTextBorderAsync(numberInput, "数字框");
            await CaptureAsync($"settings-inputs-{theme.Id}-{width}", page);

            // 点空白处恢复：页面把焦点交给卡片区的滚动区（SettingsPage.OnSettingsPointerPressed），
            // 输入框随即失焦、从侧栏色回到输入玻璃。这里直接走同一个落点，验证的是那条恢复路径本身；
            // 指针点击如何路由到处理器由 Reader 的源码检查覆盖。
            var scroll = Get<ScrollViewer>(page, "SettingsScroll");
            scroll.Focus(FocusState.Programmatic);
            try
            {
                await UntilAsync(() => TextFrame(numberInput).Background is AcrylicBrush acrylic
                    && acrylic.TintColor == glass && !TextFocusSettled(numberInput, TextFrame(numberInput)));
            }
            catch (TimeoutException error)
            {
                throw new InvalidOperationException(
                    $"{theme.Id}/{width}/数字框 失焦后没有回到输入玻璃；{Describe(numberInput, TextFrame(numberInput))}", error);
            }
            reads.AppendLine($"数字框失焦恢复：玻璃 tint={glass}");
            await CaptureAsync($"settings-inputs-restored-{theme.Id}-{width}", page);

            page.SelectedCategory = "播放行为";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            var pathInput = Descendants(page).OfType<TextBox>().Single(control =>
                AutomationProperties.GetName(control) == "截图保存目录");
            AssertRadius(pathInput, "设置文本输入框");
            await FocusAsync(pathInput, "设置文本输入框");
            await AssertTextBorderAsync(pathInput, "设置文本输入框");
            await CaptureAsync($"settings-text-radius-{theme.Id}-{width}", page);
            page.SelectedCategory = "主页";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            searchInput = Descendants(search).OfType<TextBox>().Single();

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
                await AssertTextBorderAsync(nameBox, "元数据名称");
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

            void AssertBackground(Brush brush, string name)
            {
                Require(brush is SolidColorBrush solid && solid.Color == background, $"{theme.Id}/{name} 未使用侧边栏底色");
                reads.AppendLine($"{name}：背景={background}");
            }
            void AssertRadius(Control control, string name)
            {
                Require(control.CornerRadius == new CornerRadius(10), $"{theme.Id}/{width}/{name} 未使用设置页 10 DIP 圆角");
                if (control is TextBox text)
                    Require(TextFrame(text).CornerRadius == new CornerRadius(10), $"{name} 真实模板未接收设置圆角");
                reads.AppendLine($"{name}：圆角={control.CornerRadius}");
            }
            void AssertGlass(Brush brush, string name)
            {
                Require(brush is AcrylicBrush acrylic && acrylic.TintColor == glass, $"{theme.Id}/{name} 未使用输入玻璃");
                reads.AppendLine($"{name}：玻璃 tint={glass}");
            }
            async Task AssertTextBorderAsync(TextBox control, string name)
            {
                var frame = TextFrame(control);
                reads.AppendLine($"{name}：等待前 {Describe(control, frame)}");
                // Focus 成功与布局结束不代表 NumberBox 内部 InputBox 的 Focused storyboard 已经落定。
                // 不调用 GoToState 造状态；只等待实际焦点及原本要求的颜色、厚度同时成立。
                try { await UntilAsync(() => TextFocusSettled(control, frame)); }
                catch (TimeoutException error)
                {
                    throw new InvalidOperationException($"{theme.Id}/{width}/{name} 焦点样式未落定；期望边线={border}、厚度=1；{Describe(control, frame)}", error);
                }
                AssertBackground(frame.Background, name);
                Require(frame.BorderBrush is SolidColorBrush solid && solid.Color == border,
                    $"{theme.Id}/{width}/{name} 焦点线不是中性强边线；{Describe(control, frame)}");
                Require(frame.BorderThickness == new Thickness(1), $"{theme.Id}/{width}/{name} 焦点线四边厚度不是 1 DIP；{Describe(control, frame)}");
                reads.AppendLine($"{name}：{Describe(control, frame)}");
            }
            async Task FocusAsync(Control control, string name)
            {
                Require(control.Visibility == Visibility.Visible && Parents(control).OfType<FrameworkElement>()
                    .All(parent => parent.Visibility == Visibility.Visible), $"{theme.Id}/{width}/{name} 位于隐藏区域");
                if (Parents(control).OfType<ScrollViewer>().FirstOrDefault() is { } scroll)
                {
                    control.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
                    try { await UntilAsync(() => InViewport(control, scroll)); }
                    catch (TimeoutException error)
                    {
                        throw new InvalidOperationException($"{theme.Id}/{width}/{name} 没有完整进入视口；{Describe(control)}；视口={scroll.ActualWidth}×{scroll.ActualHeight}，偏移={scroll.VerticalOffset}", error);
                    }
                }
                Require(control.Focus(FocusState.Keyboard), $"{theme.Id}/{name} 未收到键盘焦点");
                await LayoutAsync(control);
                Require(ReferenceEquals(FocusManager.GetFocusedElement(control.XamlRoot), control),
                    $"{theme.Id}/{width}/{name} 焦点被其他控件截走；{Describe(control)}");
            }
            bool TextFocusSettled(TextBox control, Border frame) =>
                ReferenceEquals(FocusManager.GetFocusedElement(control.XamlRoot), control)
                && control.FocusState != FocusState.Unfocused
                && TextState(control) == "Focused"
                && frame.Background is SolidColorBrush fill && fill.Color == background
                && frame.BorderBrush is SolidColorBrush line && line.Color == border
                && frame.BorderThickness == new Thickness(1);

            static Border TextFrame(TextBox control) => Descendants(control).OfType<Border>()
                .Single(element => element.Name == "BorderElement");

            static string TextState(TextBox control) => Descendants(control).OfType<FrameworkElement>()
                .SelectMany(VisualStateManager.GetVisualStateGroups)
                .FirstOrDefault(group => group.Name == "CommonStates" && group.States.Any(state => state.Name == "Focused"))
                ?.CurrentState?.Name ?? "未找到";

            static bool InViewport(FrameworkElement control, ScrollViewer scroll)
            {
                var bounds = control.TransformToVisual(scroll).TransformBounds(
                    new Windows.Foundation.Rect(0, 0, control.ActualWidth, control.ActualHeight));
                return control.ActualWidth > 0 && control.ActualHeight > 0 && bounds.Left >= -1 && bounds.Top >= -1
                    && bounds.Right <= scroll.ActualWidth + 1 && bounds.Bottom <= scroll.ActualHeight + 1;
            }

            static IEnumerable<DependencyObject> Parents(DependencyObject child)
            {
                for (var parent = VisualTreeHelper.GetParent(child); parent is not null; parent = VisualTreeHelper.GetParent(parent))
                    yield return parent;
            }

            static string Describe(Control control, Border? frame = null) =>
                $"控件={control.GetType().Name}/{control.Name}，名称={AutomationProperties.GetName(control)}，焦点={control.FocusState}，"
                + $"实际焦点={(FocusManager.GetFocusedElement(control.XamlRoot) as FrameworkElement)?.Name ?? "无"}，"
                + $"状态={(control is TextBox text ? TextState(text) : "非文本框")}，尺寸={control.ActualWidth:0.0}×{control.ActualHeight:0.0}，"
                + $"背景={BrushText(frame?.Background)}，边线={BrushText(frame?.BorderBrush)}，厚度={frame?.BorderThickness.ToString() ?? "无"}";

            static string BrushText(Brush? brush) => brush is SolidColorBrush solid ? solid.Color.ToString() : brush?.GetType().Name ?? "无";
            async Task CaptureAsync(string name, FrameworkElement element)
            {
                await SaveReturnFrameAsync(element, $"{name}-render.png");
                await WaitForDiagnosticsFrameAsync();
                await CompositionPlaybackProbe.SaveScreenAsync(handle, Path.Combine(_options.Paths.LogDirectory, $"{name}-screen.png"));
            }
        }
        catch (Exception error)
        {
            reads.AppendLine("失败：" + error.Message);
            throw;
        }
        finally
        {
            File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, $"input-theme-{theme.Id}-{width}.txt"), reads.ToString());
        }
    }
}
