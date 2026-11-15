using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Theming;
using Windows.UI;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private readonly List<ControlBackgroundBrushes> _controlBackgrounds = [];
    private bool _appearanceObserved;
    private int _appearanceLifetime;
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _actionAccessibility = new();
    private readonly Windows.UI.ViewManagement.UISettings _actionUiSettings = new();
    private readonly Dictionary<TextBlock, SolidColorBrush> _readingColors = [];
    /// <summary>「其他信息」表里那两档墨（标签／值）；纸面档取主题的两支，沉浸档取压在图上那两支。</summary>
    private readonly SolidColorBrush _infoLabelInk = new();
    private readonly SolidColorBrush _infoValueInk = new();
    private readonly List<FontIcon> _plainActionIcons = [];
    private readonly Dictionary<FrameworkElement, Control> _explicitActionForegrounds = [];
    /// <summary>「连播开着／已观看」的图标绿 —— 页面资源 <c>EgActionActiveBrush</c> 的初值。</summary>
    private Color _actionActiveInk;
    private ControlBackgroundBrushes? _columnPlay;
    private ControlBackgroundBrushes? _compactPlay;
    private bool _readingQueued;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _appearanceSystemTimer;
    private bool _lastContrast;
    private bool _lastEffects;

    private void PrepareAppearance()
    {
        // 「连播 / 已观看」共用的那支墨从 XAML 取初值；之后由 ApplyAppearance 在正常对比与高对比度之间换。
        _actionActiveInk = ((SolidColorBrush)Resources["EgActionActiveBrush"]).Color;
        foreach (var strip in new[] { EpisodeStrip, SeasonRow, CastRow, SimilarRow })
            strip.ReadingChanged += QueueReadingColors;
        // 其他信息表里的行是回收着建的：新行建出来那一刻按当前那一套墨补一次（见 PaintInfoRowInk）。
        InfoTable.ElementPrepared += (_, args) => PaintInfoRowInk(args.Element);
        foreach (var group in new[] { ColumnActions, CompactBlock })
            foreach (var row in group.Children.OfType<WrapRow>())
                foreach (var control in row.Children.OfType<Microsoft.UI.Xaml.Controls.Control>())
                {
                    var brushes = ControlBackgroundBrushes.ForDetail(control, lightSurface: true);
                    _controlBackgrounds.Add(brushes);
                    if (ReferenceEquals(control, ColumnPlayButton)) _columnPlay = brushes;
                    else if (ReferenceEquals(control, CompactPlayButton)) _compactPlay = brushes;
                }

        foreach (var picker in new[] { ColumnSourcePicker, ColumnAudioPicker, ColumnSubtitlePicker,
            SourcePicker, AudioPicker, SubtitlePicker })
        {
            // 展开面与行背景留给 Palette 的亚克力及 WinUI 原状态，闭合态才与按钮共用薄白。
            _controlBackgrounds.Add(ControlBackgroundBrushes.ForDetail(picker, lightSurface: true));
        }
    }

    private void QueueReadingColors()
    {
        if (!_appearanceObserved || _readingQueued) return;
        _readingQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _readingQueued = false;
            PaintReadingColors();
        })) _readingQueued = false;
    }

    private void WatchAppearance()
    {
        ShellPrefs.AppearanceChanged -= OnAppearanceChanged;
        ShellPrefs.AppearanceChanged += OnAppearanceChanged;
        _appearanceObserved = true;
        _lastContrast = _actionAccessibility.HighContrast;
        _lastEffects = _actionUiSettings.AdvancedEffectsEnabled;
        _appearanceSystemTimer ??= DispatcherQueue.CreateTimer();
        _appearanceSystemTimer.Interval = TimeSpan.FromSeconds(1);
        _appearanceSystemTimer.Tick -= CheckAppearanceSystem;
        _appearanceSystemTimer.Tick += CheckAppearanceSystem;
        _appearanceSystemTimer.Start();
        ApplyAppearance();
    }

    private void StopAppearance()
    {
        _appearanceObserved = false;
        _appearanceSystemTimer?.Stop();
        _appearanceLifetime++;
        ShellPrefs.AppearanceChanged -= OnAppearanceChanged;
    }

    private void CheckAppearanceSystem(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        var contrast = _actionAccessibility.HighContrast;
        var effects = _actionUiSettings.AdvancedEffectsEnabled;
        if (!_appearanceObserved || contrast == _lastContrast && effects == _lastEffects) return;
        _lastContrast = contrast;
        _lastEffects = effects;
        ApplyAppearance();
    }

    private void OnAppearanceChanged(UiSettings ui)
    {
        if (!_appearanceObserved) return;
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyAppearance();
            return;
        }
        var lifetime = _appearanceLifetime;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_appearanceObserved && lifetime == _appearanceLifetime) ApplyAppearance();
        });
    }

    private void ApplyAppearance()
    {
        var contrast = _actionAccessibility.HighContrast;
        var tint = contrast ? _actionUiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)
            : ThemeHost.ToColor(ShellPrefs.AppearanceColor(false, ViewModel.AppearanceSettings));
        // 商店模式的按钮与选择框用固定的面色，与按钮材质那四项设置脱钩（2026-11-11 用户令）：
        // 商店背景使用固定阅读渐变，设置页里那一整组也随显示模式收起。高对比度仍走系统实色。
        // 主播放键例外（2026-11-14 用户令「播放按钮改成主题色」）：它吃强调色面，和参考图里的主操作一样
        // 是这一页唯一一块有颜色的键；其余按钮和闭合选择框仍是固定面色。
        var simple = !contrast && !ViewModel.AppearanceSettings.EnableImmersive;
        PaintActionActiveInk();
        foreach (var brushes in _controlBackgrounds)
        {
            if (simple)
            {
                if (IsStorePlay(brushes)) brushes.ApplyAccentSurface();
                else brushes.ApplyFixedSurface();
            }
            else
                brushes.Apply(contrast ? 100 : ViewModel.ControlBackgroundOpacity,
                    !contrast && ViewModel.UseAcrylicControls, tint,
                    ViewModel.ControlTintOpacity, ViewModel.ControlTintLuminosityOpacity);
        }
        ViewModel.RefreshAppearance();
        PaintScrim();
        // 模式与背景强度直接应用终值，窄窗仍使用自己的排版。
        SettleShowcase();
    }

    /// <summary>
    /// 连播「开着」和「已观看」那两颗图标的墨：正常对比下固定用页面资源里的绿（2026-11-15 用户令
    /// 「开启连播还有已观看时，按钮中心的图标要切换为绿色」，并明确「这个按钮的修改包括沉浸模式」）；
    /// 只有高对比度回到系统强调色，那颗图标原来就是 accent。
    /// </summary>
    internal void PaintActionActiveInk() =>
        ((SolidColorBrush)Resources["EgActionActiveBrush"]).Color =
            _actionAccessibility.HighContrast ? ResourceColor("EgAccentBrush") : _actionActiveInk;

    private void OnBackdropImageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ViewModels.DetailViewModel.HeroImage)) return;
        PaintStoreBackdrop();
        PaintReadingColors();
        QueueReadingColors();
    }

    private void PaintBackdropReading()
    {
        // 2026-11-14 用户令「改为去掉其他信息的背景卡片」：沉浸档这张卡和其余四块正文面板一样让开卡面，
        // 表里的字改吃压在图上那套浅墨（PaintInfoInk）—— 此前那块深色亚克力正是为了让暗字压在剧照上还能读。
        // 商店档仍用固定面色，纸面档仍用正文面板那支亚克力。
        InfoPanel.Background = PlainMode ? StoreCardMaterialSource.Background
            : ViewModel.Showcase ? null : _panelFill;
        PaintInfoInk();
        PaintReadingColors();
    }

    /// <summary>
    /// 沉浸档没有卡面垫着的「其他信息」：表里的标签和值、上面那行「媒体信息」小标题、底下那条其他信息
    /// 都换成压在图上那套浅墨；纸面档回到主题自己的两支暗墨。
    /// <para>
    /// 表里的行是回收着建的，所以两处都要：已经建出来的过一遍（<see cref="ItemsRepeater.TryGetElement"/>），
    /// 之后建出来的由 <see cref="ItemsRepeater.ElementPrepared"/> 接住（订阅在 <see cref="PrepareAppearance"/>）。
    /// </para>
    /// </summary>
    private void PaintInfoInk()
    {
        var forced = _actionAccessibility.HighContrast
            ? _actionUiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground)
            : (Color?)null;
        var scrim = ViewModel.Showcase;
        _infoLabelInk.Color = forced ?? ResourceColor(scrim ? "EgOnScrimDimBrush" : "EgTextFaintBrush");
        _infoValueInk.Color = forced ?? ResourceColor(scrim ? "EgOnScrimBrush" : "EgTextDimBrush");

        if (scrim || forced is not null) MediaInfoTitle.Foreground = _infoValueInk;
        else MediaInfoTitle.ClearValue(TextBlock.ForegroundProperty);

        for (var index = 0; index < ViewModel.InfoRows.Count; index++)
            if (InfoTable.TryGetElement(index) is { } element) PaintInfoRowInk(element);

        // 那一行的字都在内联的 Run 上，画刷由它在搭的时候带走，所以整行重搭一遍。
        PaintOtherInfo();
    }

    private void PaintInfoRowInk(DependencyObject row)
    {
        foreach (var text in AppearanceChildren(row).OfType<TextBlock>())
        {
            if (Equals(text.Tag, "InfoLabel")) text.Foreground = _infoLabelInk;
            else if (Equals(text.Tag, "InfoValue")) text.Foreground = _infoValueInk;
        }
    }

    private static Color ResourceColor(string key) => Application.Current.Resources[key] is SolidColorBrush brush
        ? brush.Color
        : Microsoft.UI.Colors.White;

    private ThemeColor ReadingBackground(FrameworkElement node)
    {
        var fallback = ThemeHost.Current.Colors.Window;
        if (!ViewModel.BackdropShown) return fallback;
        var sample = BackdropInk.Sample(node, BackdropPicture, ViewModel.BackdropPixels, true, fallback);
        var overlay = ShellPrefs.AppearanceColor(true, ViewModel.AppearanceSettings);
        // 画了亚克力那一层的地方，字压在材料上：挡住多少就用 Coverage 估多少；没画那一层（窄窗、集页卡面）
        // 压的还是那道实色罩子。系统关掉透明效果时材质回退成不透明实色，这里也按满不透明算。
        var alpha = UsesBackdropFade
            ? AppearanceOpacity.Coverage(ViewModel.BackdropTintOpacity, ViewModel.BackdropTintLuminosityOpacity)
            : AppearanceOpacity.ReadingVeil;
        if (UsesBackdropFade && !_actionUiSettings.AdvancedEffectsEnabled) alpha = 1;
        return sample.Mix(overlay, alpha);
    }

    private void PaintReadingColors()
    {
        if (!_appearanceObserved) return;
        var contrast = _actionAccessibility.HighContrast;
        var forced = contrast ? _actionUiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground) : (Color?)null;
        // 商店模式主播放键的强调面上，字和图标取实心强调键的对比色；高对比度仍让给系统前景。
        var onAccent = !contrast && !ViewModel.AppearanceSettings.EnableImmersive
            ? ((SolidColorBrush)Application.Current.Resources["EgTextOnAccentBrush"]).Color : (Color?)null;
        var storeInk = ViewModel.StoreMode && !ThemeHost.Current.IsDark
            ? ThemeHost.ToColor(ThemeHost.Current.Colors.Text) : (Color?)null;
        foreach (var backgrounds in _controlBackgrounds)
            backgrounds.ApplyDefaultForeground(
                forced ?? (onAccent is { } accent && IsStorePlay(backgrounds) ? accent : storeInk));
        // 模板中显式指定过颜色的非强调图标/连播文字，跟同一颗按钮走。
        foreach (var backgrounds in _controlBackgrounds)
        {
            if (backgrounds.Control is ComboBox) continue;
            foreach (var child in AppearanceChildren(backgrounds.Control))
                if (child is TextBlock text && text.Name.Length == 0)
                {
                    if (!_explicitActionForegrounds.ContainsKey(text))
                    {
                        if (backgrounds.Control is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton)
                            text.Foreground = backgrounds.Control.Foreground;
                        else text.ClearValue(TextBlock.ForegroundProperty);
                        _explicitActionForegrounds.Add(text, backgrounds.Control);
                    }
                }
                else if (child is FontIcon icon)
                {
                    if (!_plainActionIcons.Contains(icon) && icon.Foreground is SolidColorBrush ink
                        && ink.Color == ((SolidColorBrush)Application.Current.Resources["EgDetailActionTextBrush"]).Color)
                        _plainActionIcons.Add(icon);
                    if (_plainActionIcons.Contains(icon) && !_explicitActionForegrounds.ContainsKey(icon))
                    {
                        icon.ClearValue(FontIcon.ForegroundProperty);
                        _explicitActionForegrounds.Add(icon, backgrounds.Control);
                    }
                }
        }
        // 商店模式的播放键是强调色面：断点那颗图标原先是强调色，压在强调面上等于消失，改成和字同一支
        // 对比色；其余模式还原成主题色图标（那一档按钮还是半透明底，强调色本来只落在图标上）。
        var resumeInk = (Brush)Application.Current.Resources["EgAccentBrush"];
        foreach (var (glyph, button) in new[] { (ColumnResumeGlyph, ColumnPlayButton), (CompactResumeGlyph, CompactPlayButton) })
            glyph.Foreground = onAccent is null ? resumeInk : button.Foreground;
        foreach (var text in new[] { TitleText, GenreLine, HeroFacts, Overview, ScoreText, ScoreSourceText,
            DetailVideoText, DetailDirectorsText })
        {
            var defaultBrush = text == GenreLine ? (SolidColorBrush)ViewModel.SublineBrush
                : (SolidColorBrush)Application.Current.Resources[text == DetailDirectorsText ? "EgOnScrimDimBrush" : "EgOnScrimBrush"];
            var defaultColor = defaultBrush.Color;
            var color = forced is { } system ? ThemeColor.Rgb(system.R, system.G, system.B)
                : ThemeColor.Rgb(defaultColor.R, defaultColor.G, defaultColor.B);
            // 商店浅色的环境底随主题调亮，正文随之使用主题深墨；沉浸图上的浅墨保持原阅读保护。
            if (forced is null && ViewModel.StoreMode && !ThemeHost.Current.IsDark)
                color = text == DetailDirectorsText || text == GenreLine || text == ScoreSourceText
                    ? ThemeHost.Current.Colors.Text.Mix(ThemeHost.Current.Colors.Window, 0.14)
                    : ThemeHost.Current.Colors.Text;
            if (!_readingColors.TryGetValue(text, out var brush))
            {
                brush = new SolidColorBrush();
                _readingColors.Add(text, brush);
                text.Foreground = brush;
            }
            brush.Color = ThemeHost.ToColor(color);
        }
        foreach (var child in AppearanceChildren(BodySheet))
        {
            var systemInk = forced is { } system ? ThemeColor.Rgb(system.R, system.G, system.B) : (ThemeColor?)null;
            if (child is ShelfHead head) head.ApplyBackdropInk(systemInk);
            else if (child is PosterCard card)
                card.ApplyBackdropInk(systemInk is { } ink ? _ => ink : null);
        }
        foreach (var group in new[] { ColumnPickers, PickerPanel })
            foreach (var text in AppearanceChildren(group).OfType<TextBlock>())
            {
                if (IsPickerContent(text)) continue;
                if (!_readingColors.TryGetValue(text, out var brush))
                {
                    brush = new SolidColorBrush();
                    _readingColors.Add(text, brush);
                    text.Foreground = brush;
                }
                brush.Color = forced ?? ResourceColor(ViewModel.StoreMode && !ThemeHost.Current.IsDark
                    ? "EgTextBrush" : "EgOnScrimBrush");
            }
    }

    /// <summary>主播放键那两份画刷（宽版式一颗、紧凑版式一颗）—— 商店模式只给它们换强调色面。</summary>
    private bool IsStorePlay(ControlBackgroundBrushes brushes) =>
        ReferenceEquals(brushes, _columnPlay) || ReferenceEquals(brushes, _compactPlay);

    private static bool IsPickerContent(DependencyObject node)
    {
        for (var parent = VisualTreeHelper.GetParent(node); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ComboBox picker)
                return picker.Header is not FrameworkElement header || !ReferenceEquals(header, node);
        return false;
    }

    private static IEnumerable<DependencyObject> AppearanceChildren(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var nested in AppearanceChildren(child)) yield return nested;
        }
    }
}
