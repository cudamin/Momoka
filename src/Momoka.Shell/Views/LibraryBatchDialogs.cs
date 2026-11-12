using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;

namespace Momoka.Shell.Views;

/// <summary>Native parameter dialogs; the caller owns server reads, identity and writes.</summary>
internal static class LibraryBatchDialogs
{
    internal static async Task<ItemBatchOptions?> PlaylistAsync(FrameworkElement owner, int count,
        IReadOnlyList<EmbyItem> playlists, CancellationToken cancellationToken)
    {
        var list = new ComboBox
        {
            Name = "BatchPlaylist",
            Header = "播放列表",
            DisplayMemberPath = "Name",
            ItemsSource = playlists,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var name = new TextBox { Name = "BatchPlaylistName", Header = "新建播放列表", PlaceholderText = "输入名称以新建" };
        var duplicates = new CheckBox { Name = "BatchPlaylistSkipDuplicates", Content = "跳过重复条目", IsChecked = true };
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = $"将所选 {count} 项添加到已有播放列表，或新建播放列表。", TextWrapping = TextWrapping.Wrap });
        if (playlists.Count > 0) panel.Children.Add(list);
        panel.Children.Add(name);
        panel.Children.Add(duplicates);
        var dialog = Create(owner, "添加到播放列表", "添加", panel);
        var updating = false;
        void Validate()
        {
            duplicates.Visibility = Show(string.IsNullOrWhiteSpace(name.Text));
            dialog.IsPrimaryButtonEnabled = list.SelectedItem is EmbyItem || !string.IsNullOrWhiteSpace(name.Text);
        }
        list.SelectionChanged += (_, _) =>
        {
            if (updating) return;
            updating = true;
            if (list.SelectedItem is not null) name.Text = "";
            updating = false;
            Validate();
        };
        name.TextChanged += (_, _) =>
        {
            if (updating) return;
            updating = true;
            if (!string.IsNullOrWhiteSpace(name.Text)) list.SelectedItem = null;
            updating = false;
            Validate();
        };
        Validate();
        using var registration = CancelDialog(owner, dialog, cancellationToken);
        if (cancellationToken.IsCancellationRequested || await dialog.ShowAsync() != ContentDialogResult.Primary
            || cancellationToken.IsCancellationRequested) return null;
        return list.SelectedItem is EmbyItem chosen
            ? new(PlaylistId: chosen.Id, SkipPlaylistDuplicates: duplicates.IsChecked == true)
            : new(NewPlaylistName: name.Text.Trim());
    }

    internal static async Task<EmbyConversionSelection?> ConvertAsync(FrameworkElement owner, int count,
        EmbyConversionOptions initial, Func<string, CancellationToken, Task<EmbyConversionOptions>> read,
        CancellationToken cancellationToken)
    {
        var targets = Picker("BatchConversionTarget", "转换到");
        var profiles = Picker("BatchConversionProfile", "配置");
        var qualities = Picker("BatchConversionQuality", "质量");
        targets.ItemsSource = initial.Targets;
        var container = Choices("BatchConversionContainer", "容器", "mkv", "mp4", "ts");
        var video = Choices("BatchConversionVideo", "视频编码", "h264", "hevc", "h264,hevc", "hevc,h264");
        var audio = Choices("BatchConversionAudio", "音频编码", "aac", "mp3", "aac,mp3", "aac,mp3,ac3");
        var bitrate = new NumberBox
        {
            Name = "BatchConversionBitrate",
            Header = "码率（Mbps）",
            Minimum = 0.1,
            Maximum = 2000,
            SmallChange = 0.1,
            Value = 8
        };
        var limit = new NumberBox
        {
            Name = "BatchConversionLimit",
            Header = "转换数量上限（留空表示不限）",
            Minimum = 1,
            Maximum = int.MaxValue,
            SmallChange = 1
        };
        var unwatched = new CheckBox { Name = "BatchConversionUnwatched", Content = "仅转换未观看的视频" };
        var newContent = new CheckBox { Name = "BatchConversionNewContent", Content = "自动转换新增内容" };
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var status = new TextBlock { Name = "BatchConversionStatus", TextWrapping = TextWrapping.Wrap };
        var custom = new StackPanel { Spacing = 12, Visibility = Visibility.Collapsed };
        custom.Children.Add(container);
        custom.Children.Add(video);
        custom.Children.Add(audio);
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = $"为所选 {count} 项创建转换任务，由服务器在后台处理。", TextWrapping = TextWrapping.Wrap });
        foreach (var control in new FrameworkElement[] { targets, profiles, description, custom, qualities, bitrate, unwatched, newContent, limit, status })
            panel.Children.Add(control);
        var dialog = Create(owner, "转换媒体", "开始转换", panel);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var registration = CancelDialog(owner, dialog, cancellationToken);
        EmbyConversionOptions? options = null;
        var loading = true;
        var generation = 0;
        var closed = false;
        void Update()
        {
            var profile = profiles.SelectedItem as EmbyConversionProfile;
            profiles.Visibility = Show(options?.Options.Contains("Profile") == true && options.ProfileOptions.Count > 0);
            custom.Visibility = Show(profiles.Visibility == Visibility.Visible && profile?.Id == "custom");
            qualities.Visibility = Show(options?.Options.Contains("Quality") == true && options.QualityOptions.Count > 0
                && (profiles.Visibility == Visibility.Collapsed || profile?.EnableQualityOptions == true));
            bitrate.Visibility = Show(qualities.Visibility == Visibility.Visible && (qualities.SelectedItem as EmbyConversionQuality)?.Id == "custom");
            unwatched.Visibility = Show(options?.Options.Contains("UnwatchedOnly") == true);
            newContent.Visibility = Show(options?.Options.Contains("SyncNewContent") == true);
            limit.Visibility = Show(options?.Options.Contains("ItemLimit") == true);
            description.Text = profile?.Description ?? "";
            description.Visibility = Show(description.Text.Length > 0 && profiles.Visibility == Visibility.Visible);
            dialog.IsPrimaryButtonEnabled = !loading && targets.SelectedItem is EmbyConversionTarget
                && (profiles.Visibility == Visibility.Collapsed || profile is not null)
                && (qualities.Visibility == Visibility.Collapsed || qualities.SelectedItem is EmbyConversionQuality)
                && (bitrate.Visibility == Visibility.Collapsed || double.IsFinite(bitrate.Value) && bitrate.Value > 0)
                && (limit.Visibility == Visibility.Collapsed || double.IsNaN(limit.Value) || limit.Value >= 1 && limit.Value == Math.Floor(limit.Value));
        }
        async Task LoadTargetAsync()
        {
            var version = ++generation;
            if (targets.SelectedItem is not EmbyConversionTarget target) { options = null; Update(); return; }
            loading = true;
            status.Text = "正在读取转换选项…";
            Update();
            try
            {
                var loaded = await read(target.Id, lifetime.Token).ConfigureAwait(true);
                if (closed || lifetime.IsCancellationRequested || version != generation) return;
                options = loaded;
                profiles.ItemsSource = loaded.ProfileOptions;
                profiles.SelectedItem = loaded.ProfileOptions.FirstOrDefault(item => item.IsDefault == true) ?? loaded.ProfileOptions.FirstOrDefault();
                qualities.ItemsSource = loaded.QualityOptions;
                qualities.SelectedItem = loaded.QualityOptions.FirstOrDefault(item => item.IsDefault == true) ?? loaded.QualityOptions.FirstOrDefault();
                loading = false;
                status.Text = "";
                Update();
            }
            catch (OperationCanceledException)
            {
                if (closed || lifetime.IsCancellationRequested || version != generation) return;
                loading = false;
                status.Text = "转换选项读取已取消，请重新选择转换目标。";
                dialog.IsPrimaryButtonEnabled = false;
            }
            catch (Exception error)
            {
                if (closed || version != generation) return;
                status.Text = "读取转换选项失败：" + error.Message;
                dialog.IsPrimaryButtonEnabled = false;
            }
        }
        targets.SelectionChanged += async (_, _) => await LoadTargetAsync();
        profiles.SelectionChanged += (_, _) => Update();
        qualities.SelectionChanged += (_, _) => Update();
        bitrate.ValueChanged += (_, _) => Update();
        limit.ValueChanged += (_, _) => Update();
        dialog.Opened += (_, _) => targets.SelectedIndex = initial.Targets.Count == 0 ? -1 : 0;
        dialog.Closed += (_, _) => { closed = true; lifetime.Cancel(); };
        status.Text = initial.Targets.Count == 0 ? "服务器没有提供可用的转换目标。" : "";
        Update();
        if (cancellationToken.IsCancellationRequested || await dialog.ShowAsync() != ContentDialogResult.Primary
            || cancellationToken.IsCancellationRequested || loading || targets.SelectedItem is not EmbyConversionTarget chosen) return null;
        return new(chosen.Id,
            profiles.Visibility == Visibility.Visible ? (profiles.SelectedItem as EmbyConversionProfile)?.Id : null,
            qualities.Visibility == Visibility.Visible ? (qualities.SelectedItem as EmbyConversionQuality)?.Id : null,
            bitrate.Visibility == Visibility.Visible ? checked((int)(bitrate.Value * 1_000_000)) : null,
            custom.Visibility == Visibility.Visible ? container.SelectedItem as string : null,
            custom.Visibility == Visibility.Visible ? video.SelectedItem as string : null,
            custom.Visibility == Visibility.Visible ? audio.SelectedItem as string : null,
            unwatched.Visibility == Visibility.Visible ? unwatched.IsChecked == true : null,
            newContent.Visibility == Visibility.Visible ? newContent.IsChecked == true : null,
            limit.Visibility == Visibility.Visible && !double.IsNaN(limit.Value) ? checked((int)limit.Value) : null);
    }

    private static ContentDialog Create(FrameworkElement owner, string title, string primary, FrameworkElement content)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = owner.XamlRoot,
            Title = title,
            PrimaryButtonText = primary,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light,
            Style = (Style)Application.Current.Resources["EgLibraryDialogSurfaceStyle"],
            Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 480
            }
        };
        dialog.Resources["ContentDialogMaxWidth"] = 520d;
        dialog.Resources["ContentDialogTopOverlay"] = Application.Current.Resources["LayerFillColorDefaultBrush"];
        return dialog;
    }

    private static ComboBox Picker(string name, string header) => new()
    {
        Name = name,
        Header = header,
        DisplayMemberPath = "Name",
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static ComboBox Choices(string name, string header, params string[] choices) => new()
    {
        Name = name,
        Header = header,
        ItemsSource = choices,
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    internal static CancellationTokenRegistration CancelDialog(FrameworkElement owner, ContentDialog dialog, CancellationToken token)
    {
        var dispatcher = owner.DispatcherQueue;
        return token.Register(() =>
        {
            if (dispatcher.HasThreadAccess) dialog.Hide();
            else dispatcher.TryEnqueue(() => dialog.Hide());
        });
    }
}
