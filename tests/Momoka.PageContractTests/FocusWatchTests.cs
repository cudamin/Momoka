using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class FocusWatchTests
{
    internal static void Register()
    {
        foreach (var poster in new[] { true, false })
            foreach (var button in new[] { true, false })
            {
                Test($"焦点宿主：{(poster ? "海报" : "剧集行")}越过自身并订阅{(button ? "按钮" : "项目容器")}", () =>
                {
                    var card = poster ? (UserControl)new PosterCard() : new EpisodeRow();
                    var host = button ? (Control)new Button() : new ItemContainer();
                    var outer = new ItemsView();
                    host.TreeParent = outer;
                    card.TreeParent = new FrameworkElement { TreeParent = host };
                    var inside = new FrameworkElement { TreeParent = new FrameworkElement { TreeParent = card } };
                    var changes = new List<bool>();
                    var watch = new FocusWatch(inside, changes.Add);

                    watch.Attach();
                    watch.Attach();
                    Assert.True(watch.Probe());
                    Assert.Equal(1, host.GotHandlers, "重复 Attach 不重复订阅");
                    Assert.Equal(0, card.GotHandlers, "不能接在 UserControl 自身");
                    Assert.Equal(0, outer.GotHandlers, "不能接在整张列表");
                    Assert.True(changes.SequenceEqual([false]));

                    host.RaiseGotFocus();
                    Assert.True(changes.Last(), "真实宿主事件必须到达 paint");
                    host.RaiseLostFocus();
                    Assert.False(changes.Last());
                    watch.Detach();
                    Assert.Equal(0, host.GotHandlers);
                    Assert.Equal(0, host.LostHandlers);
                    var detachedCount = changes.Count;
                    host.RaiseGotFocus();
                    Assert.Equal(detachedCount, changes.Count, "释放后旧容器不能改变卡片状态");

                    var replacement = new Button { FocusState = FocusState.Keyboard };
                    card.TreeParent = replacement;
                    watch.Attach();
                    Assert.True(changes.Last(), "重新挂接应读取新容器当前焦点");
                    Assert.Equal(1, replacement.GotHandlers);
                    Assert.Equal(0, host.GotHandlers);
                    card.TreeParent = host;
                    Assert.False(watch.Probe(), "宿主已移走时不能仅凭非空宣称焦点接线有效");
                    watch.Detach();
                });
            }

        Test("焦点宿主：没有单项容器时不借用列表或滚动器的焦点", () =>
        {
            foreach (var boundary in new Control[] { new ItemsView(), new ScrollView(), new UserControl() })
            {
                var unrelatedButton = new Button();
                boundary.TreeParent = unrelatedButton;
                var card = new PosterCard { TreeParent = boundary };
                var inside = new FrameworkElement { TreeParent = card };
                var changes = new List<bool>();
                var watch = new FocusWatch(inside, changes.Add);
                watch.Attach();
                Assert.False(watch.Probe());
                Assert.Equal(0, card.GotHandlers);
                Assert.Equal(0, boundary.GotHandlers);
                Assert.Equal(0, unrelatedButton.GotHandlers, "不能穿过集合边界误认外层按钮");
                boundary.RaiseGotFocus();
                Assert.Equal(0, changes.Count);
            }
        });

        Test("焦点宿主：未进入卡片或未入树时不误认任意按钮", () =>
        {
            var button = new Button();
            var watch = new FocusWatch(new FrameworkElement { TreeParent = button }, _ =>
                throw new AssertionException("无卡片边界不应订阅或绘制"));
            watch.Attach();
            Assert.False(watch.Probe());
            Assert.Equal(0, button.GotHandlers);
        });
    }
}
