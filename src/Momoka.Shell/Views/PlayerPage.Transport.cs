using Momoka.Playback;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Momoka.Shell.Views;

public sealed partial class PlayerPage
{
    private bool _timelinePointerDown;

    private void WireTransport()
    {
        WireInlineSpeed();
        WireTimelineArtwork();
        SeekSlider.BeginDrag = BeginTimelineGesture;
        SeekSlider.PressPosition = PressTimelineChapter;
        SeekSlider.FinishDrag = FinishTimelineGesture;
        SeekSlider.PositionValue = TimelinePositionValue;
        SeekSlider.KeyCommand = key =>
        {
            if (!Attached || _inputSuspended || key == Windows.System.VirtualKey.Space || !Dispatch(key, out var wake)) return false;
            if (wake && _chrome.WakeFully(Now)) Render();
            return true;
        };
        Rail.RightTapped += OnRailReset;
        SpeedButton.PointerWheelChanged += OnSpeedButtonWheel;
        SpeedButton.RightTapped += OnSpeedReset;
        SeekSlider.ValueChanged += OnTransportValueChanged;
        VolumeSlider.ValueChanged += OnTransportValueChanged;
        foreach (var button in new ButtonBase[] { TransportStatsButton, PreviousChapterButton, NextChapterButton })
            button.Click += OnChromeClick;
        ChapterMenu.Opened += (_, _) => Hold(true, ChromeHold.Menu);
        ChapterMenu.Closed += (_, _) => Hold(false, ChromeHold.Menu);
    }

    private void OnTransportValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        PaintTransportReadouts();
        if (Attached && ReferenceEquals(sender, SeekSlider)) PaintBufferedClock(SeekTrack.ActualWidth, ViewModel.Status);
    }

    private void PaintTransportReadouts()
    {
        if (TimelineLabelClip is null || VolumeLabelClip is null) return;
        var width = SeekTrack.ActualWidth;
        var height = SeekTrack.ActualHeight;
        TimelineLabelClip.Rect = new Rect(0, 0, Math.Max(0, width * SeekSlider.Value / SeekSlider.Maximum), height);
        var volumeHeight = VolumeTrack.ActualHeight;
        var filled = volumeHeight * VolumeScale.Fraction(VolumeSlider.Value);
        // 裁剪宽＝轨宽。以前手抄 40，音量条随档涨到 52 之后数字会被裁掉一截 —— 改读现值；
        // 布局还没量过（ActualWidth 为 0）时拿滑杆的 Width 兜底，它俩同宽、同批同档。
        var trackWidth = VolumeTrack.ActualWidth;
        if (trackWidth <= 0) trackWidth = VolumeSlider.Width;
        VolumeLabelClip.Rect = new Rect(0, Math.Max(0, volumeHeight - filled), trackWidth, Math.Max(0, filled));
        VolumeHundredMark.Margin = new Thickness(0, volumeHeight * (1 - 100 / VolumeSlider.Maximum), 0, 0);
    }

    private void OnVolumeTrackResized(object sender, SizeChangedEventArgs e) => PaintTransportReadouts();

    private void OnTransportResized(object sender, SizeChangedEventArgs e) => ArrangeTransport();

    private void ArrangeTransport()
    {
        if (TransportRow is null || !Attached) return;
        // An explicit width is used by resize/layout probes and can arrive one layout pass before
        // ActualWidth. Use the tighter current constraint; ordinary responsive UI keeps Width=NaN.
        var width = double.IsFinite(Root.Width) && Root.Width > 0
            ? Math.Min(Root.ActualWidth, Root.Width) : Root.ActualWidth;
        // The breakpoints describe window-sized controls. Fullscreen/maximized controls are
        // 1.3x larger, so the same physical row must enter its compact mode earlier.
        var logicalWidth = width / ChromeFactor;
        var compact = logicalWidth < 680;
        var tiny = logicalWidth < 520;
        var minimal = logicalWidth < 300;
        var chapters = ViewModel.ChapterMarks.Count > 0;
        TransportStatsButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ChapterButton.Visibility = compact || !chapters ? Visibility.Collapsed : Visibility.Visible;
        PreviousChapterButton.Visibility = compact || !chapters ? Visibility.Collapsed : Visibility.Visible;
        NextChapterButton.Visibility = compact || !chapters ? Visibility.Collapsed : Visibility.Visible;
        PlayButton.Visibility = compact && logicalWidth >= 188 ? Visibility.Visible : Visibility.Collapsed;
        PreviousButton.Visibility = tiny ? Visibility.Collapsed : ViewModel.EpisodeControlsVisibility;
        NextButton.Visibility = PreviousButton.Visibility;
        EpisodeButton.Visibility = PreviousButton.Visibility;
        VersionButton.Visibility = tiny ? Visibility.Collapsed : ViewModel.VersionControlsVisibility;
        AudioButton.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
        SubtitleButton.Visibility = AudioButton.Visibility;
        // 倍速条宽度档 × 档位倍率（复刻独占 scale 策略的动态两处之一）：窗口档照旧 100/146，
        // 大档（全屏或最大化）×1.3。形态一变这里会被 ApplyTitleScale 与 OnRootResized 各请一遍。
        SpeedStrip.Width = (compact ? 100 : 146) * ChromeFactor;
        SpeedStrip.Visibility = logicalWidth < 220 ? Visibility.Collapsed : Visibility.Visible;
        SpeedButton.Visibility = logicalWidth is >= 160 and < 220 ? Visibility.Visible : Visibility.Collapsed;
        // Automatic columns never shrink their children. Reserve the visible left/right slots
        // before retaining the optional speed strip, including full-screen chrome's larger cells.
        var cell = BigChrome ? FullscreenTransportCell : WindowTransportCell;
        var gap = BigChrome ? FullscreenTransportGap : WindowTransportGap;
        var narrowSlots = (PlayButton.Visibility == Visibility.Visible ? cell : 0)
            + (DanmakuButton.Visibility == Visibility.Visible ? cell + gap : 0)
            + (minimal ? cell : cell * 4) + gap * 5;
        var speedRoom = width - TransportRow.Margin.Left - TransportRow.Margin.Right - narrowSlots;
        if (tiny && SpeedStrip.Visibility == Visibility.Visible && SpeedStrip.Width > speedRoom)
            SpeedStrip.Visibility = Visibility.Collapsed;
        // 与 uosc 一样在顶栏和底栏之间居中，随可用高度缩短，保留静音键的完整格子。
        var top = TitleStrip.Height;
        var bottom = BarHeight();
        var railHeight = ChromeReveal.RailHeight(Root.ActualHeight - top - bottom, Rail.Width);
        VolumeTrack.Height = Math.Max(0, railHeight - Rail.Width);
        var margin = new Thickness(0, top, Rail.Margin.Right, bottom);
        if (Rail.Margin != margin) Rail.Margin = margin;
        PaintTransportReadouts();
        RenderSpeedNotches();
    }

    private bool BeginTimelineGesture()
    {
        if (!Attached || _inputSuspended || !ViewModel.BeginTimelineDrag()) return false;
        _timelinePointerDown = true;
        Hold(true, ChromeHold.Timeline);
        return true;
    }

    private double TimelinePositionValue(double x, double y)
    {
        if (!Attached) return SeekSlider.Value;
        var fraction = ViewModel.Status.HasDuration
            ? TimelineScale.TimeAt(x, SeekTrack.ActualWidth, ViewModel.Status.Duration) / ViewModel.Status.Duration : 0;
        ViewModel.MoveTimeline(fraction);
        UpdateTimelineHover(x, y);
        return fraction * PlayerViewModel.SeekScale;
    }

    private void FinishTimelineGesture()
    {
        if (!_timelinePointerDown) return;
        _timelinePointerDown = false;
        if (Attached) _ = ViewModel.EndTimelineDragAsync();
        Hold(false, ChromeHold.Timeline);
        if (!PointerOverSeekTrack()) HideChapterPeek();
    }

    private void OnRailReset(object sender, RightTappedRoutedEventArgs e)
    {
        if (!Attached) return;
        ViewModel.ResetVolume();
        if (_chrome.FlashRail(Now)) Render();
        e.Handled = true;
    }

    private void OnSpeedButtonWheel(object sender, PointerRoutedEventArgs e)
    {
        if (!Attached) return;
        var delta = e.GetCurrentPoint(SpeedButton).Properties.MouseWheelDelta;
        if (delta != 0) ViewModel.NudgeSpeed(delta > 0 ? 0.1 : -0.1);
        e.Handled = true;
    }

    private void OnSpeedReset(object sender, RightTappedRoutedEventArgs e)
    {
        if (!Attached) return;
        ViewModel.SetSpeed(1);
        e.Handled = true;
    }

    private void OnPreviousChapter(object sender, RoutedEventArgs e) => ViewModel.StepChapter(-1);
    private void OnNextChapter(object sender, RoutedEventArgs e) => ViewModel.StepChapter(1);

    private void OnChapterMenuOpening(object sender, object e)
    {
        ChapterMenu.Items.Clear();
        if (!Attached) return;
        var selected = ViewModel.TimelineChapters.IndexAt(ViewModel.Status.Position);
        for (var index = 0; index < ViewModel.TimelineChapters.Chapters.Count; index++)
        {
            var mark = ViewModel.TimelineChapters.Chapters[index];
            var row = new RadioMenuFlyoutItem
            {
                Text = $"{TimelineScale.Clock(mark.Start, ViewModel.Status.Duration)}  {mark.Title}",
                GroupName = "PlaybackChapters",
                IsChecked = index == selected
            };
            row.Click += (_, _) => ViewModel.SeekTimelineChapter(mark.Start);
            ChapterMenu.Items.Add(row);
        }
        if (ChapterMenu.Items.Count == 0)
            ChapterMenu.Items.Add(new MenuFlyoutItem { Text = "这个文件没有章节", IsEnabled = false });
    }
}
