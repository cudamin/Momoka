using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

/// <summary>
/// 侧边栏右沿那条拖动条：一个只多了一样东西的 <see cref="Grid"/> —— 能从外面设它的光标。
/// <para>
/// <c>UIElement.ProtectedCursor</c> 是 protected 的，只有派生类碰得到，所以要它就得有自己的类型
/// （同 <see cref="PictureSurface"/>）。指针压在这条透明命中带上时，屏上那只光标由框架的输入管线画，
/// 设成左右箭头才知道这里可以拖。
/// </para>
/// </summary>
internal sealed partial class SidebarResizeGrip : Grid
{
    /// <summary>
    /// 这条拖动条此刻要什么形状的光标。<c>null</c> 是「交回框架」，也就是平常的箭头。
    /// </summary>
    internal InputCursor? Cursor
    {
        get => ProtectedCursor;
        set => ProtectedCursor = value;
    }
}
