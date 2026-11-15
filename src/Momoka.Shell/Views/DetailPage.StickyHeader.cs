using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using CommunityToolkit.WinUI.Animations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Shell.ViewModels;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private readonly UISettings _headerSystemSettings = new();
    private ImplicitAnimationSet? _headerShow;
    private ImplicitAnimationSet? _headerHide;
    private bool _headerActive;
    private bool _headerShown;
    private bool _headerQueued;
    private bool _headerCompact;
    private bool _headerMotionPrimed;
    private int _headerLifetime;

    public static Visibility HeaderPosterVisibility(ImageSource? image) =>
        image is null ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility HeaderSubtitleVisibility(string? text) =>
        string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;

    private void PrepareStickyHeader()
    {
        Loaded += (_, _) => WatchStickyHeader();
        Unloaded += (_, _) => StopStickyHeader();
    }

    private void WatchStickyHeader()
    {
        if (_headerActive) return;
        _headerActive = true;
        ViewModel.PropertyChanged += OnHeaderStateChanged;
        ViewModel.PlayCommand.CanExecuteChanged += OnHeaderPlayChanged;
        LayoutUpdated += OnHeaderLayoutUpdated;
        HomeMotion.PreferenceChanged += OnHeaderMotionChanged;
        _headerSystemSettings.AnimationsEnabledChanged += OnHeaderSystemMotionChanged;
        SyncHeaderMotion();
        QueueStickyHeader();
    }

    private void StopStickyHeader()
    {
        _headerActive = false;
        _headerLifetime++;
        _headerQueued = false;
        ViewModel.PropertyChanged -= OnHeaderStateChanged;
        ViewModel.PlayCommand.CanExecuteChanged -= OnHeaderPlayChanged;
        LayoutUpdated -= OnHeaderLayoutUpdated;
        HomeMotion.PreferenceChanged -= OnHeaderMotionChanged;
        _headerSystemSettings.AnimationsEnabledChanged -= OnHeaderSystemMotionChanged;
        ResetStickyHeader();
    }

    private void ResetStickyHeader()
    {
        _headerShown = false;
        ClearHeaderPlayMove();
        StickyHeader.IsHitTestVisible = false;
        ClearHeaderMotion();
        StickyHeader.Visibility = Visibility.Collapsed;
    }

    private void OnHeaderStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DetailViewModel.PlayVisibility) or nameof(DetailViewModel.IsCompact)
            or nameof(DetailViewModel.StillImage) or nameof(DetailViewModel.PageWidth))
            QueueStickyHeader();
    }

    private void OnHeaderPlayChanged(object? sender, EventArgs args) => QueueStickyHeader();
    private void OnHeaderLayoutUpdated(object? sender, object args) => QueueStickyHeader();

    private void QueueStickyHeader()
    {
        if (!_headerActive || _headerQueued) return;
        _headerQueued = true;
        var lifetime = _headerLifetime;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (!_headerActive || lifetime != _headerLifetime) return;
            _headerQueued = false;
            UpdateStickyHeader();
        })) _headerQueued = false;
    }

    private Button HeaderAnchor => ViewModel.IsCompact ? CompactPlayButton : ColumnPlayButton;

    private void UpdateStickyHeader()
    {
        if (!_headerActive) return;
        if (HeaderPlayLayoutChanged()) SettleHeaderPlayPosition();
        UpdateHeaderSize();
        var anchor = HeaderAnchor;
        if (XamlRoot is null || Body.VerticalOffset < 0 || Body.ActualHeight <= 0
            || !ReferenceEquals(anchor.XamlRoot, XamlRoot)
            || ViewModel.PlayVisibility != Visibility.Visible || !ViewModel.PlayCommand.CanExecute(null)
            || anchor.Visibility != Visibility.Visible || anchor.ActualWidth <= 0 || anchor.ActualHeight <= 0)
        {
            HideStickyHeader(anchor, immediate: true);
            return;
        }
        if (Body.VerticalOffset == 0)
        {
            HideStickyHeader(anchor, immediate: false);
            return;
        }

        Rect box;
        try
        {
            box = anchor.TransformToVisual(Body)
                .TransformBounds(new Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or COMException)
        {
            // ScrollView在导航和布局断树时可能仍派发最后一次ViewChanged。
            HideStickyHeader(anchor, immediate: true);
            return;
        }
        // 已核对Gallery v2.9.3 / 14a4a1a2b8ddc527dc4a7d5f7e743d7c2bc97db7
        // Samples/ItemsView/ItemsViewPage.xaml.cs的ScrollView.ViewChanged；没有此裁切浮层的成套样例。
        // 用户指定顶部裁掉三分之一就接手；底部尚未滚到不触发。收回多让4 DIP，避免临界抖动。
        var clipLine = box.Top + box.Height / 3;
        var threshold = _headerShown ? Math.Min(4, box.Height * 0.1) : 0;
        if (clipLine <= threshold) ShowStickyHeader();
        else HideStickyHeader(anchor, immediate: false);
    }

    private void UpdateHeaderSize()
    {
        var compact = Body.ActualWidth < 520;
        if (_headerCompact != compact)
        {
            _headerCompact = compact;
            StickyHeader.Margin = new Thickness(compact ? 12 : 32, 12, compact ? 12 : 32, 0);
            StickyHeader.Padding = new Thickness(compact ? 12 : 16, 12, compact ? 12 : 16, 12);
            StickyHeaderContent.ColumnSpacing = compact ? 8 : 16;
            StickyPlayText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }
        // 属性通知与x:Bind的回填顺序不固定；每次最终布局后重算，窄窗不会被晚到的封面挤掉片名。
        StickyPoster.Visibility = compact ? Visibility.Collapsed : HeaderPosterVisibility(ViewModel.StillImage);
    }

    private void ShowStickyHeader()
    {
        if (_headerShown) return;
        ClearHeaderPlayMove();
        var anchor = HeaderAnchor;
        var move = PrepareHeaderPlayMove(anchor);
        _headerShown = true;
        SyncHeaderMotion();
        if (move is not null)
        {
            // 关联按钮的落点不能跟随父卡片的Translation继续移动。
            ClearHeaderMotion();
        }
        else if (HeaderAnimationsEnabled)
        {
            var visual = ElementCompositionPreview.GetElementVisual(StickyHeader);
            // 仅首次入场播起点；已在隐式退场中的反转由Composition接续当前帧。
            // 隐式动画结束保留目标值，下一次显示无需重置到From。
            if (!_headerMotionPrimed)
            {
                _headerMotionPrimed = true;
                visual.Opacity = 0;
                visual.Properties.InsertVector3("Translation", new Vector3(0, -24, 0));
            }
        }
        StickyHeader.IsHitTestVisible = true;
        StickyHeader.Visibility = Visibility.Visible;
        if (move is not null) StickyHeader.UpdateLayout();
        BeginHeaderPlayMove(move, anchor, returning: false);
    }

    private void HideStickyHeader(Button anchor, bool immediate)
    {
        if (!_headerShown && !immediate) return;
        _headerShown = false;
        ClearHeaderPlayMove();
        var move = immediate ? null : PrepareHeaderPlayMove(StickyPlayButton);
        if (XamlRoot is { } root && ReferenceEquals(FocusManager.GetFocusedElement(root), StickyPlayButton)
            && ReferenceEquals(anchor.XamlRoot, root)
            && anchor.Visibility == Visibility.Visible && anchor.IsEnabled)
            anchor.Focus(FocusState.Keyboard);
        StickyHeader.IsHitTestVisible = false;
        if (move is not null)
        {
            ClearHeaderMotion();
            BeginHeaderPlayMove(move, anchor, returning: true);
            return;
        }
        if (immediate) ClearHeaderMotion();
        StickyHeader.Visibility = Visibility.Collapsed;
    }

    private bool HeaderAnimationsEnabled => HomeMotion.PreferenceEnabled && _headerSystemSettings.AnimationsEnabled;

    private void OnHeaderMotionChanged() => SyncHeaderMotion();

    private void OnHeaderSystemMotionChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
    {
        var lifetime = _headerLifetime;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_headerActive && lifetime == _headerLifetime) SyncHeaderMotion();
        });
    }

    private void SyncHeaderMotion()
    {
        if (!_headerActive || !HeaderAnimationsEnabled)
        {
            SettleHeaderPlayPosition();
            ClearHeaderMotion();
            return;
        }
        if (_headerPlayAnimation is not null) return;

        // WinUI Gallery v2.9.3 / 14a4a1a2b8ddc527dc4a7d5f7e743d7c2bc97db7
        // Controls/ControlExample.xaml及.xaml.cs的implicit显示/隐藏接线，与原桌面2.10.0一致。
        // MIT，Copyright (c) Microsoft Corporation，许可见docs/详情悬浮卡片.md。
        // 用Translation替代样例Offset以保留XAML布局位置；去掉From让快速反向从当前帧继续。
        // 2026-11-15用户要求开合更灵敏：显示120ms、隐藏80ms，位移与透明度一起落定，避免拖尾。
        _headerShow ??=
        [
            new TranslationAnimation { To = "0", Duration = TimeSpan.FromMilliseconds(120), EasingMode = EasingMode.EaseOut },
            new OpacityAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(120), EasingMode = EasingMode.EaseOut },
        ];
        _headerHide ??=
        [
            new TranslationAnimation { To = "0,-24,0", Duration = TimeSpan.FromMilliseconds(80), EasingMode = EasingMode.EaseOut },
            new OpacityAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(80), EasingMode = EasingMode.EaseOut },
        ];
        Implicit.SetShowAnimations(StickyHeader, _headerShow);
        Implicit.SetHideAnimations(StickyHeader, _headerHide);
        if (StickyHeader.Visibility == Visibility.Visible) _headerMotionPrimed = true;
    }

    private void ClearHeaderMotion()
    {
        _headerMotionPrimed = false;
        // 首次OnNavigatedTo早于implicit hook；Translation须先注册，否则StopAnimation会被平台拒绝。
        ElementCompositionPreview.SetIsTranslationEnabled(StickyHeader, true);
        Implicit.SetShowAnimations(StickyHeader, null!);
        Implicit.SetHideAnimations(StickyHeader, null!);
        var visual = ElementCompositionPreview.GetElementVisual(StickyHeader);
        // 未上树时平台可能尚未建立存值，先补齐属性再停止，首次导航同样可安全清理。
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Translation");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        visual.Opacity = 1;
    }
}
