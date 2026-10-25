using System.Collections.ObjectModel;
using System.ComponentModel;
using Momoka.Danmaku;
using Momoka.Mpv;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Momoka.Shell.Views;

/// <summary>集成浮层与独占弹窗共用的弹幕面板。请求与偏好写入在驻留 view model，页面只画与接输入。</summary>
public sealed partial class DanmakuPanel : UserControl
{
    private PlayerViewModel? _viewModel;
    private PlayerViewModel.InteractionContext _context;
    private readonly ObservableCollection<DandanPlayEpisode> _results = [];
    private bool _seeding;
    private bool _closed;
    private int _searchRequest;

    public DanmakuPanel()
    {
        InitializeComponent();
        Results.ItemsSource = _results;
        Loaded += (_, _) =>
        {
            _closed = false;
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnPlayerChanged;
                _viewModel.PropertyChanged += OnPlayerChanged;
            }
        };
        Unloaded += (_, _) =>
        {
            if (XamlRoot is not null) return;
            Close();
        };
    }

    internal void Attach(PlayerViewModel viewModel)
    {
        _viewModel = viewModel;
        _context = viewModel.CaptureInteraction();
        DataContext = viewModel;
        TitleBox.Text = viewModel.DanmakuSearchTitle;
        EpisodeBox.Text = viewModel.DanmakuSearchEpisode;
        Seed();
    }

    internal void Close()
    {
        if (_closed) return;
        _closed = true;
        _searchRequest++;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnPlayerChanged;
            _viewModel.CancelDanmakuSearch();
        }
    }

    private bool Current()
    {
        if (!_closed && _viewModel is { DanmakuAvailable: true } vm && vm.IsCurrentPlayback(_context)) return true;
        SessionMessage.Text = "播放或弹幕连接已切换，请重新打开面板。";
        SessionMessage.Visibility = Visibility.Visible;
        Appearance.Visibility = Visibility.Collapsed;
        SearchButton.Visibility = Visibility.Collapsed;
        Results.Visibility = Visibility.Collapsed;
        LoadButton.Visibility = Visibility.Collapsed;
        return false;
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.DanmakuShown) && Current()) Seed();
    }

    private void Seed()
    {
        if (_viewModel is null) return;
        _seeding = true;
        try
        {
            var settings = _viewModel.DanmakuPreferences;
            ShowToggle.IsOn = settings.Show;
            Appearance.Visibility = settings.Show ? Visibility.Visible : Visibility.Collapsed;
            OpacitySlider.Value = settings.Opacity;
            FontSlider.Value = settings.FontSize;
            SpeedSlider.Value = settings.Speed;
            AreaSlider.Value = settings.Area;
            DensitySlider.Value = settings.Density;
            ScrollCheck.IsChecked = settings.Scroll;
            TopCheck.IsChecked = settings.Top;
            BottomCheck.IsChecked = settings.Bottom;
            ColorCheck.IsChecked = settings.Color;
            RepeatCheck.IsChecked = settings.Deduplicate;
            if (BlockBox.FocusState == FocusState.Unfocused) BlockBox.Text = settings.BlockedWords;
            Labels();
        }
        finally { _seeding = false; }
    }

    private async void OnSearch(object sender, RoutedEventArgs e)
    {
        if (!Current() || _viewModel is null) return;
        var request = ++_searchRequest;
        SearchButton.IsEnabled = false;
        try
        {
            var matches = await _viewModel.SearchDanmakuAsync(TitleBox.Text, EpisodeBox.Text.Trim()).ConfigureAwait(true);
            if (request != _searchRequest || !Current()) return;
            _results.Clear();
            foreach (var match in matches.Episodes) _results.Add(match);
            Results.Visibility = _results.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { if (!_closed && request == _searchRequest) SearchButton.IsEnabled = true; }
    }

    private void OnResultSelected(object sender, SelectionChangedEventArgs e) =>
        LoadButton.Visibility = Results.SelectedItem is DandanPlayEpisode ? Visibility.Visible : Visibility.Collapsed;

    private async void OnLoad(object sender, RoutedEventArgs e)
    {
        if (!Current() || _viewModel is null || Results.SelectedItem is not DandanPlayEpisode episode) return;
        LoadButton.IsEnabled = false;
        try { await _viewModel.LoadDanmakuAsync(episode).ConfigureAwait(true); }
        finally { if (!_closed) LoadButton.IsEnabled = true; }
    }

    private void OnShowToggled(object sender, RoutedEventArgs e)
    {
        if (_seeding || _viewModel is null || !Current()) return;
        _viewModel.ChangeDanmakuPreferences(settings => settings.Show = ShowToggle.IsOn);
        Seed();
    }

    private void OnStyleChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_seeding || _viewModel is null || !Current()) return;
        _viewModel.ChangeDanmakuPreferences(settings =>
        {
            settings.Opacity = OpacitySlider.Value;
            settings.FontSize = FontSlider.Value;
            settings.Speed = SpeedSlider.Value;
            settings.Area = AreaSlider.Value;
            settings.Density = (int)DensitySlider.Value;
        });
        Labels();
    }

    private void Labels()
    {
        OpacityLabel.Text = $"不透明度：{OpacitySlider.Value:0}%";
        FontLabel.Text = $"字号：{FontSlider.Value:0}";
        SpeedLabel.Text = $"速度：{SpeedSlider.Value:0.0} 倍";
        AreaLabel.Text = $"显示区域：{AreaSlider.Value:0}%";
        DensityLabel.Text = $"同屏上限：{DensitySlider.Value:0} 条";
    }

    private void OnFiltersChanged(object sender, RoutedEventArgs e)
    {
        if (_seeding || _viewModel is null || !Current()) return;
        _viewModel.ChangeDanmakuPreferences(settings =>
        {
            settings.Scroll = ScrollCheck.IsChecked == true;
            settings.Top = TopCheck.IsChecked == true;
            settings.Bottom = BottomCheck.IsChecked == true;
            settings.Color = ColorCheck.IsChecked == true;
            settings.Deduplicate = RepeatCheck.IsChecked == true;
        });
    }

    private void OnBlockLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_seeding && _viewModel is not null && Current())
            _viewModel.ChangeDanmakuPreferences(settings => settings.BlockedWords = BlockBox.Text);
    }

    private void OnSettings(object sender, RoutedEventArgs e) => _viewModel?.RequestSettings(PlayerSettingsLinks.For("danmaku")!);
}
