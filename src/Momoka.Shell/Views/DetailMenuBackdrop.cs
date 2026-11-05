using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Momoka.Theming;

namespace Momoka.Shell.Views;

/// <summary>详情菜单使用轻透的系统亚克力；主菜单和子菜单各自持有合成目标。</summary>
public sealed class DetailMenuBackdrop : SystemBackdrop
{
    private readonly Dictionary<ICompositionSupportsSystemBackdrop, DesktopAcrylicController> _controllers = [];
    private readonly Dictionary<ICompositionSupportsSystemBackdrop, SystemBackdropConfiguration> _configurations = [];

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        var color = ThemeHost.ToColor(ThemeHost.Current.Colors.SurfaceAlt);
        var controller = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Thin,
            TintColor = color,
            TintOpacity = 0.65f,
            LuminosityOpacity = 0.45f,
            FallbackColor = color
        };
        try
        {
            var attached = controller.AddSystemBackdropTarget(connectedTarget);
            var configuration = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);
            controller.SetSystemBackdropConfiguration(configuration);
            _controllers.Add(connectedTarget, controller);
            _configurations.Add(connectedTarget, configuration);
            Momoka.Diagnostics.Log.Debug("详情菜单材质", $"attached={attached} {MaterialState}");
        }
        catch
        {
            controller.Dispose();
            throw;
        }
    }

    internal bool UsesCardColor => _controllers.Count > 0 && _controllers.Values.All(controller =>
        controller.TintColor == ThemeHost.ToColor(ThemeHost.Current.Colors.SurfaceAlt)
        && controller.FallbackColor == controller.TintColor
        && Math.Abs(controller.TintOpacity - 0.65f) < 0.001f
        && Math.Abs(controller.LuminosityOpacity - 0.45f) < 0.001f);

    internal string MaterialState => string.Join(",", _controllers.Select(pair =>
        $"{pair.Value.State}/{pair.Value.Kind}/tint={pair.Value.TintOpacity:0.##}/luma={pair.Value.LuminosityOpacity:0.##}"
        + $"/color={pair.Value.TintColor}/active={_configurations[pair.Key].IsInputActive}"
        + $"/contrast={_configurations[pair.Key].IsHighContrast}/theme={_configurations[pair.Key].Theme}"));

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        if (!_controllers.Remove(disconnectedTarget, out var controller)) return;
        _configurations.Remove(disconnectedTarget);
        controller.RemoveSystemBackdropTarget(disconnectedTarget);
        controller.Dispose();
    }
}
