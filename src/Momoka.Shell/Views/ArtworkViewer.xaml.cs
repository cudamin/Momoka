using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Momoka.Shell.Views;

/// <summary>
/// 「看大图」那一张：一种图按对话框能给的面积摆出来，只读。
/// <para>
/// 图是调用方解好了交进来的（<c>CoverDialog.OnView</c>），这一张不碰网络 —— 同那张面板上每一个动作的规矩。
/// 它只负责把一张已经拿在手上的位图摆出来。
/// </para>
/// </summary>
public sealed partial class ArtworkViewer : ContentDialog
{
    /// <param name="caption">这是哪一种图。当标题用 —— 大图摆出来之后，人还得知道自己在看什么。</param>
    /// <param name="picture">解好的那张图。</param>
    public ArtworkViewer(string caption, BitmapImage picture)
    {
        InitializeComponent();

        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        Title = caption;
        Shot.Source = picture;

        // 影子要一块「投在哪」的面板：CastTo 指这块带余量的根，子视觉才会被裁在对话框内容区里，
        // 而不是裁在画框自己身上（那样四周一点都露不出来）。
        var shadow = (AttachedDropShadow)Resources["ViewerShadow"];
        shadow.CornerRadius = PictureFrame.CornerRadius.TopLeft;
        shadow.CastTo = ViewerRoot;
    }
}
