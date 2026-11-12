using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Services;

namespace Momoka.Shell.Views;

internal sealed record ServerListDraft(string Name, IReadOnlyList<EmbyItem> Items);

/// <summary>Native search/selection dialog; server requests remain in the list service.</summary>
internal static class ServerListDialogs
{
    internal static async Task<ServerListDraft?> EditAsync(FrameworkElement owner, ServerListsService service,
        EmbySessionScope scope, ServerListKind kind, EmbyItem? library, bool create, CancellationToken token)
    {
        var name = new TextBox { Name = "ServerListName", Header = "清单名称", MaxLength = 200 };
        var term = new TextBox { Name = "ServerListSearchTerm", Header = "查找媒体", PlaceholderText = "输入片名" };
        var search = new Button { Name = "ServerListSearch", Content = "搜索" };
        var matches = new ObservableCollection<EmbyItem>();
        var results = new ListView
        {
            Name = "ServerListSearchResults",
            ItemsSource = matches,
            DisplayMemberPath = "Name",
            SelectionMode = ListViewSelectionMode.Multiple,
            MaxHeight = 240,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var more = new Button { Name = "ServerListSearchMore", Content = "加载更多", Visibility = Visibility.Collapsed };
        var notice = new InfoBar { Name = "ServerListSearchNotice", IsOpen = false, Visibility = Visibility.Collapsed };
        var panel = new StackPanel { Spacing = (double)Application.Current.Resources["EgSpaceM"] };
        if (create) panel.Children.Add(name);
        panel.Children.Add(term);
        panel.Children.Add(search);
        panel.Children.Add(notice);
        panel.Children.Add(results);
        panel.Children.Add(more);
        var dialog = new ContentDialog
        {
            XamlRoot = owner.XamlRoot,
            Title = create ? "新建清单" : "添加到清单",
            PrimaryButtonText = create ? "创建" : "添加",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["EgLibraryDialogSurfaceStyle"],
            Content = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 480
            },
            RequestedTheme = owner.ActualTheme
        };
        dialog.Resources["ContentDialogTopOverlay"] = Application.Current.Resources["LayerFillColorDefaultBrush"];
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var closed = false;
        var reading = false;
        var consumed = 0;
        var query = "";
        var total = 0;
        ServerListDraft? draft = null;
        void Validate() => dialog.IsPrimaryButtonEnabled = !reading && (!create || !string.IsNullOrWhiteSpace(name.Text))
            && (results.SelectedItems.Count > 0 || create && !scope.Connection.IsPlex);
        async Task ReadAsync(bool append)
        {
            if (reading || closed || lifetime.IsCancellationRequested || string.IsNullOrWhiteSpace(term.Text)) return;
            if (!append) { query = term.Text.Trim(); consumed = 0; total = 0; matches.Clear(); more.Visibility = Visibility.Collapsed; }
            reading = true;
            term.IsEnabled = false;
            search.IsEnabled = false;
            more.IsEnabled = false;
            Validate();
            try
            {
                var page = await service.SearchAsync(scope, kind, library, query, consumed, 50, lifetime.Token);
                if (closed || lifetime.IsCancellationRequested) return;
                if (page.Consumed == 0 && consumed < page.Total) throw new InvalidDataException("服务器未返回剩余搜索结果，请重试加载");
                foreach (var item in page.Items) if (!matches.Any(known => known.Id == item.Id)) matches.Add(item);
                consumed += page.Consumed;
                total = page.Total;
                more.Visibility = consumed < total && page.Consumed > 0 ? Visibility.Visible : Visibility.Collapsed;
                notice.IsOpen = false;
                notice.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (closed) return;
                notice.Severity = InfoBarSeverity.Error;
                notice.Message = error.Message;
                notice.Visibility = Visibility.Visible;
                notice.IsOpen = true;
            }
            finally
            {
                reading = false;
                if (!closed) { term.IsEnabled = true; search.IsEnabled = true; more.IsEnabled = true; Validate(); }
            }
        }
        search.Click += async (_, _) => await ReadAsync(false);
        more.Click += async (_, _) => await ReadAsync(true);
        name.TextChanged += (_, _) => Validate();
        results.SelectionChanged += (_, _) => Validate();
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!dialog.IsPrimaryButtonEnabled) { args.Cancel = true; return; }
            draft = new(name.Text.Trim(), results.SelectedItems.Cast<EmbyItem>().ToArray());
        };
        dialog.Closed += (_, _) => { closed = true; lifetime.Cancel(); };
        Validate();
        using var cancel = LibraryBatchDialogs.CancelDialog(owner, dialog, token);
        if (token.IsCancellationRequested || await dialog.ShowAsync() != ContentDialogResult.Primary || token.IsCancellationRequested) return null;
        return draft;
    }
}
