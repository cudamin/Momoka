using Momoka.Shell.Interop;
using Momoka.Tests;

namespace Microsoft.UI.Windowing;

// Only the member names used by the extracted source are modeled; this is not a Windows API shim.
internal enum TitleBarHeightOption { Standard, Collapsed }

internal abstract class AppWindowPresenter;

// Records the calls the production window makes and refuses the combination the real API refuses:
// a presenter cannot keep the title bar once the border is gone.
internal sealed class OverlappedPresenter(NativeState state) : AppWindowPresenter
{
    internal bool HasBorder { get; private set; } = true;
    internal bool HasTitleBar { get; private set; } = true;

    internal void SetBorderAndTitleBar(bool border, bool titleBar)
    {
        if (!border && titleBar)
            throw new InvalidOperationException("无边框时不能保留标题栏");
        state.Call("SetBorderAndTitleBar", write: true);
        HasBorder = border;
        HasTitleBar = titleBar;
        state.PresenterWrites.Add((border, titleBar));
    }
}

internal sealed class AppWindow(AppWindowTitleBar? titleBar, AppWindowPresenter? presenter)
{
    internal AppWindowTitleBar? TitleBar { get; } = titleBar;
    internal AppWindowPresenter? Presenter { get; set; } = presenter;

    internal static AppWindow? GetFromWindowId(IntPtr fixtureId)
    {
        Assert.Equal(Native.State.Handle, fixtureId);
        Native.State.Call("GetFromWindowId", read: true);
        return Native.State.ConfiguredAppWindow;
    }
}

internal sealed class AppWindowTitleBar(NativeState state)
{
    private bool _extends = true;
    private TitleBarHeightOption _heightOption = TitleBarHeightOption.Standard;
    internal double StandardHeight { get; set; } = 32;
    internal double StandardInset { get; set; } = 138;
    // These are chosen endpoint values, not proof of native rendering or frame geometry.
    internal double Height => _heightOption == TitleBarHeightOption.Collapsed ? 0 : StandardHeight;
    internal double RightInset => _heightOption == TitleBarHeightOption.Collapsed ? 0 : StandardInset;

    internal bool ExtendsContentIntoTitleBar
    {
        get => _extends;
        set
        {
            state.Call("ExtendsContentIntoTitleBar", write: true);
            _extends = value;
        }
    }

    internal TitleBarHeightOption PreferredHeightOption
    {
        get => _heightOption;
        set
        {
            state.Call("PreferredHeightOption", write: true);
            _heightOption = value;
            state.HeightWrites.Add(value);
        }
    }
}
