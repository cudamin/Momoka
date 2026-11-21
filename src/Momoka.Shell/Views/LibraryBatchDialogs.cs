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
