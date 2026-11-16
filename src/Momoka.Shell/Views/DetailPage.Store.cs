using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Theming;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private StoreBackdropFade? _storeBackdropFade;
    private StoreBackdropFade? _storeAmbientFade;
    private readonly StoreBackdropStyle.Tint _storeTint = new();
    private readonly StoreAmbientBrush _storeAmbientBrush = new();

    private void PaintStoreBackdrop()
    {
        PlaceStoreComponents();
        Overview.MaxWidth = ViewModel.StoreMode ? Math.Min(520, ViewModel.StoreContentWidth) : (double)Application.Current.Resources["EgProseWidth"];
        StoreBackdrop.Visibility = _actionAccessibility.HighContrast
            ? Visibility.Collapsed : ViewModel.StoreBackdropVisibility;
        // 环境底跟剧照背景同进同退：缺图、高对比度、退出商店模式都不单独留一块色。
        StoreAmbient.Visibility = StoreBackdrop.Visibility;
        StoreBackdropSides.Background = _storeAmbientBrush.Resolve(ViewModel.BackdropPixels,
            StoreAmbient.Visibility == Visibility.Visible, ThemeHost.Current);
        StoreBackdropAcrylic.Visibility = ViewModel.StoreMode && ViewModel.PageWidth is > 0 and < DetailHero.StoreWideFloor
            && ViewModel.HeroImage is not null && !_actionAccessibility.HighContrast ? Visibility.Visible : Visibility.Collapsed;
        StoreBackdropStyle.ApplyAcrylic(StoreBackdropAcrylicFill, StoreBackdropAcrylicBottom,
            _storeTint.Resolve(ViewModel.BackdropPixels, ViewModel.HeroImage is not null, ThemeHost.Current.Colors.Window, ThemeHost.Current.IsDark),
            ThemeHost.ToColor(ThemeHost.Current.Colors.Window));
        UpdateStoreTrailer();
    }

    public static Visibility OverviewTipVisibility(bool store, bool trimmed) =>
        store && trimmed ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>四个44DIP命中格共176DIP，商店封面列至少保留180DIP；无图时按钮仍可操作。</summary>
    public static double StoreCoverColumnWidth(bool store) => store ? 180 : 0;

    // Gallery Samples/Grid/GridPage.xaml, v2.9.3 / 14a4a1a2: Auto行与原生Grid定位。
    // Gallery没有影视封面下操作样例；移动同一份控件，保留绑定、事件和焦点。
    private bool _placingStoreComponents;
    private void PlaceStoreComponents()
    {
        if (_placingStoreComponents) return;
        _placingStoreComponents = true;
        try
        {
            var store = ViewModel.StoreMode;

            // 2026-11-15：剧情放到封面下方，固定两行并通过原生提示阅读全文；同一实例保留绑定、焦点与事件。
            // 文件选项和操作继续排在剧情后面。退出简洁模式时，剧情回尾部，其余两块回片名栏。
            if (store)
            {
                if (TailContent.Children.Contains(OverviewPanel))
                {
                    TailContent.Children.Remove(OverviewPanel);
                    StoreHeroDetails.Children.Insert(0, OverviewPanel);
                }
                if (HeroStack.Children.Contains(FileOptions))
                {
                    HeroStack.Children.Remove(FileOptions);
                    StoreHeroDetails.Children.Add(FileOptions);
                }
                if (HeroStack.Children.Contains(ColumnActions))
                {
                    HeroStack.Children.Remove(ColumnActions);
                    StoreHeroDetails.Children.Add(ColumnActions);
                }
            }
            else
            {
                if (StoreHeroDetails.Children.Contains(ColumnActions))
                {
                    StoreHeroDetails.Children.Remove(ColumnActions);
                    HeroStack.Children.Add(ColumnActions);
                }
                if (StoreHeroDetails.Children.Contains(FileOptions))
                {
                    StoreHeroDetails.Children.Remove(FileOptions);
                    HeroStack.Children.Insert(HeroStack.Children.IndexOf(ColumnActions), FileOptions);
                }
                if (StoreHeroDetails.Children.Contains(OverviewPanel))
                {
                    StoreHeroDetails.Children.Remove(OverviewPanel);
                    OverviewPanel.ClearValue(FrameworkElement.HeightProperty);
                    OverviewBody.ClearValue(FrameworkElement.HeightProperty);
                    TailContent.Children.Add(OverviewPanel);
                }
            }

            StoreScrollContent.MaxWidth = store ? 1840 : double.PositiveInfinity;
            StoreScrollContent.HorizontalAlignment = store ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            if (Body.ActualWidth > 0)
            {
                StoreScrollContent.Width = store ? Math.Min(1840, Body.ActualWidth) : double.NaN;
                ViewModel.PageWidth = store ? Math.Min(1840, Body.ActualWidth) : Body.ActualWidth;
            }
            StoreHeroDetails.Visibility = store ? Visibility.Visible : Visibility.Collapsed;
            StoreHeroDetails.Spacing = store ? 14 : 8;
            HeroContent.Width = store ? ViewModel.StoreContentWidth : double.NaN;
            HeroContent.HorizontalAlignment = store ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            HeroStack.HorizontalAlignment = HorizontalAlignment.Stretch;
            HeroStack.Spacing = store ? StoreHeroStackSpacing : HeroStackSpacing;
            TitlePlate.MaxHeight = store ? 40 : 56;
            TitlePlate.Margin = new Thickness(0, 0, 0, store ? 0 : 6);
            TitleButton.Margin = new Thickness(-8, 0, 0, store ? 4 : 12);
            StoreRatingRow.Orientation = store ? Microsoft.UI.Xaml.Controls.Orientation.Vertical : Microsoft.UI.Xaml.Controls.Orientation.Horizontal;
            StoreRatingRow.Spacing = store ? 4 : 14;
            // 2026-11-15 用户令「下方的卡片别压在封面上」：版本框与那排键离封面再让开一点。
            StoreHeroDetails.Margin = new Thickness(0, store ? 16 : 8, 0, 0);
            HeroFacts.TextWrapping = store ? TextWrapping.Wrap : TextWrapping.NoWrap;

            // 2026-11-15 用户新要求：四颗操作键常驻封面下方，无边框。商店宽窄同一位置，退出商店才还原。
            PlaceStoreActionButtons(store);

            // 右侧剧照／预告视口只在宽版式出现；圆角也只写给那一档（窄窗铺满整幅的那一版保持直角）。
            // EgCardCornerRadius 在应用 Styles.xaml，直接查页面 Resources 不会向上搜索应用字典。
            var previewCorner = store && ViewModel.PageWidth >= DetailHero.StoreWideFloor
                ? (CornerRadius)Application.Current.Resources["EgCardCornerRadius"] : default;
            StoreBackdropContent.CornerRadius = previewCorner;
            StoreTrailerContent.CornerRadius = previewCorner;

            ViewModel.StoreDetailsRoom = store ? StoreHeroDetails.ActualHeight + StoreHeroDetails.Margin.Top : 0;
            if (_actions is ShellPage shell) shell.RefreshDetailChrome();
        }
        finally { _placingStoreComponents = false; }
    }

    /// <summary>
    /// 商店模式的操作键：主播放键贴在封面右下角外侧（2026-11-16 用户令「播放按钮放封面外面右边」），
    /// 已看、收藏、更多三颗仍常驻封面下方（2026-11-15 用户新要求，取代封面右侧与左上角旧位置）。
    /// 四个图标保留原控件、命令、自动化名称与状态；连播仍挂在集列表卡片右上角。
    /// <para>
    /// 搬的是同一批实例，绑定、命令、焦点不重接；退出商店模式时
    /// 按「播放、预告、花絮、连播、已看、收藏、更多」的原次序放回 <c>ColumnActionRow</c>。
    /// 已看那颗键收成图标：文字组的显隐由 <see cref="ViewModels.DetailViewModel.ColumnWatchedLabelsVisibility"/>
    /// 收起，四颗共用平台轻量按钮样式。
    /// </para>
    /// </summary>
    private void PlaceStoreActionButtons(bool store)
    {
        if (store)
        {
            MoveToPanel(ColumnPlayButton, StorePlaySlot);
            MoveToPanel(ColumnWatchedButton, StoreCoverActions);
            MoveToPanel(ColumnFavoriteButton, StoreCoverActions);
            MoveToPanel(ColumnMoreButton, StoreCoverActions);

            if (!ReferenceEquals(EpisodeHead.Trailing, ColumnContinueButton))
            {
                Detach(ColumnContinueButton);
                EpisodeHead.Trailing = ColumnContinueButton;
            }
            var subtle = (Style)Resources["EgStoreCoverActionButtonStyle"];
            foreach (var button in new[] { ColumnPlayButton, ColumnWatchedButton, ColumnFavoriteButton, ColumnMoreButton })
            {
                if (!ReferenceEquals(button.Style, subtle)) button.Style = subtle;
                // 构造时的材质Background是局部值，清掉才会真正使用SubtleButtonStyle的透明常态和平台三态。
                button.ClearValue(Control.BackgroundProperty);
                button.Opacity = 1;
            }
            return;
        }

        if (ReferenceEquals(EpisodeHead.Trailing, ColumnContinueButton)) EpisodeHead.Trailing = null;
        RestoreToActions(ColumnPlayButton, 0);
        RestoreToActions(ColumnContinueButton, IndexAfter(ColumnExtrasButton));
        RestoreToActions(ColumnWatchedButton, IndexAfter(ColumnContinueButton));
        RestoreToActions(ColumnFavoriteButton, IndexAfter(ColumnWatchedButton));
        RestoreToActions(ColumnMoreButton, ColumnActionRow.Children.Count);
        foreach (var (button, style) in new[]
        {
            (ColumnPlayButton, "EgPlayButtonStyle"), (ColumnWatchedButton, "EgPillButtonStyle"),
            (ColumnFavoriteButton, "EgRoundButtonStyle"), (ColumnMoreButton, "EgRoundButtonStyle")
        })
        {
            if (!ReferenceEquals(button.Style, Resources[style])) button.Style = (Style)Resources[style];
            // InitializeComponent 的首轮尺寸事件可能早于 PrepareAppearance；尚未种入的材质键不能索引。
            if (button.Resources.ContainsKey("ButtonBackground"))
                button.Background = (Microsoft.UI.Xaml.Media.Brush)button.Resources["ButtonBackground"];
        }
    }

    /// <summary>把一颗键从原来的父级摘下来，追加到目标那一格（已经在目标里就不动）。</summary>
    private void MoveToPanel(FrameworkElement element, Panel target)
    {
        if (target.Children.Contains(element)) return;
        Detach(element);
        if (target.Children.Contains(element)) return;
        target.Children.Add(element);
    }

    /// <summary>
    /// 从当前那一格摘下。判据用「集合里有没有」而不是 <c>Parent</c>：整页还在 <c>OnNavigatedTo</c> 里、
    /// 没有接上树时，键已经在 Children 里而 Parent 仍是 null，靠 Parent 认不出它，接着 Insert 就会抛
    /// E_ELEMENTALREADYATTACHED。挂件那一格是 ContentPresenter，先清掉 <see cref="ShelfHead.Trailing"/>。
    /// </summary>
    private void Detach(FrameworkElement element)
    {
        if (ReferenceEquals(EpisodeHead.Trailing, element)) EpisodeHead.Trailing = null;
        foreach (var panel in new Panel[] { ColumnActionRow, StoreCoverActions, StorePlaySlot })
            if (panel.Children.Contains(element)) panel.Children.Remove(element);
    }

    /// <summary>按给定的下标把键放回那排操作里；已经到了就不动。</summary>
    private void RestoreToActions(FrameworkElement element, int index)
    {
        if (ColumnActionRow.Children.Contains(element)) return;
        Detach(element);
        if (ColumnActionRow.Children.Contains(element)) return;
        ColumnActionRow.Children.Insert(Math.Clamp(index, 0, ColumnActionRow.Children.Count), element);
    }

    /// <summary>某一颗键右边那一格；那颗不在这一排时给排尾。</summary>
    private int IndexAfter(UIElement anchor)
    {
        var index = ColumnActionRow.Children.IndexOf(anchor);
        return index < 0 ? ColumnActionRow.Children.Count : index + 1;
    }

    private void OnStoreHeroSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (!ViewModel.StoreMode) return;

        ViewModel.StoreDetailsRoom = StoreHeroDetails.ActualHeight + StoreHeroDetails.Margin.Top;
    }
}
