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
        Overview.MaxWidth = ViewModel.StoreMode ? Math.Min(520, ViewModel.StoreHeroWidth) : (double)Application.Current.Resources["EgProseWidth"];
        StoreBackdrop.Visibility = _actionAccessibility.HighContrast
            ? Visibility.Collapsed : ViewModel.StoreBackdropVisibility;
        // 环境底跟剧照背景同进同退：缺图、高对比度、退出商店模式都不单独留一块色。
        StoreAmbient.Visibility = StoreBackdrop.Visibility;
        // 左上角那道淡墨也一样：压在装饰背景上，背景不在时它没有东西可压。
        StoreShade.Visibility = StoreBackdrop.Visibility;
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

    /// <summary>四个44DIP命中格共176DIP，商店封面列至少保留180DIP；无图时按钮仍可操作。片名栏的保底宽度读同一个数。</summary>
    public static double StoreCoverColumnWidth(bool store) => store ? DetailHero.StoreCoverColumn : 0;

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
            var columnWidth = store ? Math.Min(1840, Body.ActualWidth) : Body.ActualWidth;
            if (Body.ActualWidth > 0)
            {
                StoreScrollContent.Width = store ? columnWidth : double.NaN;
                ViewModel.PageWidth = columnWidth;
            }
            // 2026-11-16 用户令「渐变要填满左右两边」：商店档的装饰带（环境底与淡墨）跟着整扇窗口走，
            // 不再被 1840 的内容列截在中间；左右多出来的那两截用负边距让出去，透过来的就是同一块
            // 环境色与同一道渐变。内容是居中的，两边让出的等宽，窗口不超过 1840 时这一项为 0。
            var bleed = store && Body.ActualWidth > columnWidth ? (Body.ActualWidth - columnWidth) / 2 : 0;
            var bleedMargin = new Thickness(-bleed, 0, -bleed, 0);
            StoreAmbient.Margin = bleedMargin;
            StoreShade.Margin = bleedMargin;
            StoreHeroDetails.Visibility = store ? Visibility.Visible : Visibility.Collapsed;
            StoreHeroDetails.Spacing = store ? 14 : 8;
            // 2026-11-16 用户令「位置不够时不要裁切，直接压在背景图上」：片名栏撑到保底宽度时，
            // 这一行跟着往右长，多出来的那一截压在右侧剧照上，不再把片名、徽标和播放键挤到被切。
            HeroContent.Width = store ? ViewModel.StoreHeroWidth : double.NaN;
            HeroContent.HorizontalAlignment = store ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            HeroStack.HorizontalAlignment = HorizontalAlignment.Stretch;
            HeroStack.Spacing = store ? StoreHeroStackSpacing : HeroStackSpacing;
            // 2026-11-16 用户令「徽标不要自动缩小」：商店档不再单独压到 40，与其余页面同一个 280×56 上限。
            TitlePlate.Margin = new Thickness(0, 0, 0, store ? 0 : 6);
            TitleButton.Margin = new Thickness(-8, 0, 0, store ? 4 : 12);
            StoreRatingRow.Orientation = store ? Microsoft.UI.Xaml.Controls.Orientation.Vertical : Microsoft.UI.Xaml.Controls.Orientation.Horizontal;
            StoreRatingRow.Spacing = store ? 4 : 14;
            // 2026-11-15 用户令「下方的卡片别压在封面上」：版本框与那排键离封面再让开一点。
            StoreHeroDetails.Margin = new Thickness(0, store ? 16 : 8, 0, 0);
            HeroFacts.TextWrapping = store ? TextWrapping.Wrap : TextWrapping.NoWrap;

            // 2026-11-16 用户令：四颗操作键全放在片名下方那行日期下面，商店宽窄同一位置，退出商店才还原。
            PlaceStoreActionButtons(store);

            // 2026-11-16 用户令「商店模式的字幕和音频放到同一行」。
            PlaceStoreTrackRow(store);

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
    /// 商店模式的操作键（2026-11-16 用户令：四颗键全放在片名下面那行「播放日期」的下方）：
    /// 已看、收藏、更多三颗收成图标排成一行，主播放键放在它们下面，宽度与上面那一行相同；
    /// 播放键回到 2026-11-14 的主题色药丸样式（用户令「还原成之前的样式」），带「播放」文字。
    /// 四颗保留原控件、命令、自动化名称与状态；连播仍挂在集列表卡片右上角。
    /// <para>
    /// 搬的是同一批实例，绑定、命令、焦点不重接；退出商店模式时
    /// 按「播放、预告、花絮、连播、已看、收藏、更多」的原次序放回 <c>ColumnActionRow</c>。
    /// 已看那颗键收成图标：文字组的显隐由 <see cref="ViewModels.DetailViewModel.ColumnWatchedLabelsVisibility"/>
    /// 收起。三颗图标与下方音频/字幕选择框共用同一支固定面色和小圆角（2026-11-16 用户令），
    /// 底色仍由 <see cref="ControlBackgroundBrushes"/> 种进控件局部资源的 ButtonBackground* 承担。
    /// </para>
    /// </summary>
    private void PlaceStoreActionButtons(bool store)
    {
        if (store)
        {
            MoveToPanel(ColumnWatchedButton, StoreCoverActions);
            MoveToPanel(ColumnFavoriteButton, StoreCoverActions);
            MoveToPanel(ColumnMoreButton, StoreCoverActions);
            var icons = new[] { ColumnWatchedButton, ColumnFavoriteButton, ColumnMoreButton };
            for (var index = 0; index < icons.Length; index++)
            {
                Grid.SetColumn(icons[index], index);
                icons[index].HorizontalAlignment = HorizontalAlignment.Center;
                icons[index].VerticalAlignment = VerticalAlignment.Center;
            }

            MoveToPanel(ColumnPlayButton, StorePlaySlot);
            ColumnPlayButton.HorizontalAlignment = HorizontalAlignment.Stretch;

            if (!ReferenceEquals(EpisodeHead.Trailing, ColumnContinueButton))
            {
                Detach(ColumnContinueButton);
                EpisodeHead.Trailing = ColumnContinueButton;
            }
            var storeStyle = (Style)Resources["EgStoreCoverActionButtonStyle"];
            foreach (var button in icons)
            {
                if (!ReferenceEquals(button.Style, storeStyle)) button.Style = storeStyle;
                // 底色是 PrepareAppearance 种下的那组材质画刷（和闭合选择框同一支固定面色）；
                // 商店档不另铺一块，也不用透明常态把它清掉。
                if (button.Resources.ContainsKey("ButtonBackground"))
                    button.Background = (Microsoft.UI.Xaml.Media.Brush)button.Resources["ButtonBackground"];
                button.Opacity = 1;
            }
            // 主播放键走回带字的药丸键：底色是 PrepareAppearance 种下的那组画刷，
            // 由 ApplyAppearance 在商店档涂成强调色面（见 IsStorePlay）。
            if (!ReferenceEquals(ColumnPlayButton.Style, Resources["EgPlayButtonStyle"]))
                ColumnPlayButton.Style = (Style)Resources["EgPlayButtonStyle"];
            if (ColumnPlayButton.Resources.ContainsKey("ButtonBackground"))
                ColumnPlayButton.Background = (Microsoft.UI.Xaml.Media.Brush)ColumnPlayButton.Resources["ButtonBackground"];
            SyncStoreActionWidth();
            return;
        }

        if (ReferenceEquals(EpisodeHead.Trailing, ColumnContinueButton)) EpisodeHead.Trailing = null;
        RestoreToActions(ColumnPlayButton, 0);
        ColumnPlayButton.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
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

    /// <summary>
    /// 商店模式把音频与字幕并排在同一行（2026-11-16 用户令「商店模式的字幕和音频放到同一行」）：
    /// 两行 Grid 连同里面的标签、选择框都是既有实例，搬进横排的 <c>StoreTrackRow</c>，
    /// 绑定、命令、焦点和行高都不动；退出商店模式时按「音频、字幕」的原次序放回 <c>ColumnPickers</c>，
    /// 排在那颗收起的空行后面 —— 非商店版式仍是一行一颗。
    /// <para>
    /// 2026-11-16 用户令「该回去原来怎么显示的就怎么显示，去掉前面的自动」：商店档只把两颗下拉的<b>收起态</b>
    /// 换成 <see cref="Momoka.Emby.TrackRow.ShortText"/> —— 仍是原来的完整轨道名，只是不带「自动 · 」前缀；
    /// 展开列表照旧带前缀，沉浸版式和 <c>DisplayMemberPath</c> 都不动
    /// （见 <see cref="EgPicker.SelectionTextMemberPath"/>）。
    /// </para>
    /// <para>
    /// 同一句令还带出两条：只有一条音轨时音频不摆选择框、这一行直接显示轨道名
    /// （<see cref="ViewModels.DetailViewModel.AudioPickerVisibility"/>，「只有一个能选你弄什么选择栏，直接显示就好」）；
    /// 字幕紧贴在音频右边（2026-11-16 用户令「固定在音频右边，别跟着窗口往右移动」「不要省略，直接压在背景图上」），
    /// 两颗都按内容撑到自然宽，超出的那截压在右侧背景图上，不摊到窗口宽度里。
    /// </para>
    /// </summary>
    private void PlaceStoreTrackRow(bool store)
    {
        var member = store ? nameof(Momoka.Emby.TrackRow.ShortText) : null;
        ColumnAudioPicker.SelectionTextMemberPath = member;
        ColumnSubtitlePicker.SelectionTextMemberPath = member;

        if (store)
        {
            HookStoreTrackColumns();
            ColumnPickers.Children.Remove(AudioPickerRow);
            ColumnPickers.Children.Remove(SubtitlePickerRow);
            if (!StoreTrackRow.Children.Contains(AudioPickerRow))
            {
                StoreTrackRow.Children.Add(AudioPickerRow);
                Canvas.SetLeft(AudioPickerRow, 0);
                Canvas.SetTop(AudioPickerRow, 0);
            }
            if (!StoreTrackRow.Children.Contains(SubtitlePickerRow))
            {
                StoreTrackRow.Children.Add(SubtitlePickerRow);
                Canvas.SetTop(SubtitlePickerRow, 0);
            }
            SyncStoreTrackColumns();
            return;
        }

        StoreTrackRow.Children.Remove(AudioPickerRow);
        StoreTrackRow.Children.Remove(SubtitlePickerRow);
        StoreTrackRow.Visibility = Visibility.Collapsed;
        var index = ColumnPickers.Children.IndexOf(StoreTrackRow);
        index = index < 0 ? ColumnPickers.Children.Count : index + 1;
        if (!ColumnPickers.Children.Contains(AudioPickerRow))
            ColumnPickers.Children.Insert(Math.Min(index, ColumnPickers.Children.Count), AudioPickerRow);
        if (!ColumnPickers.Children.Contains(SubtitlePickerRow))
            ColumnPickers.Children.Insert(Math.Min(index + 1, ColumnPickers.Children.Count), SubtitlePickerRow);
    }

    private bool _storeTrackColumnsHooked;

    /// <summary>音频那一行的宽一变，字幕的 Left 要跟着挪；回调只挂一次。</summary>
    private void HookStoreTrackColumns()
    {
        if (_storeTrackColumnsHooked) return;
        _storeTrackColumnsHooked = true;
        ColumnAudioPicker.RegisterPropertyChangedCallback(UIElement.VisibilityProperty,
            (_, _) => SyncStoreTrackColumns());
        SubtitlePickerRow.RegisterPropertyChangedCallback(UIElement.VisibilityProperty,
            (_, _) => SyncStoreTrackColumns());
        AudioPickerRow.SizeChanged += (_, _) => SyncStoreTrackColumns();
    }

    /// <summary>
    /// Canvas 里那一排的位置：音频始终在左，字幕按音频的实测宽贴在它右边（间距 14，和原来两列时一致）。
    /// Canvas 不裁子内容，字幕长过详情栏时直接压在右侧背景图上，不省略、也不跟着窗口漂。
    /// 两颗都收起时整排一个像素都不留。
    /// </summary>
    private void SyncStoreTrackColumns()
    {
        if (!ViewModel.StoreMode) return;

        var audio = AudioPickerRow.Visibility == Visibility.Visible;
        var subtitle = SubtitlePickerRow.Visibility == Visibility.Visible;
        Canvas.SetLeft(AudioPickerRow, 0);
        Canvas.SetLeft(SubtitlePickerRow, audio ? AudioPickerRow.ActualWidth + 14 : 0);
        StoreTrackRow.Visibility = audio || subtitle ? Visibility.Visible : Visibility.Collapsed;
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

    /// <summary>
    /// 三颗图标那一行跟着主播放键的实测宽走（2026-11-16 用户令「播放按钮的长度等于上面三个按钮的长度」）：
    /// 播放键的内容（「继续播放 20:34」这类文字）决定药丸多长，上面那行三列等分同一个宽度。
    /// </summary>
    private void SyncStoreActionWidth()
    {
        if (!ViewModel.StoreMode || StorePlaySlot.ActualWidth <= 0) return;
        StoreCoverActions.Width = StorePlaySlot.ActualWidth;
    }

    private void OnStorePlaySlotSizeChanged(object sender, SizeChangedEventArgs args) => SyncStoreActionWidth();

    private void OnStoreHeroSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (!ViewModel.StoreMode) return;

        SyncStoreActionWidth();
        ViewModel.StoreDetailsRoom = StoreHeroDetails.ActualHeight + StoreHeroDetails.Margin.Top;
    }
}
