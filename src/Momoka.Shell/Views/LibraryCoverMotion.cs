using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

/// <summary>封面循环只在可见视口运行；复用 AnimatedPoster 的系统偏好与宿主可见性边界。</summary>
internal sealed class LibraryCoverMotion
{
    private readonly FrameworkElement _cover;
    private readonly Storyboard _board;
    private readonly UISettings _settings = new();
    private XamlRoot? _root;
    private bool _loaded;
    private bool _inViewport;
    private bool _running;

    internal LibraryCoverMotion(FrameworkElement cover, Storyboard board)
    {
        _cover = cover;
        _board = board;
        cover.Loaded += OnLoaded;
        cover.Unloaded += OnUnloaded;
        cover.EffectiveViewportChanged += OnViewportChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        _root = _cover.XamlRoot;
        if (_root is not null) _root.Changed += OnRootChanged;
        HomeMotion.PreferenceChanged += Refresh;
        _settings.AnimationsEnabledChanged += OnSystemPreferenceChanged;
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _loaded = false;
        _inViewport = false;
        if (_root is not null) _root.Changed -= OnRootChanged;
        _root = null;
        HomeMotion.PreferenceChanged -= Refresh;
        _settings.AnimationsEnabledChanged -= OnSystemPreferenceChanged;
        Refresh();
    }

    private void OnViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        var viewport = args.EffectiveViewport;
        _inViewport = viewport.Width > 0 && viewport.Height > 0
            && viewport.X < _cover.ActualWidth && viewport.Y < _cover.ActualHeight
            && viewport.X + viewport.Width > 0 && viewport.Y + viewport.Height > 0;
        Refresh();
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Refresh();

    private void OnSystemPreferenceChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
        => _cover.DispatcherQueue.TryEnqueue(Refresh);

    private void Refresh()
    {
        bool run = _loaded && _inViewport && _root is { IsHostVisible: true } && HomeMotion.AnimationsEnabled;
        if (run == _running) return;
        _running = run;
        if (run) _board.Begin();
        else _board.Stop();
    }
}
