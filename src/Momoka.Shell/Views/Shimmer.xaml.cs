using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace Momoka.Shell.Views;

/// <summary>
/// 「正在加载」那一块底板上扫过的一道光。见 <c>Shimmer.xaml</c> 顶上为什么不用 Storyboard。
/// <para>
/// 亮度只跟主题走一档：深色主题用低透明度的白，浅色主题换成更低透明度的黑 —— 白在黑底上是亮一道，
/// 在浅灰底上什么也看不见。高光宽度是面板宽度的整份，起点和终点各留一整个面板宽，扫过去时两头都是
/// 全透明的，看不出接缝。
/// </para>
/// <para>
/// 关掉动画的路径有三条：<see cref="IsActive"/> 变假、系统或应用关了动画、控件离树。三条都只做一件事
/// —— 把子视觉整个摘掉；占位底板还在，界面不会因为少了一道光而缺一块。
/// </para>
/// </summary>
public sealed partial class Shimmer : UserControl
{
    /// <summary>这条光扫完一趟要多久。慢于一次指针悬停的反馈，快于「图怎么还不来」的等待感。</summary>
    private const int SweepMs = 1300;

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(Shimmer), new PropertyMetadata(false, OnIsActiveChanged));

    private SpriteVisual? _visual;
    private CompositionLinearGradientBrush? _brush;
    private bool _watchingPreference;

    public Shimmer()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var shimmer = (Shimmer)d;
        if ((bool)e.NewValue) shimmer.Start();
        else shimmer.Stop();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_watchingPreference)
        {
            HomeMotion.PreferenceChanged += OnPreferenceChanged;
            _watchingPreference = true;
        }
        Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_watchingPreference)
        {
            HomeMotion.PreferenceChanged -= OnPreferenceChanged;
            _watchingPreference = false;
        }
        Stop();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Start();

    private void OnPreferenceChanged()
    {
        if (HomeMotion.AnimationsEnabled) Start();
        else Stop();
    }

    private void Start()
    {
        if (!IsLoaded || !IsActive || !HomeMotion.AnimationsEnabled)
        {
            Stop();
            return;
        }

        var width = (float)ActualWidth;
        var height = (float)ActualHeight;
        if (width <= 0 || height <= 0) return;

        var host = ElementCompositionPreview.GetElementVisual(this);
        var compositor = host.Compositor;

        if (_visual is null)
        {
            _brush = compositor.CreateLinearGradientBrush();
            _brush.StartPoint = new System.Numerics.Vector2(0f, 0.5f);
            _brush.EndPoint = new System.Numerics.Vector2(1f, 0.5f);
            _brush.ColorStops.Add(compositor.CreateColorGradientStop(0f, Microsoft.UI.Colors.Transparent));
            _brush.ColorStops.Add(compositor.CreateColorGradientStop(0.5f, Microsoft.UI.Colors.Transparent));
            _brush.ColorStops.Add(compositor.CreateColorGradientStop(1f, Microsoft.UI.Colors.Transparent));

            _visual = compositor.CreateSpriteVisual();
            _visual.Brush = _brush;
            ElementCompositionPreview.SetElementChildVisual(this, _visual);
        }

        var radius = (float)Math.Min(CornerRadius.TopLeft, Math.Min(width, height) / 2);
        var clip = compositor.CreateRectangleClip();

        // CreateRectangleClip 的四条边默认全是 0：只填圆角不填边，裁出来是一块空区域，整层光都看不见。
        clip.Left = 0;
        clip.Top = 0;
        clip.Right = width;
        clip.Bottom = height;
        clip.TopLeftRadius = clip.TopRightRadius = clip.BottomLeftRadius = clip.BottomRightRadius =
            new System.Numerics.Vector2(radius, radius);
        host.Clip = clip;

        _visual.Size = new System.Numerics.Vector2(width, height);

        // 高光横跨整块面板；中点停在中线上，两端各铺一层全透明。每次 Start 都重写颜色，
        // 因为主题可能在页面存活期间被换掉。
        var sheen = ActualTheme == ElementTheme.Light
            ? Color.FromArgb(0x16, 0, 0, 0)
            : Color.FromArgb(0x24, 255, 255, 255);
        if (_brush is { ColorStops.Count: 3 }) _brush.ColorStops[1].Color = sheen;

        using var easing = compositor.CreateLinearEasingFunction();
        using var sweep = compositor.CreateScalarKeyFrameAnimation();

        // 高光在渐变的正中，所以子视觉的位移只要从 -半宽 走到 +半宽，光带正好从左边扫出、右边扫进；
        // 走 -整宽到 +整宽 会让它有一半时间停在面板外面 —— 截图和肉眼都会当成「根本没动」。
        sweep.InsertKeyFrame(0f, -width / 2, easing);
        sweep.InsertKeyFrame(1f, width / 2, easing);
        sweep.Duration = TimeSpan.FromMilliseconds(SweepMs);
        sweep.IterationBehavior = AnimationIterationBehavior.Forever;

        // 先落初值再起动画：动画跑着的时候写同一个属性会把它停掉，下一趟就只剩一条静止的高光。
        _visual.Offset = new System.Numerics.Vector3(-width / 2, 0, 0);
        _visual.StartAnimation("Offset.X", sweep);
    }

    private void Stop()
    {
        if (_visual is null) return;

        _visual.StopAnimation("Offset.X");
        ElementCompositionPreview.SetElementChildVisual(this, null);
        _visual.Dispose();
        _brush?.Dispose();
        _visual = null;
        _brush = null;

        if (IsLoaded) ElementCompositionPreview.GetElementVisual(this).Clip = null;
    }
}
