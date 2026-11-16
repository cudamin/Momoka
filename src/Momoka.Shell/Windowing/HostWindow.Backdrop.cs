using Momoka.Diagnostics;

namespace Momoka.Shell.Windowing;

internal sealed partial class HostWindow
{
    private bool _useBackdrop = true;
    private bool _backdropInputActive = true;
    private IslandMicaBackdrop? _backdrop;

    /// <summary>浏览材质的请求；连接失败和系统实色回退都不改变这个意图。</summary>
    public bool UseBackdrop
    {
        get => _useBackdrop;
        set
        {
            if (_useBackdrop == value && (value ? BackdropConnected : _source?.SystemBackdrop is null)) return;
            _useBackdrop = value;
            ApplyBackdrop();
        }
    }

    /// <summary>
    /// 播放页请求撤掉浏览材质，退出后重新接入。此值记录调用方的请求，不证明后端正在播放。
    /// 集成视频在页面的 Composition 视觉树里，独占视频在 mpv 自建顶层窗口里；这里不操作视频目标。
    /// </summary>
    public bool VideoVisible
    {
        get => !_useBackdrop;
        set => UseBackdrop = !value;
    }

    internal bool BackdropConnected => _source is not null && _backdrop is { ConnectedTargetCount: > 0 }
        && ReferenceEquals(_source.SystemBackdrop, _backdrop);

    internal string BackdropState => $"requested={_useBackdrop}/connected={BackdropConnected}/"
        + (_backdrop?.MaterialState ?? "no-controller");

    private void ApplyBackdrop()
    {
        if (_source is null) return;
        try
        {
            // 播放页撤材质与设置页的「透明效果」关掉走同一条：不挂任何系统底材，画布自己落成实色。
            if (!_useBackdrop || !ThemeHost.TransparencyEnabled)
            {
                _source.SystemBackdrop = null;
                return;
            }

            _backdrop ??= new IslandMicaBackdrop();
            _backdrop.SetInputActive(_backdropInputActive);
            // 相同引用赋值不会重新调用连接回调。失败后重试必须先让框架真的断开旧 target。
            if (ReferenceEquals(_source.SystemBackdrop, _backdrop) && !BackdropConnected)
                _source.SystemBackdrop = null;
            _source.SystemBackdrop = _backdrop;
        }
        catch (Exception error)
        {
            Log.Warn(Category, "设置 Mica 背景失败，岛面退回类背景刷＋画布", error);
            try { _source.SystemBackdrop = null; }
            catch (Exception cleanup) { Log.Warn(Category, "摘除失败的 Mica 背景也失败", cleanup); }
        }
    }

    private void OnBackdropFocusChanged(bool active)
    {
        // 材质尚未创建或播放态已摘除时也保留 HWND 的最近激活状态。
        _backdropInputActive = active;
        _backdrop?.SetInputActive(active);
    }

    private void ReleaseBackdrop()
    {
        FocusChanged -= OnBackdropFocusChanged;
        try { if (_source is not null) _source.SystemBackdrop = null; }
        catch (Exception error) { Log.Warn(Category, "关闭窗口时摘除 Mica 失败", error); }
        _backdrop = null;
    }
}
