using System.Text;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>真实卡片与设置模板；只移动键盘焦点，不调用卡片动作、设置服务或网络。</summary>
    private async Task CardFocusAndSettingNamesAsync()
    {
        ResizeInspect(1100, 900);
        var content = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
        var exit = new Button { Name = "FocusProbeExit", Content = "焦点移出卡片" };
        content.Children.Add(exit);
        _root.Children.Add(content);
        var readings = new StringBuilder();
        try
        {
            foreach (var poster in new[] { true, false })
                foreach (var itemContainer in new[] { false, true })
                {
                    UserControl card = poster ? new PosterCard() : new EpisodeRow();
                    var name = card.GetType().Name + (itemContainer ? "/ItemContainer" : "/Button");
                    Control host = itemContainer
                        ? new ItemContainer { Child = card, IsTabStop = true }
                        : new Button { Content = card };
                    host.Width = 320;
                    host.Height = 120;
                    content.Children.Add(host);
                    try
                    {
                        await LayoutAsync(content);
                        await UntilAsync(() => card.IsLoaded);
                        var watch = Get<FocusWatch>(card, "_focus");
                        Require(ReferenceEquals(Get<Control>(watch, "_host"), host), name + " 焦点监听没有绑定外层单项容器");
                        Require(exit.Focus(FocusState.Keyboard), "离焦目标不能取得焦点");
                        await UntilAsync(() => !Get<bool>(card, "_focused"));
                        Require(host.Focus(FocusState.Keyboard), name + " 宿主不能取得焦点");
                        await UntilAsync(() => ReferenceEquals(FocusManager.GetFocusedElement(host.XamlRoot), host)
                            && Get<bool>(card, "_focused"));
                        Require(watch.Probe(), name + " 当前宿主诊断失败");
                        Require(exit.Focus(FocusState.Keyboard), "离焦目标不能再次取得焦点");
                        await UntilAsync(() => !Get<bool>(card, "_focused"));
                        readings.AppendLine(name + "：宿主身份、键盘进入和离开均通过");
                    }
                    finally
                    {
                        content.Children.Remove(host);
                        // ItemContainer.Child does not accept null. Removing the whole temporary host
                        // ends its visual lifetime; no child is reused by the next case.
                        await LayoutAsync(content);
                    }
                }

            // 将真实设置 DataTemplate 交给 ItemsControl 实例化，既验证绑定结果，也验证 UIA peer 的最终名称。
            var page = new SettingsPage();
            var selector = (SettingRowTemplates)page.Resources["EgSettingRows"];
            var writes = 0;
            SettingRow[] rows =
            [
                new SettingSliderRow("字号", null, 10, 100, 1, 50, _ => writes++, () => writes++),
                new SettingSliderRow("字幕缩放（%）", null, 50, 200, 5, 100, _ => writes++, () => writes++),
                new SettingColorRow("文字颜色", null, "#FFFFFF", _ => writes++, () => writes++),
                new SettingColorRow("描边颜色", null, "#000000", _ => writes++, () => writes++)
            ];
            var list = new ItemsControl { ItemsSource = rows, ItemTemplateSelector = selector };
            content.Children.Add(list);
            await LayoutAsync(content);
            var controls = Descendants(list).OfType<Control>()
                .Where(control => control is Slider || control is Button { Flyout: not null }).ToArray();
            Require(controls.Length == rows.Length, $"设置控件数量不符：{controls.Length}");
            foreach (var row in rows)
            {
                var control = controls.SingleOrDefault(candidate => AutomationProperties.GetName(candidate) == row.Label);
                Require(control is not null, "设置控件未使用具体名称：" + row.Label);
                Require(FrameworkElementAutomationPeer.CreatePeerForElement(control!)?.GetName() == row.Label,
                    "UIA peer 没有暴露具体名称：" + row.Label);
                Require(control!.Focus(FocusState.Keyboard), "设置控件不能接受键盘焦点：" + row.Label);
                readings.AppendLine("设置名称：" + row.Label);
            }
            Require(writes == 0, "只读取名称和改变焦点却提交了设置");

            // 负向用例：退回旧的统一名称时，同一断言必须能检测到差异。
            var first = controls.OfType<Slider>().First();
            var originalName = AutomationProperties.GetName(first);
            AutomationProperties.SetName(first, "mpv 参数滑块");
            Require(FrameworkElementAutomationPeer.CreatePeerForElement(first)?.GetName() != originalName,
                "名称检查未能识别旧的通用名称");
            AutomationProperties.SetName(first, originalName);
            await SaveProbeScreenAsync("card-focus-setting-names-screen.png");
            await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "card-focus-setting-names.txt"), readings.ToString());
        }
        finally
        {
            _root.Children.Remove(content);
        }
    }
}
