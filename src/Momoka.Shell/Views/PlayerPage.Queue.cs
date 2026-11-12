using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class PlayerPage
{
    private bool _queueDialogOpen;

    /// <summary>Both pipelines request the same native picker; reading media never writes a server list.</summary>
    internal async Task ShowQueueDialogAsync(XamlRoot root, bool next)
    {
        if (_queueDialogOpen || ViewModel.CaptureQueueArrangement() is not { } arrangement) return;
        _queueDialogOpen = true;
        var term = new TextBox { Name = "VideoQueueSearchTerm", Header = "查找媒体", PlaceholderText = "输入片名" };
        var search = new Button { Name = "VideoQueueSearch", Content = "搜索" };
        var matches = new ObservableCollection<EmbyItem>();
        var results = new ListView
        {
            Name = "VideoQueueSearchResults",
            ItemsSource = matches,
            DisplayMemberPath = "Name",
            SelectionMode = ListViewSelectionMode.Multiple,
            MaxHeight = 300,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        var more = new Button { Name = "VideoQueueSearchMore", Content = "加载更多", Visibility = Visibility.Collapsed };
        var notice = new InfoBar { Name = "VideoQueueSearchNotice", IsOpen = false, Visibility = Visibility.Collapsed };
        var panel = new StackPanel { Spacing = (double)Application.Current.Resources["EgSpaceM"] };
        panel.Children.Add(term); panel.Children.Add(search); panel.Children.Add(notice); panel.Children.Add(results); panel.Children.Add(more);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = next ? "下一项播放" : "追加到本次队尾",
            PrimaryButtonText = "加入本次队列",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["EgLibraryDialogSurfaceStyle"],
            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 480,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            },
            RequestedTheme = ActualTheme
        };
        dialog.Resources["ContentDialogTopOverlay"] = Application.Current.Resources["LayerFillColorDefaultBrush"];
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(arrangement.Scope.Lifetime);
        var closed = false;
        var reading = false;
        var consumed = 0;
        var total = 0;
        var query = "";
        IReadOnlyList<EmbyItem>? chosen = null;
        void Validate() => dialog.IsPrimaryButtonEnabled = !reading && results.SelectedItems.Count > 0
            && ViewModel.IsCurrentQueueArrangement(arrangement);
        void QueueChanged(object? sender, EventArgs args)
        {
            if (closed) return;
            if (!ViewModel.IsCurrentQueueArrangement(arrangement)) dialog.Hide();
            else Validate();
        }
        async Task ReadAsync(bool append)
        {
            if (reading || closed || !ViewModel.IsCurrentQueueArrangement(arrangement) || string.IsNullOrWhiteSpace(term.Text)) return;
            if (!append) { query = term.Text.Trim(); consumed = 0; total = 0; matches.Clear(); more.Visibility = Visibility.Collapsed; }
            reading = true; term.IsEnabled = false; search.IsEnabled = false; more.IsEnabled = false; Validate();
            try
            {
                var page = await ViewModel.SearchQueueAsync(arrangement, query, consumed, lifetime.Token);
                if (closed || lifetime.IsCancellationRequested || !ViewModel.IsCurrentQueueArrangement(arrangement)) return;
                if (page.Items.Count == 0 && consumed < page.TotalRecordCount) throw new InvalidDataException("服务器未返回剩余搜索结果，请重新搜索");
                foreach (var item in page.Items)
                    if (ViewModel.CanQueue(item) && !matches.Any(known => known.IdentityKey == item.IdentityKey)) matches.Add(item);
                consumed += page.Items.Count;
                total = page.TotalRecordCount;
                more.Visibility = consumed < total && page.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                notice.IsOpen = false; notice.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!closed) { notice.Message = error.Message; notice.Severity = InfoBarSeverity.Error; notice.Visibility = Visibility.Visible; notice.IsOpen = true; }
            }
            finally
            {
                reading = false;
                if (!closed) { term.IsEnabled = true; search.IsEnabled = true; more.IsEnabled = true; Validate(); }
            }
        }
        search.Click += async (_, _) => await ReadAsync(false);
        more.Click += async (_, _) => await ReadAsync(true);
        results.SelectionChanged += (_, _) => Validate();
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!dialog.IsPrimaryButtonEnabled) { args.Cancel = true; return; }
            // Screen/result order is stable even if the user selected the rows in a different order.
            chosen = matches.Where(item => results.SelectedItems.Contains(item)).ToArray();
        };
        dialog.Closed += (_, _) => { closed = true; lifetime.Cancel(); };
        ViewModel.QueueChanged += QueueChanged;
        ViewModel.PlayerHidden += PlaybackHidden;
        void PlaybackHidden() => dialog.Hide();
        Validate();
        using var cancel = LibraryBatchDialogs.CancelDialog(this, dialog, lifetime.Token);
        var previousInputSuspended = _inputSuspended;
        var presentation = _presentationGeneration;
        _inputSuspended = true;
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && chosen is not null
                && ViewModel.IsCurrentQueueArrangement(arrangement))
                ViewModel.ArrangeQueue(arrangement, chosen, next);
        }
        finally
        {
            ViewModel.QueueChanged -= QueueChanged;
            ViewModel.PlayerHidden -= PlaybackHidden;
            _queueDialogOpen = false;
            if (presentation == _presentationGeneration) _inputSuspended = previousInputSuspended;
        }
    }
}
