using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell.Views;

/// <summary>局部系统材质绘制在页面实色底之上；关闭或离页时释放，不改变窗口的播放背景。</summary>
internal static class DetailMica
{
    internal static void Apply(SystemBackdropElement surface, bool enabled, bool alternate = false)
    {
        if (!enabled)
        {
            surface.SystemBackdrop = null;
            return;
        }
        var kind = alternate ? MicaKind.BaseAlt : MicaKind.Base;
        if (surface.SystemBackdrop is not MicaBackdrop { Kind: var current } || current != kind)
            surface.SystemBackdrop = new MicaBackdrop { Kind = kind };
    }
}
