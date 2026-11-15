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
                if (OverviewPanel.Parent == TailContent)
                {
                    TailContent.Children.Remove(OverviewPanel);
                    StoreHeroDetails.Children.Insert(0, OverviewPanel);
                }
                if (FileOptions.Parent == HeroStack)
                {
                    HeroStack.Children.Remove(FileOptions);
                    StoreHeroDetails.Children.Add(FileOptions);
                }
                if (ColumnActions.Parent == HeroStack)
                {
                    HeroStack.Children.Remove(ColumnActions);
                    StoreHeroDetails.Children.Add(ColumnActions);
                }
            }
            else
            {
                if (ColumnActions.Parent == StoreHeroDetails)
                {
                    StoreHeroDetails.Children.Remove(ColumnActions);
                    HeroStack.Children.Add(ColumnActions);
                }
                if (FileOptions.Parent == StoreHeroDetails)
                {
                    StoreHeroDetails.Children.Remove(FileOptions);
                    HeroStack.Children.Insert(HeroStack.Children.IndexOf(ColumnActions), FileOptions);
                }
                if (OverviewPanel.Parent == StoreHeroDetails)
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
            HeroStack.Spacing = store ? 6 : 10;
            TitlePlate.MaxHeight = store ? 40 : 56;
            TitlePlate.Margin = new Thickness(0, 0, 0, store ? 0 : 6);
            TitleButton.Margin = new Thickness(-8, 0, 0, store ? 4 : 12);
            StoreRatingRow.Orientation = store ? Microsoft.UI.Xaml.Controls.Orientation.Vertical : Microsoft.UI.Xaml.Controls.Orientation.Horizontal;
            StoreRatingRow.Spacing = store ? 4 : 14;
            // 2026-11-15 用户令「下方的卡片别压在封面上」：版本框与那排键离封面再让开一点。
            StoreHeroDetails.Margin = new Thickness(0, store ? 16 : 8, 0, 0);
            HeroFacts.TextWrapping = store ? TextWrapping.Wrap : TextWrapping.NoWrap;

            // 2026-11-15 用户令：播放／更多到封面右侧、收藏／已看到封面左上角、连播到集列表卡片右上角。
            // 宽版式里搬同一批实例，退出商店或收成紧凑版式时按原次序放回 ColumnActions。
            var storeWide = store && !ViewModel.IsCompact;
            PlaceStoreActionButtons(storeWide);

            // 右侧剧照／预告视口只在宽版式出现；圆角也只写给那一档（窄窗铺满整幅的那一版保持直角）。
            var previewCorner = store && ViewModel.PageWidth >= DetailHero.StoreWideFloor
                ? (CornerRadius)Resources["EgCardCornerRadius"] : default;
            StoreBackdropContent.CornerRadius = previewCorner;
            StoreTrailerContent.CornerRadius = previewCorner;

            ViewModel.StoreDetailsRoom = store ? StoreHeroDetails.ActualHeight + StoreHeroDetails.Margin.Top : 0;
            if (_actions is ShellPage shell) shell.RefreshDetailChrome();
        }
        finally { _placingStoreComponents = false; }
    }

    /// <summary>
    /// 商店宽版式里那几颗键换位置（2026-11-15 用户令）：
    /// 播放、更多搬进封面右侧的横排（更多在最右）；收藏、已看搬进封面左上角；
    /// 连播挂到集列表卡片标题那一行的右端（<see cref="ShelfHead.Trailing"/>）。
    /// <para>
    /// 搬的是同一批实例，绑定、命令、焦点与外观画刷都不重接；退出商店模式或页面收窄到紧凑版式时
    /// 按「播放、预告、花絮、连播、已看、收藏、更多」的原次序放回 <c>ColumnActionRow</c>。
    /// 已看那颗键在封面里收成图标（用户确认「收成图标」）：文字组的显隐由
    /// <see cref="ViewModels.DetailViewModel.ColumnWatchedLabelsVisibility"/> 收起，样式换成圆角方键。
    /// </para>
    /// </summary>
    private void PlaceStoreActionButtons(bool storeWide)
    {
        if (storeWide)
        {
            MoveToPanel(ColumnPlayButton, StoreCoverActions);
            MoveToPanel(ColumnMoreButton, StoreCoverActions);
            MoveToPanel(ColumnFavoriteButton, StoreCoverBadges);
            MoveToPanel(ColumnWatchedButton, StoreCoverBadges);

            if (!ReferenceEquals(EpisodeHead.Trailing, ColumnContinueButton))
            {
                Detach(ColumnContinueButton);
                EpisodeHead.Trailing = ColumnContinueButton;
            }
            if (!ReferenceEquals(ColumnWatchedButton.Style, Resources["EgRoundButtonStyle"]))
                ColumnWatchedButton.Style = (Style)Resources["EgRoundButtonStyle"];
            return;
        }

        if (ReferenceEquals(EpisodeHead.Trailing, ColumnContinueButton)) EpisodeHead.Trailing = null;
        RestoreToActions(ColumnPlayButton, 0);
        RestoreToActions(ColumnContinueButton, IndexAfter(ColumnExtrasButton));
        RestoreToActions(ColumnWatchedButton, IndexAfter(ColumnContinueButton));
        RestoreToActions(ColumnFavoriteButton, IndexAfter(ColumnWatchedButton));
        RestoreToActions(ColumnMoreButton, ColumnActionRow.Children.Count);
        if (!ReferenceEquals(ColumnWatchedButton.Style, Resources["EgPillButtonStyle"]))
            ColumnWatchedButton.Style = (Style)Resources["EgPillButtonStyle"];
    }

    /// <summary>把一颗键从原来的父级摘下来，追加到目标那一格（已经在目标里就不动）。</summary>
    private static void MoveToPanel(FrameworkElement element, Panel target)
    {
        if (ReferenceEquals(element.Parent, target)) return;
        Detach(element);
        target.Children.Add(element);
    }

    /// <summary>从当前父级摘下；只认 Panel，那一格里的键都是 Panel 的孩子。</summary>
    private static void Detach(FrameworkElement element)
    {
        if (element.Parent is Panel parent) parent.Children.Remove(element);
    }

    /// <summary>按给定的下标把键放回那排操作里；已经到了就不动。</summary>
    private void RestoreToActions(FrameworkElement element, int index)
    {
        if (ReferenceEquals(element.Parent, ColumnActionRow)) return;
        Detach(element);
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
