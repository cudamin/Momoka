using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
    /// <summary>「已观看」那颗图标绿 —— 页面资源 <c>EgActionActiveBrush</c> 的初值。</summary>
    private Color _actionActiveInk;
    /// <summary>连播图标的两支墨：关着取压在图上那支浅墨，开着取固定的绿（见 <see cref="PaintContinueInk"/>）。</summary>
    private readonly SolidColorBrush _continueOffInk = new();
    private readonly SolidColorBrush _continueOnInk = new();
    private ControlBackgroundBrushes? _columnPlay;
    private ControlBackgroundBrushes? _compactPlay;
    /// <summary>顶部悬浮栏那颗播放键 —— 正文主播放键的第二个落点，材质与墨色跟同一份走。</summary>
    private ControlBackgroundBrushes? _stickyPlay;
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

        // 悬浮栏那颗播放键不在那两排里，但它是同一颗键的第二个落点（2026-11-16 用户令
        // 「悬浮上的播放按钮和实际播放按钮一样」）：同一支材质底、同一套三态墨色，一起进这份名单。
        _stickyPlay = ControlBackgroundBrushes.ForDetail(StickyPlayButton, lightSurface: true);
        _controlBackgrounds.Add(_stickyPlay);

        foreach (var picker in new[] { ColumnSourcePicker, ColumnAudioPicker, ColumnSubtitlePicker,
            SourcePicker, AudioPicker, SubtitlePicker })
        {
            // 展开面与行背景留给 Palette 的亚克力及 WinUI 原状态，闭合态才与按钮共用薄白。
            _controlBackgrounds.Add(ControlBackgroundBrushes.ForDetail(picker, lightSurface: true));
        }
        PaintContinueInk();
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
        PaintContinueInk();
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
    /// 「已观看」那颗图标的墨：正常对比下固定用页面资源里的绿（2026-11-15 用户令
    /// 「开启连播还有已观看时，按钮中心的图标要切换为绿色」，并明确「这个按钮的修改包括沉浸模式」；
    /// 连播那颗当天稍后改为跟主题色，见 <see cref="PaintContinueInk"/>）；
    /// 只有高对比度回到系统前景，那颗图标原来就取系统色。
    /// </summary>
    internal void PaintActionActiveInk() =>
        ((SolidColorBrush)Resources["EgActionActiveBrush"]).Color =
            _actionAccessibility.HighContrast ? ResourceColor("EgAccentBrush") : _actionActiveInk;

    /// <summary>
    /// 连播那颗图标的墨：关着取压在图上那支浅墨，开着取固定的绿（与「已观看」同一支，6 位十六进制
    /// #52B54B；2026-11-16 用户令「激活时图二要切换为绿色」）。高对比度两档都让给系统前景。
    /// <para>
    /// 这一颗在 XAML 里是 <see cref="TextBlock"/> 而不是 <see cref="FontIcon"/>：2026-11-16 实测
    /// FontIcon 放在 ToggleButton 的内容里时 Foreground 不参与绘制（把它临时改成红色、离屏渲染出来
    /// 仍是白箭头），所以换成同一字形的 TextBlock，前景才是真的画笔。
    /// </para>
    /// </summary>
    private void PaintContinueInk()
    {
        var forced = _actionAccessibility.HighContrast
            ? _actionUiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground)
            : (Color?)null;
        _continueOnInk.Color = forced ?? _actionActiveInk;
        _continueOffInk.Color = forced ?? ((SolidColorBrush)Application.Current.Resources["EgDetailActionTextBrush"]).Color;
        var ink = ViewModel.ContinuePlay ? _continueOnInk : _continueOffInk;
        ColumnContinueGlyph.Foreground = ink;
        CompactContinueGlyph.Foreground = ink;
    }

    /// <summary>
    /// 连播被点了一下：勾选变了，把那颗图标重画一次（只换色，不带旋转或描边动画）。
    /// <para>
    /// 勾选事件发生在 x:Bind 的 TwoWay 写回之前，这里直接读 <see cref="ViewModels.DetailViewModel.ContinuePlay"/>
    /// 会拿到上一拍的值 —— 用户报「点了没反应，箭头没变绿」就是这一步。所以先按被点这颗自己的
    /// <c>IsChecked</c> 上墨，再排一拍按视图模型校准（设置没写回时颜色跟着退回去）。
    /// </para>
    /// </summary>
    private void OnContinueToggled(object sender, RoutedEventArgs e)
    {
        var ink = sender is ToggleButton { IsChecked: true } ? _continueOnInk : _continueOffInk;
        ColumnContinueGlyph.Foreground = ink;
        CompactContinueGlyph.Foreground = ink;
        DispatcherQueue.TryEnqueue(PaintContinueInk);
    }

    private void OnBackdropImageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ViewModels.DetailViewModel.HeroImage)) return;
        PaintStoreBackdrop();
        PaintReadingColors();
        QueueReadingColors();
    }

    private void PaintBackdropReading()
    {
        // 2026-11-15 用户令「给沉浸模式下的更多信息卡片加背景卡片」：沉浸档这张卡重新铺一块卡片底
        // （2026-11-14 曾按「改为去掉其他信息的背景卡片」整块让开，字因此换成了压图浅墨）。
        // 卡片底是它自己那一支 —— 其余四块共用的 _panelFill 在沉浸档是 0，正是让它们让开用的。
        // 商店档仍是固定面色，纸面档仍是正文面板那一支亚克力。描边照全站卡片的规矩：不画
        // （EgCardBorderThickness 是 0），一块面色加 16 的圆角就已经从剧照上浮起来了。
        InfoPanel.Background = PlainMode ? StoreCardMaterialSource.Background
            : ViewModel.Showcase ? _infoCardFill : _panelFill;
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
                // 连播那两颗图标（TextBlock，带名字）由 PaintContinueInk 自己管：上面的分支只收
                // 没有名字的普通文字，这一支只收 FontIcon，两者都不会碰它们勾上后画的主题色。
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
        // 悬浮栏那颗是主播放键的第二个落点，三处一起换。
        var resumeInk = (Brush)Application.Current.Resources["EgAccentBrush"];
        foreach (var (glyph, button) in new[]
        {
            (ColumnResumeGlyph, ColumnPlayButton),
            (CompactResumeGlyph, CompactPlayButton),
            (StickyResumeGlyph, StickyPlayButton)
        })
            glyph.Foreground = onAccent is null || IsStoreCoverAction(button) ? resumeInk : button.Foreground;
        foreach (var text in new[] { TitleText, GenreLine, HeroFacts, Overview, ScoreText, CriticScoreText,
            DetailVideoText, DetailDirectorsText })
        {
            var defaultBrush = text == GenreLine ? (SolidColorBrush)ViewModel.SublineBrush
                : (SolidColorBrush)Application.Current.Resources[text == DetailDirectorsText ? "EgOnScrimDimBrush" : "EgOnScrimBrush"];
            var defaultColor = defaultBrush.Color;
            var color = forced is { } system ? ThemeColor.Rgb(system.R, system.G, system.B)
                : ThemeColor.Rgb(defaultColor.R, defaultColor.G, defaultColor.B);
            // 商店浅色的环境底随主题调亮，正文随之使用主题深墨；沉浸图上的浅墨保持原阅读保护。
            if (forced is null && ViewModel.StoreMode && !ThemeHost.Current.IsDark)
                color = text == DetailDirectorsText || text == GenreLine
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
        // 正文里换墨的只有四条带的卡和几块分区标题。以前这里递归整页视觉树：每建出一张卡都会排队来一遍，
        // 一次要走过页面上所有控件（实测一个 ViewChanged 里能占 20ms 以上）。带自己只过已实现的那几张，
        // 标题按正文面板的直接孩子取，成本回到与在屏卡片数成正比。
        var systemInk = forced is { } forcedColor ? ThemeColor.Rgb(forcedColor.R, forcedColor.G, forcedColor.B) : (ThemeColor?)null;
        foreach (var strip in BodyStrips) strip.ForEachRealizedCard(card => card.ApplyBackdropInk(systemInk));
        // 季页的单集是一行一集的竖列表（EpisodeList），行模板里也有一张 PosterCard；只过视口里建出来的那几行。
        PaintRepeaterCards(EpisodeList, systemInk);
        foreach (var panel in BodySheet.Children.OfType<Panel>())
            foreach (var child in panel.Children)
            {
                if (child is ShelfHead head) head.ApplyBackdropInk(systemInk);
                else if (child is Border { Child: ShelfHead nested }) nested.ApplyBackdropInk(systemInk);
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
        PaintContinueInk();
    }

    /// <summary>
    /// 主播放键那三份画刷（宽版式一颗、紧凑版式一颗、悬浮栏一颗）—— 商店模式只给它们换强调色面。
    /// 2026-11-16 起商店档的主播放键回到带字药丸，不再是无边框图标，所以宽版式那一颗也要换；
    /// 同一天悬浮栏那颗也并进来，三处同一副外观。
    /// </summary>
    private bool IsStorePlay(ControlBackgroundBrushes brushes) =>
        ReferenceEquals(brushes, _compactPlay) || ReferenceEquals(brushes, _columnPlay)
        || ReferenceEquals(brushes, _stickyPlay);

    /// <summary>
    /// 商店模式下那三颗图标键（已看、收藏、更多）；主播放键不在此列。
    /// 2026-11-16 起它们和下方音频/字幕选择框共用同一支固定面色与小圆角（ApplyAppearance 不再
    /// 跳过它们），这里只留给探针按「三颗图标」这一组单独核对，以及断点图标的墨色判断。
    /// </summary>
    internal bool IsStoreCoverAction(Control control) => ViewModel.StoreMode
        && (ReferenceEquals(control, ColumnWatchedButton) || ReferenceEquals(control, ColumnFavoriteButton)
            || ReferenceEquals(control, ColumnMoreButton));

    /// <summary>一条竖列表里已经建出来的行，行模板里的卡片换一次墨。见 <see cref="ShelfStrip.ForEachRealizedCard"/>。</summary>
    private static void PaintRepeaterCards(ItemsRepeater repeater, ThemeColor? ink)
    {
        var count = repeater.ItemsSourceView?.Count ?? 0;
        for (var index = 0; index < count; index++)
            if (repeater.TryGetElement(index) is { } container)
                foreach (var card in ShelfStrip.CardsIn(container)) card.ApplyBackdropInk(ink);
    }

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
