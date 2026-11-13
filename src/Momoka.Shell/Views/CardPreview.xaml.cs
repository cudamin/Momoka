using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Infrastructure;

namespace Momoka.Shell.Views;

/// <summary>
/// 悬停卡片的内容与动效；打开时机、位置和图片请求由 PosterCard 管理。
/// 只改变渲染变换，不在动画中改变布局。宽图晚到时保持同一个媒体视口。
/// </summary>
public sealed partial class CardPreview : UserControl
{
    // Microsoft Motion 的 Direct Entrance/Exit：(0,0,0,1)，250/167ms。
    // 等比小幅放大保留从卡片中心出现的关系，不把整段文字横向压成海报宽度。
    private const int EnterMs = 250;
    private const int ExitMs = 167;
    private const int FadeMs = 83;
    private const double PanelPadding = 18;

    private Storyboard? _motion;
    private readonly ImageArrivalFade _artworkArrival;
    private Action? _exitCompleted;

    public CardPreview()
    {
        InitializeComponent();
        Panel.Opacity = 0;
        _artworkArrival = new ImageArrivalFade(PreviewImage);
        Loaded += (_, _) => HomeMotion.PreferenceChanged += OnMotionPreferenceChanged;
        Unloaded += (_, _) =>
        {
            HomeMotion.PreferenceChanged -= OnMotionPreferenceChanged;
            Reset();
        };
    }

    internal event EventHandler? PlayRequested;
    internal FrameworkElement PlayAnchor => PlayButton;

    internal void SetSize(double width)
    {
        Width = width;
        Media.MinHeight = CardPreviewPlacement.MediaHeight(Math.Max(0, width - (PanelPadding * 2) - 2));
    }

    internal void Show(CardItem card)
    {
        var item = card.Item;
        PreviewImage.Source = card.Poster;
        TitleText.Text = card.Title;

        MetaText.Text = CardPreviewText.Meta(item, card.Title);
        MetaText.Visibility = Shown(MetaText.Text.Length > 0);
        GenresText.Text = CardPreviewText.Genres(item);
        GenresText.Visibility = Shown(GenresText.Text.Length > 0);
        RatingText.Text = CardPreviewText.Rating(item);
        RatingBlock.Visibility = Shown(RatingText.Text.Length > 0);
        AutomationProperties.SetName(RatingText, $"评分 {RatingText.Text}");
        // 没有评分时题材从左沿开始，不留下 Grid.ColumnSpacing。
        Grid.SetColumn(GenresText, RatingText.Text.Length > 0 ? 1 : 0);
        Grid.SetColumnSpan(GenresText, RatingText.Text.Length > 0 ? 1 : 2);
        TagsRow.Visibility = Shown(GenresText.Text.Length + RatingText.Text.Length > 0);
        FactsBlock.Visibility = Shown(MetaText.Text.Length + GenresText.Text.Length + RatingText.Text.Length > 0);

        OverviewText.Text = CardPreviewText.Overview(item);
        OverviewText.Visibility = Shown(OverviewText.Text.Length > 0);
        ResumeText.Text = CardPreviewText.Resume(item);
        ResumeBlock.Visibility = Shown(ResumeText.Text.Length > 0);
        ResumeProgress.Value = CardPreviewText.Progress(item);
        AutomationProperties.SetName(ResumeProgress, $"观看进度 {ResumeText.Text}");

        PlayButton.Visibility = card.PlayVisibility;
        PlayText.Text = item.HasResumePosition ? "继续播放" : "播放";
        AutomationProperties.SetName(PlayButton, card.PlayActionTip);
        ToolTipService.SetToolTip(PlayButton, card.PlayActionTip);
    }

    internal void SetArtwork(BitmapImage? artwork)
    {
        if (artwork is not null) PreviewImage.Source = artwork;
    }

    private void OnMediaSizeChanged(object sender, SizeChangedEventArgs e)
    {
        PreviewImage.Width = e.NewSize.Width;
        PreviewImage.Height = e.NewSize.Height;
    }

    internal void PlayEnter()
    {
        var interrupted = _motion is not null;
        var opacity = interrupted ? Panel.Opacity : 0;
        var scale = interrupted ? PanelMotion.ScaleX : 0.96;
        var y = interrupted ? PanelMotion.TranslateY : 4;
        Stop();
        if (!HomeMotion.AnimationsEnabled)
        {
            Settle(1);
            return;
        }

        var board = new Storyboard();
        Add(board, Panel, "Opacity", opacity, 1, FadeMs, linear: true);
        Add(board, PanelMotion, "ScaleX", scale, 1, EnterMs);
        Add(board, PanelMotion, "ScaleY", scale, 1, EnterMs);
        Add(board, PanelMotion, "TranslateY", y, 0, EnterMs);
        Start(board, () => Settle(1));
    }

    internal void PlayExit(Action closed)
    {
        var opacity = Panel.Opacity;
        var scale = PanelMotion.ScaleX;
        var y = PanelMotion.TranslateY;
        Stop();
        if (!HomeMotion.AnimationsEnabled)
        {
            Settle(0);
            closed();
            return;
        }

        _exitCompleted = closed;
        var board = new Storyboard();
        Add(board, Panel, "Opacity", opacity, 0, FadeMs, linear: true);
        Add(board, PanelMotion, "ScaleX", scale, 0.98, ExitMs);
        Add(board, PanelMotion, "ScaleY", scale, 0.98, ExitMs);
        Add(board, PanelMotion, "TranslateY", y, 2, ExitMs);
        Start(board, () =>
        {
            _exitCompleted = null;
            Settle(0);
            closed();
        });
    }

    internal void Reset()
    {
        Stop();
        Settle(0);
        PreviewImage.Source = null;
    }

    private void OnPlayClicked(object sender, RoutedEventArgs e) => PlayRequested?.Invoke(this, EventArgs.Empty);

    private void OnMotionPreferenceChanged()
    {
        if (HomeMotion.AnimationsEnabled || _motion is null) return;
        var closed = _exitCompleted;
        Stop();
        Settle(closed is null ? 1 : 0);
        closed?.Invoke();
    }

    private void Start(Storyboard board, Action completed)
    {
        _motion = board;
        board.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_motion, board)) return;
            _motion = null;
            board.Stop();
            completed();
        };
        board.Begin();
    }

    private void Stop()
    {
        _motion?.Stop();
        _motion = null;
        _exitCompleted = null;
    }

    private void Settle(double opacity)
    {
        Panel.Opacity = opacity;
        PanelMotion.ScaleX = PanelMotion.ScaleY = 1;
        PanelMotion.TranslateY = 0;
    }

    private static void Add(Storyboard board, DependencyObject target, string path, double from, double to,
        int milliseconds, bool linear = false)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = from });
        var end = TimeSpan.FromMilliseconds(milliseconds);
        if (linear)
            animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = end, Value = to });
        else
            animation.KeyFrames.Add(new SplineDoubleKeyFrame
            {
                KeyTime = end,
                Value = to,
                KeySpline = new KeySpline { ControlPoint1 = new(0, 0), ControlPoint2 = new(0, 1) }
            });
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, path);
        board.Children.Add(animation);
    }

    private static Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
