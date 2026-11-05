using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Momoka.Danmaku;
using Momoka.Mpv;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

/// <summary>集成浮层与独占弹窗共用的弹幕面板。请求与偏好写入在驻留 view model，页面只画与接输入。</summary>
public sealed partial class DanmakuPanel : UserControl
{
    private PlayerViewModel? _viewModel;
    private PlayerViewModel.InteractionContext _context;
    private readonly ObservableCollection<DandanPlayEpisode> _results = [];
    private bool _seeding;
    private bool _sourceSeeding;
    private DanmakuSource _searchSource = DanmakuSource.DandanPlay;
    private DanmakuSource? _resultsSource;
    private int _resultsConnectionVersion;
    private bool _closed;
    private int _searchRequest;
    private readonly ObservableCollection<string> _fontFamilies = [];

    public DanmakuPanel()
    {
        InitializeComponent();
        DensityModeBox.ItemsSource = new[] { "正常", "较多", "重叠" };
        BorderStyleBox.ItemsSource = new[] { "描边", "重墨", "45° 投影" };
        FontFamilyBox.ItemsSource = _fontFamilies;
        Results.ItemsSource = _results;
        Loaded += (_, _) =>
        {
            _closed = false;
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnPlayerChanged;
                _viewModel.PropertyChanged += OnPlayerChanged;
            }
            _ = LoadFontsAsync();
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
        SeedSources(viewModel);
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
        ShowToggle.Visibility = Visibility.Collapsed;
        TitleBox.Visibility = Visibility.Collapsed;
        EpisodeBox.Visibility = Visibility.Collapsed;
        SettingsButton.Visibility = Visibility.Collapsed;
        Appearance.Visibility = Visibility.Collapsed;
        SourceBox.Visibility = Visibility.Collapsed;
        SearchButton.Visibility = Visibility.Collapsed;
        Results.Visibility = Visibility.Collapsed;
        LoadButton.Visibility = Visibility.Collapsed;
        return false;
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.DanmakuShown) && Current()) Seed();
        else if (e.PropertyName == nameof(PlayerViewModel.DanmakuAvailable) && _viewModel is { DanmakuAvailable: false }) Current();
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
            ScaleCheck.IsChecked = settings.ScaleWithWindow;
            SubtitleCheck.IsChecked = settings.AvoidSubtitles;
            OverlapCheck.IsChecked = settings.PreventOverlap;
            OverlapCheck.Visibility = settings.DensityMode == 2 ? Visibility.Collapsed : Visibility.Visible;
            SpeedSyncCheck.IsChecked = settings.SpeedSyncPlayback;
            BoldCheck.IsChecked = settings.Bold;
            DensityModeBox.SelectedIndex = settings.DensityMode;
            BorderStyleBox.SelectedIndex = settings.BorderStyle;
            if (!_fontFamilies.Contains(settings.FontFamily)) _fontFamilies.Insert(0, settings.FontFamily);
            FontFamilyBox.SelectedItem = settings.FontFamily;
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

    private async Task LoadFontsAsync()
    {
        if (_viewModel is null) return;
        var catalogue = await _viewModel.DanmakuFontsAsync().ConfigureAwait(true);
        if (_closed || _viewModel is null || XamlRoot is null) return;
        _seeding = true;
        try
        {
            _fontFamilies.Clear();
            foreach (var family in catalogue.Families) _fontFamilies.Add(family.Name);
        }
        finally { _seeding = false; }
        if (Current()) Seed();
    }

    private async void OnSearch(object sender, RoutedEventArgs e)
    {
        if (!Current() || _viewModel is null) return;
        var request = ++_searchRequest;
        var source = _searchSource;
        var connectionVersion = _viewModel.DanmakuConnectionVersion;
        SearchButton.IsEnabled = false;
        try
        {
            var matches = await _viewModel.SearchDanmakuAsync(TitleBox.Text, EpisodeBox.Text.Trim(), source).ConfigureAwait(true);
            if (request != _searchRequest || !Current() || connectionVersion != _viewModel.DanmakuConnectionVersion) return;
            _results.Clear();
            _resultsSource = source;
            _resultsConnectionVersion = connectionVersion;
            foreach (var match in matches.Episodes) _results.Add(match);
            Results.Visibility = _results.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { if (!_closed && request == _searchRequest) SearchButton.IsEnabled = true; }
    }

    private void OnResultSelected(object sender, SelectionChangedEventArgs e) =>
        LoadButton.Visibility = Results.SelectedItem is DandanPlayEpisode ? Visibility.Visible : Visibility.Collapsed;

    private async void OnLoad(object sender, RoutedEventArgs e)
    {
        if (!Current() || _viewModel is null || Results.SelectedItem is not DandanPlayEpisode episode
            || _resultsSource is not { } source || !_results.Contains(episode)
            || _resultsConnectionVersion != _viewModel.DanmakuConnectionVersion) return;
        LoadButton.IsEnabled = false;
        try { await _viewModel.LoadDanmakuAsync(episode, source).ConfigureAwait(true); }
        finally { if (!_closed) LoadButton.IsEnabled = true; }
    }

    private void SeedSources(PlayerViewModel viewModel)
    {
        var sources = viewModel.DanmakuSearchSources();
        _sourceSeeding = true;
        try
        {
            SourceBox.ItemsSource = sources;
            SourceBox.DisplayMemberPath = nameof(DanmakuSourceOption.Label);
            SourceBox.SelectedIndex = sources.Count > 0 ? 0 : -1;
            _searchSource = sources.Count > 0 ? sources[0].Source : DanmakuSource.DandanPlay;
            SourceBox.Visibility = sources.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            SearchButton.Content = sources.Count > 0 ? $"搜索{sources[0].Label}" : "搜索";
        }
        finally { _sourceSeeding = false; }
    }

    private void OnSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sourceSeeding || SourceBox.SelectedItem is not DanmakuSourceOption option) return;
        _searchRequest++;
        _viewModel?.CancelDanmakuSearch();
        _resultsSource = null;
        SearchButton.IsEnabled = true;
        _searchSource = option.Source;
        SearchButton.Content = $"搜索{option.Label}";
        _results.Clear();
        Results.Visibility = Visibility.Collapsed;
        LoadButton.Visibility = Visibility.Collapsed;
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

    private void OnAppearanceChoiceChanged(object sender, SelectionChangedEventArgs e) => ApplyAppearance();
    private void OnAppearanceChanged(object sender, RoutedEventArgs e) => ApplyAppearance();

    private void ApplyAppearance()
    {
        if (_seeding || _viewModel is null || !Current()) return;
        _viewModel.ChangeDanmakuPreferences(settings =>
        {
            settings.ScaleWithWindow = ScaleCheck.IsChecked == true;
            settings.AvoidSubtitles = SubtitleCheck.IsChecked == true;
            settings.PreventOverlap = OverlapCheck.IsChecked == true;
            settings.SpeedSyncPlayback = SpeedSyncCheck.IsChecked == true;
            settings.Bold = BoldCheck.IsChecked == true;
            settings.DensityMode = DensityModeBox.SelectedIndex;
            settings.BorderStyle = BorderStyleBox.SelectedIndex;
            if (FontFamilyBox.SelectedItem is string font) settings.FontFamily = font;
        });
    }

    private void OnRestoreAppearance(object sender, RoutedEventArgs e)
    {
        if (Current()) _viewModel?.ChangeDanmakuPreferences(settings => settings.RestoreAppearance());
    }

    private void OnSettings(object sender, RoutedEventArgs e) => _viewModel?.RequestSettings(PlayerSettingsLinks.For("danmaku")!);
}
