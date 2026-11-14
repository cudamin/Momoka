using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Theming;
using Windows.Foundation;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    /// <summary>商店模式版本框的左内边距 —— 标签压在这一段留白里，选中值从标签右侧起排。</summary>
    private const double StorePickerLabelLane = 76;

    /// <summary>版本框的高度与上限宽：同微软商店条目页那个「版本」框，55% 面色、约 520 宽。</summary>
    private const double StorePickerHeight = 40;
    private const double StorePickerMaxWidth = 520;

    /// <summary>简介正文的行高 —— 和 DetailPage.xaml 里那个 TextBlock 的 <c>LineHeight</c> 是同一个数。</summary>
    private const double OverviewLineHeight = 24;

    /// <summary>
    /// 封面右栏里简介最少留出来的高：一行正文加导演那一行。片名折行把右栏撑得比封面还高时，
    /// 这个下限让简介和导演仍然读得成块，封面底那条线随之让位 —— 那种宽度下没有「恰好齐平」可谈。
    /// </summary>
    private const double StoreOverviewFloor = 52;

    private bool _syncingStoreOverview;

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
            // 同日第二令「把下面的剧情说明移动到封面右边，导演那一行的底部要刚好和封面底部持平」：简介不再
            // 排在封面底下，而是接在片名栏的最后、由 SyncStoreOverviewFill 撑到封面下沿；版本框和操作那排键
            // 仍留在封面下方的 StoreHeroDetails。
            // 退出商店模式时按原位置放回：说明回尾部（TailContent 的最后一块），文件选项和那排键回片名那一栏
            // （文件选项仍排在那排键前面）。
            if (store)
            {
                if (OverviewPanel.Parent == TailContent)
                {
                    TailContent.Children.Remove(OverviewPanel);
                    HeroStack.Children.Add(OverviewPanel);
                    // 只在刚搬进商店版式时回落到起始行数；重复上色/换主题时不要把它打回两行再量一次。
                    ViewModel.StoreOverviewLines = 2;
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
                if (OverviewPanel.Parent == HeroStack)
                {
                    HeroStack.Children.Remove(OverviewPanel);
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
        if (!ViewModel.StoreMode) return;

        ViewModel.StoreDetailsRoom = StoreHeroDetails.ActualHeight + 8;
        SyncStoreOverviewFill();
    }

    /// <summary>
    /// 简洁（商店）模式：把简介撑到左侧封面的下沿，导演那一行因此与封面底齐平（2026-11-14 用户令
    /// 「导演那一行的底部要刚好和封面底部持平，填满剧情说明，写不完的自动折叠」）。
    /// <para>
    /// 简介接在片名栏的最后，而这一栏顶上对齐、左边封面也顶上对齐，所以「简介顶」和「封面底」都与
    /// 简介自己的高无关 —— 高度可以一次算出来：封面底减简介顶。算完把同样的高给里面那层
    /// <c>OverviewBody</c>（星号行吃掉简介、底下导演那一行钉在底），再按剩下的高折行
    /// （<see cref="UpdateStoreOverviewLines"/>）。
    /// </para>
    /// <para>
    /// 片名折行把这一栏撑得比封面还高时，<see cref="StoreOverviewFloor"/> 保底，整栏跟着长、封面底那条
    /// 线落空 —— 那种宽度里没有「恰好齐平」可言，先保证简介和导演读得成块。写回之后 <c>HeroRoom</c>
    /// 读的是这一栏的实测高，带高跟着内容长，不会再循环着改高度。
    /// </para>
    /// </summary>
    private void SyncStoreOverviewFill()
    {
        if (_syncingStoreOverview || !ViewModel.StoreMode || OverviewPanel.Parent != HeroStack
            || OverviewPanel.Visibility != Visibility.Visible
            || PosterStill.Visibility != Visibility.Visible || PosterStill.XamlRoot is null
            || PosterStill.ActualWidth <= 0 || PosterStill.ActualHeight <= 0
            || OverviewPanel.ActualWidth <= 0 || HeroBand.ActualHeight <= 0)
            return;

        var cover = PosterStill.TransformToVisual(HeroBand)
            .TransformBounds(new Rect(0, 0, PosterStill.ActualWidth, PosterStill.ActualHeight));
        var panel = OverviewPanel.TransformToVisual(HeroBand)
            .TransformBounds(new Rect(0, 0, OverviewPanel.ActualWidth, OverviewPanel.ActualHeight));
        var padding = OverviewPanel.Padding;
        var height = Math.Max(StoreOverviewFloor + padding.Top + padding.Bottom, cover.Bottom - panel.Top);
        var body = Math.Max(0, height - padding.Top - padding.Bottom);

        _syncingStoreOverview = true;
        try
        {
            if (double.IsNaN(OverviewPanel.Height) || Math.Abs(OverviewPanel.Height - height) > 0.5)
                OverviewPanel.Height = height;
            if (double.IsNaN(OverviewBody.Height) || Math.Abs(OverviewBody.Height - body) > 0.5)
                OverviewBody.Height = body;
        }
        finally { _syncingStoreOverview = false; }

        UpdateStoreOverviewLines();
    }

    /// <summary>
    /// 简介能写几行 —— 撑满之后剩下的高只有量过才知道：减去下面导演那一行（含它头上的 6 和
    /// <c>OverviewFooter</c> 自己那 2），再按固定行高折。行数写回视图模型，超出的字由
    /// <c>TextTrimming=CharacterEllipsis</c> 收成省略号，就是用户要的「写不完的自动折叠」。
    /// </summary>
    private void UpdateStoreOverviewLines()
    {
        if (!ViewModel.StoreMode || OverviewPanel.Parent != HeroStack || OverviewBody.ActualHeight <= 0) return;

        var footer = OverviewFooter.ActualHeight + OverviewFooter.Margin.Top + OverviewFooter.Margin.Bottom;
        var room = Math.Max(0, OverviewBody.ActualHeight - footer);
        ViewModel.StoreOverviewLines = DetailHero.OverviewLines(room, OverviewLineHeight);
    }

    private void OnOverviewBodySizeChanged(object sender, SizeChangedEventArgs args) => UpdateStoreOverviewLines();

    private void OnOverviewFooterSizeChanged(object sender, SizeChangedEventArgs args) => UpdateStoreOverviewLines();
}
