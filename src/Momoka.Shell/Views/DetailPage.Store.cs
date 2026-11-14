using Microsoft.UI.Xaml;
using Momoka.Theming;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private StoreBackdropFade? _storeBackdropFade;
    private StoreBackdropFade? _storeAmbientFade;
    private readonly StoreBackdropStyle.Tint _storeTint = new();

    private void PaintStoreBackdrop()
    {
        PlaceStoreComponents();
        Overview.MaxWidth = ViewModel.StoreMode ? Math.Min(520, ViewModel.StoreContentWidth) : (double)Application.Current.Resources["EgProseWidth"];
        StoreBackdrop.Visibility = _actionAccessibility.HighContrast
            ? Visibility.Collapsed : ViewModel.StoreBackdropVisibility;
        // 环境底跟剧照背景同进同退：缺图、高对比度、退出商店模式都不单独留一块色。
        StoreAmbient.Visibility = StoreBackdrop.Visibility;
        StoreBackdropStyle.Apply(StoreBackdropSides,
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
            // 简介、文件选项、操作按钮，全在封面下方同一条左沿上。三样都是既有实例，只换父级，绑定与事件不动。
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

    private void OnStoreHeroSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (ViewModel.StoreMode) ViewModel.StoreDetailsRoom = StoreHeroDetails.ActualHeight + 8;
    }
}
