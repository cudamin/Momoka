using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

/// <summary>
/// 订阅墙里的一张宽背景卡片。画法参照 <see cref="MoviePilotSubscriptionPoster"/>：Card 一换就无条件发起图片请求
/// （容器回收时 Loaded/Unloaded 不可信，权威信号是 Card 变更），其余视觉全部 x:Bind OneWay 跟着卡片走。
/// 整卡的打开／选择由宿主 ListView 的 ItemClick 接，这里只抛出更多键与图片请求两个事件。
/// </summary>
public sealed partial class MoviePilotSubscriptionWallCard : UserControl
{
    public static readonly DependencyProperty CardProperty = DependencyProperty.Register(nameof(Card),
        typeof(MoviePilotSubscriptionCard), typeof(MoviePilotSubscriptionWallCard), new PropertyMetadata(null, OnCardChanged));

    public MoviePilotSubscriptionWallCard() => InitializeComponent();

    public MoviePilotSubscriptionCard? Card
    {
        get => (MoviePilotSubscriptionCard?)GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    /// <summary>更多键（订阅管理菜单）。sender 是本卡片，宿主从 <see cref="Card"/> 取订阅。</summary>
    public event EventHandler? MenuRequested;
    /// <summary>容器换卡即发起：宿主据此加载海报、底图与订阅人头像。</summary>
    public event EventHandler? ImageRequested;

    private static void OnCardChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var card = (MoviePilotSubscriptionWallCard)sender;
        // 换卡只有一个含义：这个容器要显示这条订阅了。无条件发起，不依赖 Loaded（回收容器不会再补 Loaded）。
        if (card.Card is not null) card.ImageRequested?.Invoke(card, EventArgs.Empty);
    }

    private void OnMore(object sender, RoutedEventArgs e) => MenuRequested?.Invoke(this, EventArgs.Empty);

    private Brush ToneBrush(MoviePilotExecutionTone tone) => MoviePilotWallUi.Tone(tone);
    private Visibility VisibleWhen(bool value) => MoviePilotWallUi.Show(value);
    private Visibility SelectedVisible(bool selected, bool batch) => MoviePilotWallUi.Show(batch && selected);
    private Visibility MoreVisible(bool batch) => MoviePilotWallUi.Show(!batch);
    private Visibility BestVersionVisible(bool bestVersion) => MoviePilotWallUi.Show(bestVersion);
    private Visibility UpdatedVisible(Visibility state, Visibility updated) =>
        state == Visibility.Visible ? Visibility.Collapsed : updated;
    private string SelectionGlyph(bool selected) => selected ? "\uE73E" : "\uE739";
}