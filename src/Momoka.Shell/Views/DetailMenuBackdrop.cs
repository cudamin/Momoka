using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Momoka.Diagnostics;
using Momoka.Theming;

namespace Momoka.Shell.Views;

/// <summary>详情菜单使用轻透的系统亚克力；主菜单和子菜单各自持有合成目标。</summary>
public sealed class DetailMenuBackdrop : SystemBackdrop
{
    private const string Category = "详情菜单材质";
    private readonly Dictionary<ICompositionSupportsSystemBackdrop, Entry> _entries = [];
    internal int ConnectedTargetCount => _entries.Values.Count(entry => entry.Controller is not null);
    internal int SolidTargetCount => _entries.Values.Count(entry => entry.Controller is null);
    internal int TargetCount => _entries.Count;
    internal bool NeedsSolidSurface => _entries.Count == 0 || SolidTargetCount > 0;
    internal event Action? SurfaceChanged;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        DesktopAcrylicController? controller = null;
        SystemBackdropConfiguration? configuration = null;
        var addAttempted = false;
        try
        {
            configuration = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);
            if (DesktopAcrylicController.IsSupported())
            {
                // 先取得所有权再设置属性，setter 失败也能释放已创建的 controller。
                controller = new DesktopAcrylicController();
                controller.Kind = DesktopAcrylicKind.Thin;
                Paint(controller);
                controller.SetSystemBackdropConfiguration(configuration);
                addAttempted = true;
                if (controller.AddSystemBackdropTarget(connectedTarget))
                {
                    AddEntry(connectedTarget, new Entry(controller, configuration));
                    controller = null;
                    NotifySurface();
                    try { Log.Debug(Category, "亚克力目标已连接：" + MaterialState); }
                    catch (Exception error) { Log.Warn(Category, "读取菜单材质状态失败", error); }
                    return;
                }
                Log.Warn(Category, "亚克力目标拒绝连接，尝试实色回退");
            }
            else Log.Warn(Category, "本机不支持桌面亚克力，尝试实色回退");
        }
        catch (Exception error) { Log.Warn(Category, "初始化亚克力失败，尝试实色回退", error); }
        finally
        {
            if (controller is not null) ReleaseController(controller, connectedTarget, addAttempted);
        }

        // 当前接口的 SystemBackdrop 是 Windows.UI.Composition.Brush，不能直接接 XAML 的
        // Microsoft.UI.Composition.Brush。回退由 presenter 的 XAML 实色层提供，不另造合成桥。
        AddEntry(connectedTarget, new Entry(null, configuration));
        NotifySurface();
    }

    private void AddEntry(ICompositionSupportsSystemBackdrop target, Entry entry)
    {
        _entries.Add(target, entry);
        if (_entries.Count == 1) ThemeHost.Changed += OnThemeChanged;
    }

    private void OnThemeChanged(UiTheme theme)
    {
        foreach (var entry in _entries.Values) Update(entry);
        NotifySurface();
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(
        ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
        if (!_entries.TryGetValue(target, out var entry)) return;
        try
        {
            entry.Configuration = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
            Update(entry);
            NotifySurface();
        }
        catch (Exception error) { Log.Warn(Category, "更新菜单系统配置失败", error); }
    }

    private void Update(Entry entry)
    {
        try
        {
            if (entry.Controller is { } controller) Paint(controller);
        }
        catch (Exception error) { Log.Warn(Category, "更新菜单颜色失败", error); }
    }

    private static void Paint(DesktopAcrylicController controller)
    {
        var color = ThemeHost.ToColor(ThemeHost.Current.Colors.SurfaceAlt);
        controller.TintColor = color;
        controller.FallbackColor = color;
        controller.TintOpacity = 0.65f;
        controller.LuminosityOpacity = 0.45f;
    }

    internal bool UsesCardColor => _entries.Count > 0 && _entries.Values.All(entry =>
        entry.Controller is { } controller
            ? controller.TintColor == ThemeHost.ToColor(ThemeHost.Current.Colors.SurfaceAlt)
                && controller.FallbackColor == controller.TintColor
                && Math.Abs(controller.TintOpacity - 0.65f) < 0.001f
                && Math.Abs(controller.LuminosityOpacity - 0.45f) < 0.001f
            : true);

    internal string MaterialState => string.Join(",", _entries.Values.Select(entry =>
        (entry.Controller is { } controller
            ? $"{controller.State}/{controller.Kind}/tint={controller.TintOpacity:0.##}/luma={controller.LuminosityOpacity:0.##}"
                + $"/color={controller.TintColor}"
            : "PresenterSolid")
        + $"/active={entry.Configuration?.IsInputActive}/contrast={entry.Configuration?.IsHighContrast}"
        + $"/theme={entry.Configuration?.Theme}"));

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        try { base.OnTargetDisconnected(disconnectedTarget); }
        finally
        {
            if (_entries.Remove(disconnectedTarget, out var entry))
            {
                if (_entries.Count == 0) ThemeHost.Changed -= OnThemeChanged;
                if (entry.Controller is { } controller) ReleaseController(controller, disconnectedTarget, true);
                NotifySurface();
            }
        }
    }

    private static void ReleaseController(
        DesktopAcrylicController controller, ICompositionSupportsSystemBackdrop target, bool remove)
    {
        try { if (remove) controller.RemoveSystemBackdropTarget(target); }
        catch (Exception error) { Log.Warn(Category, "移除菜单亚克力目标失败", error); }
        finally
        {
            try { controller.Dispose(); }
            catch (Exception error) { Log.Warn(Category, "释放菜单亚克力控制器失败", error); }
        }
    }

    private void NotifySurface()
    {
        if (SurfaceChanged is not { } changed) return;
        foreach (Action handler in changed.GetInvocationList())
        {
            try { handler(); }
            catch (Exception error) { Log.Warn(Category, "更新菜单回退表面失败", error); }
        }
    }

    private sealed class Entry(
        DesktopAcrylicController? controller, SystemBackdropConfiguration? configuration)
    {
        internal DesktopAcrylicController? Controller { get; } = controller;
        internal SystemBackdropConfiguration? Configuration { get; set; } = configuration;
    }
}
