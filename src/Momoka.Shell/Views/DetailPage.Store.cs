using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Theming;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    /// <summary>商店模式版本框的左内边距 —— 标签压在这一段留白里，选中值从标签右侧起排。</summary>
    private const double StorePickerLabelLane = 76;

    /// <summary>版本框的高度与上限宽：同微软商店条目页那个「版本」框，55% 面色、约 520 宽。</summary>
    private const double StorePickerHeight = 40;
    private const double StorePickerMaxWidth = 520;

    private StoreBackdropFade? _storeBackdropFade;
    private readonly StoreBackdropStyle.Tint _storeTint = new();

    private void PaintStoreBackdrop()
    {
        PlaceStoreComponents();
        Overview.MaxWidth = ViewModel.StoreMode ? Math.Min(520, ViewModel.StoreContentWidth) : (double)Application.Current.Resources["EgProseWidth"];
        StoreBackdrop.Visibility = _actionAccessibility.HighContrast
            ? Visibility.Collapsed : ViewModel.StoreBackdropVisibility;
        StoreBackdropStyle.Apply(StoreBackdropSides, StoreBackdropBottom,
            _storeTint.Resolve(ViewModel.BackdropPixels, ViewModel.HeroImage is not null, ThemeHost.Current.Colors.Window));
        StoreBackdropAcrylic.Visibility = ViewModel.StoreMode && ViewModel.PageWidth is > 0 and < 1100
            && ViewModel.HeroImage is not null && !_actionAccessibility.HighContrast ? Visibility.Visible : Visibility.Collapsed;
        StoreBackdropStyle.ApplyAcrylic(StoreBackdropAcrylicFill, StoreBackdropAcrylicBottom,
            _storeTint.Resolve(ViewModel.BackdropPixels, ViewModel.HeroImage is not null, ThemeHost.Current.Colors.Window),
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

            // 2026-11-14 用户令「参考上图布局修改商店模式详情页」：微软商店条目页的顺序是封面和片名、
            // 简介、版本框、操作按钮，全在封面下方同一条左沿上。三样都是既有实例，只换父级，绑定与事件不动。
            // 退出商店模式时按原位置放回：说明回尾部（TailContent 的最后一块），文件选项和那排键回片名那一栏
            // （文件选项仍排在那排键前面）。
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
                    var at = StoreHeroDetails.Children.IndexOf(OverviewPanel);
                    StoreHeroDetails.Children.Insert(at < 0 ? 0 : at + 1, FileOptions);
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
            // 版本框的宽跟着内容宽走，所以放在 PageWidth 落地之后再量一次。
            PlaceStorePickerBoxes(store);
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
            StoreHeroDetails.Margin = new Thickness(0, 8, 0, 0);
            HeroFacts.TextWrapping = store ? TextWrapping.Wrap : TextWrapping.NoWrap;
            ViewModel.StoreDetailsRoom = store ? StoreHeroDetails.ActualHeight + 8 : 0;
            if (_actions is ShellPage shell) shell.RefreshDetailChrome();
        }
        finally { _placingStoreComponents = false; }
    }

    /// <summary>
    /// 商店模式的三个文件选择框（媒体源、音频、字幕）排成微软商店「版本」框那一副样子：一个固定的宽框、
    /// 标签写在框里左侧、选中值和下拉箭头在右。做法是把选择框横跨标签那一列（Grid.ColumnSpan），把标签
    /// 盖在它上面（Canvas.ZIndex）—— 框自己的底色、悬停、按下、聚焦和箭头仍由框架模板和
    /// <see cref="ControlBackgroundBrushes"/> 画，不另外描一层假框。
    /// <para>
    /// 标签压住的那一段靠选择框自己的 Padding 让开（<see cref="StorePickerLabelLane"/>）：模板里
    /// ContentPresenter 的 Margin 就是 TemplateBinding Padding，所以选中值从标签右边起排，而整条框
    /// （连同悬停底色）仍然是一个能点的面。宽度用 MinWidth 而不是 Width：EgPicker 展开时会临时钉住
    /// Width、收起时 ClearValue，写 Width 会被那一下抹掉。
    /// </para>
    /// </summary>
    private void PlaceStorePickerBoxes(bool store)
    {
        var width = Math.Max(0, Math.Min(StorePickerMaxWidth, ViewModel.StoreContentWidth));
        foreach (var (label, picker) in new[]
        {
            (SourceLabel, ColumnSourcePicker),
            (AudioLabel, ColumnAudioPicker),
            (SubtitleLabel, ColumnSubtitlePicker)
        })
        {
            if (store)
            {
                Grid.SetColumn(picker, 0);
                Grid.SetColumnSpan(picker, 2);
                picker.HorizontalAlignment = HorizontalAlignment.Left;
                picker.MinWidth = width;
                picker.Padding = new Thickness(StorePickerLabelLane, 0, 0, 0);
                picker.MinHeight = StorePickerHeight;
                Canvas.SetZIndex(label, 1);
                label.IsHitTestVisible = false;
                label.Margin = new Thickness(16, 0, 0, 0);
            }
            else
            {
                Grid.SetColumn(picker, 1);
                Grid.SetColumnSpan(picker, 1);
                picker.ClearValue(FrameworkElement.MinWidthProperty);
                picker.ClearValue(Control.PaddingProperty);
                picker.ClearValue(FrameworkElement.MinHeightProperty);
                Canvas.SetZIndex(label, 0);
                label.IsHitTestVisible = true;
                label.ClearValue(FrameworkElement.MarginProperty);
            }
        }
    }

    private void OnStoreHeroSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (ViewModel.StoreMode) ViewModel.StoreDetailsRoom = StoreHeroDetails.ActualHeight + 8;
    }
}
