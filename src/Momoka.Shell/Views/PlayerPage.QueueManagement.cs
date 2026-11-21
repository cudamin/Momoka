using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Playback;

namespace Momoka.Shell.Views;

public sealed partial class PlayerPage
{
    internal async Task ShowQueueManagementAsync(XamlRoot root)
    {
        if (_queueDialogOpen || !ViewModel.HasPendingQueue || ViewModel.CaptureQueueArrangement() is not { } arrangement) return;
        var list = new ListView
        {
            Name = "VideoQueuePendingItems",
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 300,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        var up = new Button { Name = "VideoQueueMoveUp", Content = "上移" };
        var down = new Button { Name = "VideoQueueMoveDown", Content = "下移" };
        var remove = new Button { Name = "VideoQueueRemove", Content = "移除" };
        var spacing = (double)Application.Current.Resources["EgSpaceM"];
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(up);
        actions.Children.Add(down);
        actions.Children.Add(remove);
        var body = (Style)Application.Current.Resources["EgBodyStyle"];
        var caption = (Style)Application.Current.Resources["EgCaptionStyle"];
        var empty = new TextBlock { Text = "没有待播项", Style = body };
        var panel = new StackPanel { Spacing = spacing };
        panel.Children.Add(list);
        panel.Children.Add(empty);
        panel.Children.Add(actions);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "管理待播项",
            CloseButtonText = "完成",
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["EgLibraryDialogSurfaceStyle"],
            Content = panel,
            RequestedTheme = ActualTheme
        };
        dialog.Resources["ContentDialogTopOverlay"] = Application.Current.Resources["LayerFillColorDefaultBrush"];
        void UpdateActions()
        {
            var index = list.SelectedIndex;
            up.IsEnabled = index > 0;
            down.IsEnabled = index >= 0 && index < list.Items.Count - 1;
            remove.IsEnabled = index >= 0;
        }

        void RefreshRows()
        {
            var index = list.SelectedIndex;
            long? slot = list.SelectedItem is ListViewItem { Tag: SessionPlaybackQueue.Entry selected } ? selected.Slot : null;
            var pending = ViewModel.PendingQueue;
            list.Items.Clear();
            for (var i = 0; i < pending.Count; i++)
            {
                var entry = pending[i];
                var row = new StackPanel { Spacing = 2 };
                row.Children.Add(new TextBlock
                {
                    Text = $"{i + 1}. {entry.Item.ToPlaybackHeadline()}",
                    TextWrapping = TextWrapping.Wrap,
                    Style = body
                });
                row.Children.Add(new TextBlock
                {
                    Text = entry.Scope.DisplayName,
                    TextWrapping = TextWrapping.Wrap,
                    Style = caption
                });
                list.Items.Add(new ListViewItem { Tag = entry, Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch });
                if (entry.Slot == slot) index = i;
            }
            // Keep the edited slot selected across moves and choose a nearby item after removal.
            if (pending.Count > 0) list.SelectedIndex = Math.Clamp(index, 0, pending.Count - 1);
            if (list.SelectedItem is ListViewItem target) list.ScrollIntoView(target);
            list.Visibility = actions.Visibility = pending.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            empty.Visibility = pending.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateActions();
        }

        void UpdateQueue(object? sender, EventArgs args)
        {
            if (ViewModel.IsCurrentQueueArrangement(arrangement)) RefreshRows();
            else dialog.Hide();
        }

        void EditQueue(int offset)
        {
            if (list.SelectedItem is not ListViewItem { Tag: SessionPlaybackQueue.Entry selected }) return;
            var changed = offset == 0 ? ViewModel.RemovePendingQueueItem(arrangement, selected.Slot)
                : ViewModel.MovePendingQueueItem(arrangement, selected.Slot, offset);
            if (!changed) UpdateQueue(null, EventArgs.Empty);
        }

        list.SelectionChanged += (_, _) => UpdateActions();
        up.Click += (_, _) => EditQueue(-1);
        down.Click += (_, _) => EditQueue(1);
        remove.Click += (_, _) => EditQueue(0);
        void PlaybackHidden() => dialog.Hide();
        using var cancel = LibraryBatchDialogs.CancelDialog(this, dialog, arrangement.Scope.Lifetime);
        RefreshRows();
        var previousInputSuspended = _inputSuspended;
        var presentation = _presentationGeneration;
        _queueDialogOpen = _inputSuspended = true;
        ViewModel.QueueChanged += UpdateQueue;
        ViewModel.PlayerHidden += PlaybackHidden;
        try { await dialog.ShowAsync(); }
        finally
        {
            ViewModel.QueueChanged -= UpdateQueue;
            ViewModel.PlayerHidden -= PlaybackHidden;
            _queueDialogOpen = false;
            if (presentation == _presentationGeneration) _inputSuspended = previousInputSuspended;
        }
    }
}
