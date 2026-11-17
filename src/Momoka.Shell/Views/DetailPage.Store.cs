using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Theming;
using Windows.Foundation;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    /// <summary>
    /// 商店档三颗图标键（已看、收藏、更多）常态的「框」：透明。
    /// 2026-11-17 用户令「这三个按钮的框改为鼠标移到按钮上的时候才显示」——
    /// 指针移上去之后由按钮模板的 PointerOver／Pressed 读局部 <c>ButtonBackground*</c> 画刷，
    /// 仍是与下方选择框同一支固定面色（见 <see cref="ControlBackgroundBrushes"/>）。
    /// </summary>
    private static readonly SolidColorBrush StoreActionRestFrame = new(Microsoft.UI.Colors.Transparent);

    private StoreBackdropFade? _storeBackdropFade;
    private StoreBackdropFade? _storeAmbientFade;
    private readonly StoreBackdropStyle.Tint _storeTint = new();
    private readonly StoreAmbientBrush _storeAmbientBrush = new();
    private readonly StoreBackdropStyle.Wash _storeArtWash = new();

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
        // 艺术图那一档没有剧照可铺，顶部换成封面取色的纯色底（2026-11-17 用户令「要纯色，铺满左上角和
        // 右上角」）：一整片同色、纵向淡出，左右不收边，两个上角都铺满；背景图/预告那一档不画。
        var artShown = ViewModel.StoreMode && ViewModel.StoreArtSelected && !_actionAccessibility.HighContrast;
        StoreArtWash.Visibility = artShown ? Visibility.Visible : Visibility.Collapsed;
        if (artShown)
        {
            StoreArtWash.Background = _storeArtWash.Resolve(ViewModel.StillColorPixels, ThemeHost.Current);
        }
        else
        {
            StoreArtWash.Background = null;
            _storeArtWash.Clear();
        }
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

            // 2026-11-17 卡片与窗口间距那条规则：内容列不再收进 1840 居中，而是铺满窗口，由视图模型的
            // 头／尾／纸面留白把整列内容按「1500 之后 170、2250 以上随窗口等比」收窄。装饰带继续铺满窗口，
            // 列已经不窄了，bleed 因此是 0；这一支保留原来的形状，装饰层的四条边距仍从同一处写。
            StoreScrollContent.MaxWidth = double.PositiveInfinity;
            StoreScrollContent.HorizontalAlignment = store ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            var columnWidth = Body.ActualWidth;
            if (Body.ActualWidth > 0)
            {
                StoreScrollContent.Width = store ? columnWidth : double.NaN;
                ViewModel.PageWidth = columnWidth;
                // 窗口真实宽另存一份：右侧剧照／预告与窗口右边缘之间的空隙仍按窗口宽算。
                ViewModel.WindowWidth = Body.ActualWidth;
            }
            // 2026-11-16 用户令「渐变要填满左右两边」：商店档的装饰带（环境底与淡墨）跟着整扇窗口走。
            // 2026-11-17 内容列改成铺满窗口之后不再需要负边距越过它（这一项是 0），但装饰层仍共用这一句。
            // 2026-11-17 用户令「加大背景图和右边窗口边缘之间的间距，跟随窗口不断变宽」：视口右沿收在窗口边上
            // 那道随窗口加宽的空隙里，视频底下那块局部色也跟着装饰带一起铺满窗口，右边不再短一截。
            var bleed = store && Body.ActualWidth > columnWidth ? (Body.ActualWidth - columnWidth) / 2 : 0;
            var bleedMargin = new Thickness(-bleed, 0, -bleed, 0);
            StoreAmbient.Margin = bleedMargin;
            StoreShade.Margin = bleedMargin;
            // 艺术图那一档的纯色底也铺到窗口两侧：用户令「铺满左上角和右上角」。
            StoreArtWash.Margin = bleedMargin;
            StoreTrailerFoundation.Margin = bleedMargin;
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
    /// 四颗保留原控件、命令、自动化名称与状态；连播在商店档是集列表卡片右上角那颗开关
    /// （<c>StoreContinueSwitch</c>，2026-11-16 用户令），沉浸档的药丸留在操作排里。
    /// <para>
    /// 搬的是同一批实例，绑定、命令、焦点不重接；退出商店模式时
    /// 按「播放、预告、花絮、连播、已看、收藏、更多」的原次序放回 <c>ColumnActionRow</c>，开关回到排尾收起。
    /// 已看那颗键收成图标：文字组的显隐由 <see cref="ViewModels.DetailViewModel.ColumnWatchedLabelsVisibility"/>
    /// 收起。三颗图标与下方音频/字幕选择框共用同一支固定面色和小圆角（2026-11-16 用户令），
    /// 底色仍由 <see cref="ControlBackgroundBrushes"/> 种进控件局部资源的 ButtonBackground* 承担。
    /// 2026-11-17 用户令「这三个按钮的框改为鼠标移到按钮上的时候才显示」：常态改成透明，
    /// 悬停与按下才由模板读那组画刷画出框（见 <see cref="StoreActionRestFrame"/>）。
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

            // 连播在商店档是集卡右上角那颗开关（2026-11-16 用户参考图「把商店页面的连播开关改为这种」，
            // 左边带「连播」标签）；沉浸档那颗药丸此时由 ColumnContinueVisibility 收起，留在操作排里不占位。
            if (!ReferenceEquals(EpisodeHead.Trailing, StoreContinueEntry))
            {
                Detach(StoreContinueEntry);
                EpisodeHead.Trailing = StoreContinueEntry;
            }
            var storeStyle = (Style)Resources["EgStoreCoverActionButtonStyle"];
            foreach (var button in icons)
            {
                if (!ReferenceEquals(button.Style, storeStyle)) button.Style = storeStyle;
                // 2026-11-17 用户令「这三个按钮的框改为鼠标移到按钮上的时候才显示」：常态不铺框，
                // 指针移上去才由模板的 PointerOver／Pressed 读那组固定面色画刷（与下方选择框同一档）。
                // 退出商店模式时下面那段照原样把 ButtonBackground 铺回去。
                button.Background = StoreActionRestFrame;
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

        if (ReferenceEquals(EpisodeHead.Trailing, StoreContinueEntry)) EpisodeHead.Trailing = null;
        // 开关那一整条回到操作排末尾（自己收起，不占位）；沉浸档的连播药丸一直在原位，不用搬。
        RestoreToActions(StoreContinueEntry, ColumnActionRow.Children.Count);
        RestoreToActions(ColumnPlayButton, 0);
        ColumnPlayButton.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
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

    private void OnStorePlaySlotSizeChanged(object sender, SizeChangedEventArgs args)
    {
        SyncStoreActionWidth();
        MeasureStoreCover();
    }

    /// <summary>
    /// 商店档封面该长到哪儿：右栏从徽标上沿到主播放键下沿的那一段实测高（2026-11-17 用户令「把商店模式下
    /// 媒体详情页左上角的封面放大一点，让它上方和徽标上方平行，下方和播放按钮下方平行」）。
    /// <para>
    /// 封面与片名那一栏同一行、都从 <c>HeroContent</c> 的上沿起排，所以量 <c>StoreCoverColumn</c> 的上沿与
    /// <c>StorePlaySlot</c> 的下沿之差，就是这一格该长的高。量变换后的边界而不是直接读 <c>ActualHeight</c>：
    /// 这一栏里将来多一样东西、或者先把别的行加进来，封面照样只跟到播放键下沿。
    /// </para>
    /// <para>
    /// 主播放键收起或者还没布出来时交 0，视图模型退回 180×270 的兜底盒。片名那一栏的实测高变了
    /// （<c>OnHeroStackSizeChanged</c>）和这一格换了几何时（搬进商店档、播放文字变了）都来这里重报一次。
    /// </para>
    /// </summary>
    private void MeasureStoreCover()
    {
        if (!ViewModel.StoreMode || ColumnPlayButton.Visibility != Visibility.Visible
            || StorePlaySlot.ActualHeight <= 0 || HeroContent.XamlRoot is null)
        {
            ViewModel.StoreCoverRoom = 0;
            return;
        }

        var top = StoreCoverColumn.TransformToVisual(HeroContent).TransformPoint(new Point(0, 0)).Y;
        var bottom = StorePlaySlot.TransformToVisual(HeroContent)
            .TransformBounds(new Rect(0, 0, StorePlaySlot.ActualWidth, StorePlaySlot.ActualHeight)).Bottom;
        ViewModel.StoreCoverRoom = Math.Max(0, Math.Round(bottom - top));
    }

    private void OnStoreHeroSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (!ViewModel.StoreMode) return;

        SyncStoreActionWidth();
        ViewModel.StoreDetailsRoom = StoreHeroDetails.ActualHeight + StoreHeroDetails.Margin.Top;
    }
}
