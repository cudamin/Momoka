using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Diagnostics;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

/// <summary>菜单材质未接入时保留可读的 XAML 实色面；共享材质任一目标失败时整组保守回退。</summary>
public static class DetailMenuSurface
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(DetailMenuSurface), new PropertyMetadata(false, OnEnabledChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(DetailMenuSurface), new PropertyMetadata(null));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not MenuFlyoutPresenter presenter) return;
        if (presenter.GetValue(StateProperty) is State previous) previous.Dispose();
        presenter.ClearValue(StateProperty);
        if (args.NewValue is true)
        {
            var state = new State(presenter);
            presenter.SetValue(StateProperty, state);
        }
    }

    private sealed class State : IDisposable
    {
        private readonly MenuFlyoutPresenter _presenter;
        private readonly SolidColorBrush _solid = new();
        private readonly SolidColorBrush _clear = new(Color.FromArgb(0, 0, 0, 0));
        private readonly AccessibilitySettings _accessibility = new();
        private readonly UISettings _settings = new();
        private DetailMenuBackdrop? _backdrop;
        private long _token;
        private bool _watching;
        private bool _registered;

        internal State(MenuFlyoutPresenter presenter)
        {
            _presenter = presenter;
            presenter.Loaded += OnLoaded;
            presenter.Unloaded += OnUnloaded;
            if (presenter.IsLoaded) Start();
        }

        private void OnLoaded(object sender, RoutedEventArgs args) => Start();
        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            if (!_presenter.IsLoaded) Stop();
        }

        private void Start()
        {
            if (_watching) return;
            try
            {
                _token = _presenter.RegisterPropertyChangedCallback(MenuFlyoutPresenter.SystemBackdropProperty, OnBackdropChanged);
                _registered = true;
                _watching = true;
                Connect();
            }
            catch (Exception error)
            {
                Stop();
                Paint();
                Log.Warn("详情菜单材质", "订阅菜单表面失败，重新加载时重试", error);
            }
        }

        private void OnBackdropChanged(DependencyObject sender, DependencyProperty property)
        {
            if (_watching) Connect();
        }

        private void Connect()
        {
            if (_backdrop is not null) _backdrop.SurfaceChanged -= Paint;
            _backdrop = _presenter.SystemBackdrop as DetailMenuBackdrop;
            if (_backdrop is not null) _backdrop.SurfaceChanged += Paint;
            Paint();
        }

        private void Paint()
        {
            try
            {
                // Establish an opaque fallback before querying system colors. A failed first
                // read must not leave an unattached material covered by a transparent style.
                _solid.Color = ThemeHost.ToColor(ThemeHost.Current.Colors.SurfaceAlt.WithAlpha(0xFF));
                _presenter.Background = _solid;
                if (_accessibility.HighContrast) _solid.Color = _settings.GetColorValue(UIColorType.Background);
                _presenter.Background = _backdrop is { NeedsSolidSurface: false } ? _clear : _solid;
            }
            catch (Exception error) { Log.Warn("详情菜单材质", "更新菜单 XAML 表面失败", error); }
        }

        private void Stop()
        {
            if (!_watching) return;
            _watching = false;
            try
            {
                if (_registered) _presenter.UnregisterPropertyChangedCallback(MenuFlyoutPresenter.SystemBackdropProperty, _token);
            }
            catch (Exception error) { Log.Warn("详情菜单材质", "退订菜单属性失败", error); }
            finally
            {
                _registered = false;
                if (_backdrop is not null) _backdrop.SurfaceChanged -= Paint;
                _backdrop = null;
            }
        }

        public void Dispose()
        {
            Stop();
            _presenter.Loaded -= OnLoaded;
            _presenter.Unloaded -= OnUnloaded;
            if (ReferenceEquals(_presenter.Background, _solid) || ReferenceEquals(_presenter.Background, _clear))
                _presenter.ClearValue(Control.BackgroundProperty);
        }
    }
}
