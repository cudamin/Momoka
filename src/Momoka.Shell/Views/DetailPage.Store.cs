using Microsoft.UI.Xaml;
using Momoka.Theming;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
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
            StoreScrollContent.MaxWidth = store ? 1840 : double.PositiveInfinity;
            StoreScrollContent.HorizontalAlignment = store ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            if (Body.ActualWidth > 0)
            {
                StoreScrollContent.Width = store ? Math.Min(1840, Body.ActualWidth) : double.NaN;
                ViewModel.PageWidth = store ? Math.Min(1840, Body.ActualWidth) : Body.ActualWidth;
            }
            if (ColumnActions.Parent == StoreHeroDetails)
            {
                StoreHeroDetails.Children.Remove(ColumnActions);
                HeroStack.Children.Add(ColumnActions);
            }
            if (store && FileOptions.Parent == HeroStack)
            {
                HeroStack.Children.Remove(FileOptions);
                StoreHeroDetails.Children.Add(FileOptions);
            }
            else if (!store && FileOptions.Parent == StoreHeroDetails)
            {
                StoreHeroDetails.Children.Remove(FileOptions);
                HeroStack.Children.Insert(HeroStack.Children.IndexOf(ColumnActions), FileOptions);
            }
            StoreHeroDetails.Visibility = store ? Visibility.Visible : Visibility.Collapsed;
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
