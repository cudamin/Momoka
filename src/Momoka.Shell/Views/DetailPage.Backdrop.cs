using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Theming;
using Windows.UI;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    // 阅读保护固定在图片层，滚动内容和标题栏不再重复叠暗。沉浸背景也必须压住任意位置的亮部。
    // 2026-11-08 用户令「窄窗口的背景图不要加亚克力」：窄窗那一档这块材料不上，压在图上的回到下面那道
    // 40% 实色罩子（PaintScrim 的 HeroTail／HeroBand 那一支）。规则本体在 Core，窄窗与页面类型各一句。
    private bool UsesBackdropFade => DetailHero.BackdropMaterial(
        ViewModel.BackdropShown, NarrowWindow, ViewModel.Showcase, ViewModel.ItemType);

    /// <summary>窗口窄过紧凑断点（<see cref="DetailHero.CompactFloor"/>）；还没量到宽时不算窄。</summary>
    private bool NarrowWindow => ViewModel.PageWidth is > 0 and < DetailHero.CompactFloor;

    /// <summary>
    /// 没上亚克力那一层时的阅读底色（窄窗与集页卡面）：固定的 40% 实色罩子，和 2026-11-06 那版
    /// 「背景图不透明度」的出厂值同一个观感。背景固定亚克力之后，别处没有再画这一层的地方。
    /// </summary>
    private Color ReadingShadeColor
    {
        get
        {
            var ink = ShellPrefs.AppearanceColor(true, ViewModel.AppearanceSettings);
            return Color.FromArgb(AppearanceOpacity.ScrimAlpha(DetailHero.ScrimCeiling, AppearanceOpacity.ReadingVeil),
                ink.R, ink.G, ink.B);
        }
    }

    private void PaintBackdropFade()
    {
        // smoothstep 两端斜率归零；同一主题RGB只改变alpha，避开透明黑造成的脏边。
        var color = _actionAccessibility.HighContrast
            ? _actionUiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)
            : ThemeHost.ToColor(ThemeHost.Current.Colors.Window);
        BackdropEdgeBrush.GradientStops.Clear();
        BackdropEdgeBrush.GradientStops.Add(new GradientStop { Offset = 0, Color = Color.FromArgb(0, color.R, color.G, color.B) });
        for (var step = 0; step <= 32; step++)
        {
            var t = step / 32d;
            var alpha = (byte)Math.Round(255 * t * t * (3 - 2 * t));
            BackdropEdgeBrush.GradientStops.Add(new GradientStop
            {
                Offset = 0.8 + 0.2 * t,
                Color = Color.FromArgb(alpha, color.R, color.G, color.B)
            });
        }
        BackdropEdgeFade.Visibility = ViewModel.Showcase ? Visibility.Collapsed : Visibility.Visible;
        // 背景固定走亚克力（2026-11-07 用户令「去掉高斯模糊和颜色遮罩」）：可调的只有下面三项 ——
        // 染色颜色、染色浓度、明暗浓度。不另加整体浓度，也不再往图上压一道纯色遮罩。
        // 系统关掉透明效果（省电、远程桌面）时平台落到 FallbackColor：一整块不透明的主题表面，
        // 图不再透上来，字照旧读得出。高对比度直接给实色，绕开材质。
        if (_actionAccessibility.HighContrast)
        {
            BackdropFade.Background = new SolidColorBrush(color);
        }
        else
        {
            var tint = ThemeHost.ToColor(ShellPrefs.AppearanceColor(true, ViewModel.AppearanceSettings));
            tint.A = byte.MaxValue;
            BackdropFade.Background = new AcrylicBrush
            {
                TintColor = tint,
                FallbackColor = tint,
                TintOpacity = ViewModel.BackdropTintOpacity,
                TintLuminosityOpacity = ViewModel.BackdropTintLuminosityOpacity,
                Opacity = 1,
                AlwaysUseFallback = false,
                TintTransitionDuration = TimeSpan.Zero
            };
        }
        BackdropFade.Visibility = UsesBackdropFade ? Visibility.Visible : Visibility.Collapsed;
    }
}
