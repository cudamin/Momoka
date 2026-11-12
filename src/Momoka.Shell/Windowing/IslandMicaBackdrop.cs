using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Momoka.Diagnostics;

namespace Momoka.Shell.Windowing;

/// <summary>
/// 主窗口那块云母：配置仍用框架给的那一份，只把 <c>IsInputActive</c> 换成宿主窗口真正在说的那个值。
/// <para>
/// <b>为什么不能用框架的 <c>MicaBackdrop</c>。</b>它在 <c>OnTargetConnected</c> 里新建一个
/// <c>MicaController</c>，配置取自 <c>GetDefaultSystemBackdropConfiguration</c> —— 那份配置的
/// <c>IsInputActive</c> 跟着 <c>XamlRoot.IsInputActive</c> 走（框架 <c>SystemBackdrop_Partial.cpp</c> 的
/// <c>AttachInputActivationChanged</c>）。XAML 岛没有窗口激活：被激活的是宿主 HWND，岛只是它的子窗口，
/// 所以那份配置里的值是假，云母按「窗口不在前台」渲染成实心底 —— 2026-11-12 实测 <c>#202020</c>，一块纯灰，
/// 壁纸一点没采（同一台机器、同一张蓝色壁纸下，设置窗口走框架 <c>Window</c>，它的 XamlRoot 拿得到激活，
/// 云母实测 <c>45,57,70</c> 带蓝）。这就是「云母完全看不出桌面颜色」的根因，与画布浓度无关。
/// </para>
/// <para>
/// <b>为什么仍借框架那份配置。</b>主题与高对比度由框架挂在这份配置上、跟着 <c>ActualThemeChanged</c> /
/// <c>HighContrastChanged</c> 自动更新，另起一份就得自己抄一遍这些规则。这里只覆盖一项，其余照旧。
/// 覆盖要顶得住：框架那边的 <c>InputActivationChanged</c> 一旦触发会把值写回去，所以
/// <see cref="OnDefaultSystemBackdropConfigurationChanged"/> 里再钉一次。
/// </para>
/// </summary>
internal sealed class IslandMicaBackdrop : SystemBackdrop
{
    private const string Category = "窗口云母";

    private readonly Dictionary<ICompositionSupportsSystemBackdrop, Entry> _entries = [];

    /// <summary>
    /// 宿主窗口是否在前台。窗口还没收到过一次 <c>WM_ACTIVATE</c> 时按前台算 —— 刚起来的那一帧本来就该是亮的，
    /// 而「不在前台」那一档是实心灰，猜错会让第一帧平白暗一下。
    /// </summary>
    private bool _inputActive = true;

    /// <summary>宿主窗口的激活状态。由 <c>HostWindow</c> 的 <c>FocusChanged</c> 喂。</summary>
    internal void SetInputActive(bool active)
    {
        if (_inputActive == active) return;

        _inputActive = active;
        foreach (var entry in _entries.Values)
        {
            entry.Configuration.IsInputActive = active;
        }
    }

    /// <summary>换主题时重钉一次深浅。云母只认配置里的 <c>Theme</c>，画刷改了它不知道。</summary>
    internal void ApplyTheme()
    {
        var theme = ThemeHost.Current.IsDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        foreach (var entry in _entries.Values)
        {
            entry.Configuration.Theme = theme;
        }
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        if (!MicaController.IsSupported())
        {
            // 没有云母就没有：底由窗口画布给，与材质失败那条路一样，不假装挂上了。
            Log.Warn(Category, "本机不支持云母，窗口退回画布实色");
            return;
        }

        var controller = new MicaController { Kind = MicaKind.Base };
        var configuration = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);

        Log.Info(
            Category,
            $"框架配置：active={configuration.IsInputActive} theme={configuration.Theme}"
            + $" contrast={configuration.IsHighContrast} → 改成 active={_inputActive}");

        configuration.IsInputActive = _inputActive;

        try
        {
            var attached = controller.AddSystemBackdropTarget(connectedTarget);
            controller.SetSystemBackdropConfiguration(configuration);
            _entries[connectedTarget] = new Entry(controller, configuration);
            Log.Info(Category, $"云母已挂上：attached={attached} state={controller.State}");
        }
        catch (Exception error)
        {
            // 云母是锦上添花：挂不上还有画布那层底，窗口照旧能用。
            controller.Dispose();
            Log.Warn(Category, "挂云母失败，窗口退回画布实色", error);
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);

        if (!_entries.Remove(disconnectedTarget, out var entry)) return;

        entry.Controller.RemoveSystemBackdropTarget(disconnectedTarget);
        entry.Controller.Dispose();
    }

    /// <summary>
    /// 框架重算完那份配置之后钉回自己的值。这条会在换内容、换主题、换高对比度时被调用；<c>IsInputActive</c>
    /// 是框架从 XamlRoot 读的，而岛上的 XamlRoot 永远说「不在前台」，所以每次都要改回来。
    /// </summary>
    protected override void OnDefaultSystemBackdropConfigurationChanged(
        ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);

        if (!_entries.TryGetValue(target, out var entry)) return;

        if (entry.Configuration.IsInputActive != _inputActive)
        {
            entry.Configuration.IsInputActive = _inputActive;
        }
    }

    private sealed record Entry(MicaController Controller, SystemBackdropConfiguration Configuration);
}
