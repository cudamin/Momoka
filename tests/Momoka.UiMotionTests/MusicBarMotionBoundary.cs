using Microsoft.UI.Xaml;

namespace Momoka.Shell.Views;

// 业务显隐、XAML 页面和尺寸是端点；栏显隐与内容避让方法由构建任务原样提取。
internal sealed class MusicLibraryPage
{
    internal double PlayerClearance { get; private set; }
    internal void SetPlayerClearance(double clearance) => PlayerClearance = clearance;
}

internal sealed partial class MusicBarMotionFixture
{
    private readonly MusicVisibility _music = new();
    private SurfaceMotion? _musicBarMotion;
    private bool _musicBarShown;
    private FrameworkElement MusicBar { get; } = new() { NaturalHeight = 70, Visibility = Visibility.Collapsed };
    private FrameworkElement ContentHost { get; } = new() { Margin = new Thickness(240, 0, 0, 0) };
    private ContentFrameBoundary ContentFrame { get; } = new();
    internal FrameworkElement Bar => MusicBar;
    internal Thickness PageMargin => ContentFrame.Margin;
    internal MusicLibraryPage? Page => ContentFrame.Content as MusicLibraryPage;
    internal void UseMusicPage()
    {
        ContentFrame.Content = new MusicLibraryPage();
        PlaceMusicBar();
    }
    internal void Show(bool visible)
    {
        _music.Visible = visible;
        PlaceMusicBar();
    }
    internal void SizeChanged(double height)
    {
        MusicBar.NaturalHeight = height;
        PlaceMusicBar();
    }
    internal void Stop() => _musicBarMotion?.Collapse();
    private sealed class MusicVisibility
    {
        internal bool Visible { get; set; }
    }
    private sealed class ContentFrameBoundary : FrameworkElement
    {
        internal object? Content { get; set; }
    }
}
