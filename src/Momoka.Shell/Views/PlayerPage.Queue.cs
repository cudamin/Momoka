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
        var viewModel = ViewModel;
        if (_queueDialogOpen || viewModel.CaptureQueueArrangement() is not { } arrangement) return;
        _queueDialogOpen = true;
        var sourceBox = new ComboBox
        {
            Name = "VideoQueueSearchSource",
            Header = "搜索来源",
            DisplayMemberPath = nameof(EmbySessionScope.DisplayName),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
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
        panel.Children.Add(sourceBox);
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
        var refreshingSources = false;
        EmbySessionScope? selectedSource = null;
        CancellationTokenSource? activeSearch = null;
        var consumed = 0;
        var query = "";
        (EmbySessionScope Source, IReadOnlyList<EmbyItem> Items)? chosen = null;
        void Validate()
        {
            var ready = !closed && activeSearch is null && selectedSource?.IsAvailable == true
                && viewModel.IsCurrentQueueArrangement(arrangement);
            term.IsEnabled = ready;
            search.IsEnabled = ready;
            more.IsEnabled = ready;
            dialog.IsPrimaryButtonEnabled = ready && results.SelectedItems.Count > 0;
        }
        void ResetResults()
        {
            consumed = 0;
            matches.Clear();
            more.Visibility = Visibility.Collapsed;
            notice.IsOpen = false;
            notice.Visibility = Visibility.Collapsed;
        }
        void ChooseSource(EmbySessionScope? source)
        {
            var changed = !ReferenceEquals(selectedSource?.UserStateOwner, source?.UserStateOwner);
            selectedSource = source;
            if (changed)
            {
                activeSearch?.Cancel();
                activeSearch = null;
                query = "";
                ResetResults();
            }
            Validate();
        }
        void RefreshSources()
        {
            var sources = viewModel.GetQueueSearchSources(arrangement);
            var source = sources.FirstOrDefault(candidate => ReferenceEquals(candidate.UserStateOwner, selectedSource?.UserStateOwner))
                ?? sources.FirstOrDefault();
            refreshingSources = true;
            sourceBox.ItemsSource = sources;
            sourceBox.SelectedItem = source;
            sourceBox.Visibility = sources.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            refreshingSources = false;
            ChooseSource(source);
        }
        void QueueChanged(object? sender, EventArgs args)
        {
            if (closed) return;
            if (!viewModel.IsCurrentQueueArrangement(arrangement)) dialog.Hide();
            else RefreshSources();
        }
        async Task ReadAsync(bool append)
        {
            if (activeSearch is not null || closed || selectedSource is not { IsAvailable: true } source
                || !viewModel.IsCurrentQueueArrangement(arrangement)) return;
            if (!append)
            {
                query = term.Text.Trim();
                if (query.Length == 0) return;
                ResetResults();
            }
            using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, source.Lifetime);
            activeSearch = request;
            Validate();
            bool IsCurrentSearch() => !closed && !request.IsCancellationRequested && ReferenceEquals(activeSearch, request)
                && ReferenceEquals(selectedSource?.UserStateOwner, source.UserStateOwner)
                && viewModel.IsCurrentQueueArrangement(arrangement);
            try
            {
                var page = await viewModel.SearchQueueAsync(arrangement, query, consumed, request.Token, source);
                if (!IsCurrentSearch()) return;
                if (page.Items.Count == 0 && consumed < page.TotalRecordCount) throw new InvalidDataException("服务器未返回剩余搜索结果，请重新搜索");
                foreach (var item in page.Items)
                    if (viewModel.CanQueue(item) && !matches.Any(known => known.IdentityKey == item.IdentityKey)) matches.Add(item);
                consumed += page.Items.Count;
                more.Visibility = consumed < page.TotalRecordCount && page.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                notice.IsOpen = false; notice.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (IsCurrentSearch()) { notice.Message = error.Message; notice.Severity = InfoBarSeverity.Error; notice.Visibility = Visibility.Visible; notice.IsOpen = true; }
            }
            finally
            {
                if (ReferenceEquals(activeSearch, request))
                {
                    activeSearch = null;
                    if (!closed) Validate();
                }
            }
        }
        sourceBox.SelectionChanged += (_, _) =>
        {
            if (!closed && !refreshingSources) ChooseSource(sourceBox.SelectedItem as EmbySessionScope);
        };
        search.Click += async (_, _) => await ReadAsync(false);
        more.Click += async (_, _) => await ReadAsync(true);
        results.SelectionChanged += (_, _) => Validate();
        dialog.PrimaryButtonClick += (_, args) =>
        {
            Validate();
            if (!dialog.IsPrimaryButtonEnabled || selectedSource is not { } source) { args.Cancel = true; return; }
            // Screen/result order is stable even if the user selected the rows in a different order.
            chosen = (source, matches.Where(item => results.SelectedItems.Contains(item)).ToArray());
        };
        dialog.Closed += (_, _) => { closed = true; lifetime.Cancel(); };
        viewModel.QueueChanged += QueueChanged;
        viewModel.PlayerHidden += PlaybackHidden;
        void PlaybackHidden() => dialog.Hide();
        RefreshSources();
        using var cancel = LibraryBatchDialogs.CancelDialog(this, dialog, lifetime.Token);
        var previousInputSuspended = _inputSuspended;
        var presentation = _presentationGeneration;
        _inputSuspended = true;
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && chosen is { } choice
                && viewModel.IsCurrentQueueArrangement(arrangement))
                viewModel.ArrangeQueue(arrangement, choice.Items, next, choice.Source);
        }
        finally
        {
            viewModel.QueueChanged -= QueueChanged;
            viewModel.PlayerHidden -= PlaybackHidden;
            _queueDialogOpen = false;
            if (presentation == _presentationGeneration) _inputSuspended = previousInputSuspended;
        }
    }
}
