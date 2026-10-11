using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

/// <summary>
/// 「刷新元数据」那张表（照 Emby 网页端 <c>components/refreshdialog</c>）。这个类只把表上那三处选择读出来 ——
/// 它们各自落到服务器哪几条查询参数上，是 <c>EmbyClient.RefreshItemAsync</c> 的事，不在这里。
/// <para>
/// 三处都按控件的当前状态现读，不另存一份字段：存一份就得跟着三个事件更新，而只要有一个事件忘了接，读到的那份
/// 就和用户看到的表对不上。
/// </para>
/// </summary>
public sealed partial class RefreshMetadataDialog : ContentDialog
{
    public RefreshMetadataDialog()
    {
        InitializeComponent();

        // 同 MetadataDialog 那段说明：对话框摆在 XamlRoot 的浮层根上，从提出它的那棵树上继承不到主题，不点明
        // 就按 Windows 的应用模式来 —— 用户在晴昼（唯一那套浅色）下会得到一张深色的表。
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
    }

    /// <summary>
    /// 表上选的那一档：<c>missing</c>（搜索缺少的元数据）或 <c>all</c>（替换所有元数据）。
    /// <para>
    /// 认的是索引而不是下拉里那个字符串：字符串是给人看的，服务器认的是这两个词（见
    /// <c>EmbyClient.RefreshItemAsync</c> 的守卫），中间再夹一层翻译迟早会有一天对不上。默认选中的是
    /// <c>all</c> —— 网页端那张表也是它。
    /// </para>
    /// </summary>
    public string Mode => ModeBox.SelectedIndex == 0 ? "missing" : "all";

    /// <summary>「替换现有图像」那一颗。开着会先删光现有的图再重下。</summary>
    public bool ReplaceImages => ImagesSwitch.IsOn;

    /// <summary>「替换现有视频预览缩略图」那一颗。同上，管的是预览缩略图。</summary>
    public bool ReplaceThumbnails => ThumbnailsSwitch.IsOn;
}
