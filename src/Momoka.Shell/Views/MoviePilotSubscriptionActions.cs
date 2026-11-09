using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

/// <summary>订阅封面的分享、活动与文件统计弹窗。业务调用及访问期由视图模型持有。</summary>
internal static class MoviePilotSubscriptionActions
{
    private static readonly HashSet<XamlRoot> OpenRoots = [];
    internal static Task ShareAsync(FrameworkElement owner, MoviePilotSubscriptionsViewModel host,
        MoviePilotSubscriptionCard card, MoviePilotService service) => ShowAsync(owner, host, card, service, "分享订阅");

    internal static Task ActivityAsync(FrameworkElement owner, MoviePilotSubscriptionsViewModel host,
        MoviePilotSubscriptionCard card, MoviePilotService service) => ShowAsync(owner, host, card, service, "订阅活动");

    internal static Task FilesAsync(FrameworkElement owner, MoviePilotSubscriptionsViewModel host,
        MoviePilotSubscriptionCard card, MoviePilotService service) => ShowAsync(owner, host, card, service, "文件统计");

    private static async Task ShowAsync(FrameworkElement owner, MoviePilotSubscriptionsViewModel host,
        MoviePilotSubscriptionCard card, MoviePilotService service, string title)
    {
        if (owner.XamlRoot is not { } root || !OpenRoots.Add(root)) return;
        try { await ShowCoreAsync(owner, host, card, service, title); }
        finally { OpenRoots.Remove(root); }
    }

    private static async Task ShowCoreAsync(FrameworkElement owner, MoviePilotSubscriptionsViewModel host,
        MoviePilotSubscriptionCard card, MoviePilotService service, string title)
    {
        if (owner.XamlRoot is not { } root || host.Working || !host.Owns(card) || !service.Available) return;
        using var vm = new MoviePilotSubscriptionActionsViewModel();
        vm.Attach(service, card.Subscription, () => host.Owns(card));
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Text(card.Title));
        var progress = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed };
        var notice = new InfoBar { IsClosable = true };
        panel.Children.Add(progress);
        panel.Children.Add(notice);
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var dialog = new ContentDialog
        {
            XamlRoot = root, RequestedTheme = owner.ActualTheme, Title = title,
            Content = scroll, CloseButtonText = "关闭", DefaultButton = ContentDialogButton.None
        };
        dialog.Resources["ContentDialogMaxWidth"] = 820d;
        AutomationProperties.SetAutomationId(dialog, "SubscriptionActionDialog");
        void Resize()
        {
            panel.Width = Math.Max(200, Math.Min(720, root.Size.Width - 100));
            scroll.MaxHeight = Math.Max(120, root.Size.Height - 230);
        }
        void SizeChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Resize();
        root.Changed += SizeChanged;
        Resize();
        Action sync = () => { };
        vm.PropertyChanged += (_, _) =>
        {
            progress.Visibility = vm.Busy ? Visibility.Visible : Visibility.Collapsed;
            notice.IsOpen = vm.NoticeOpen;
            notice.Visibility = vm.NoticeOpen ? Visibility.Visible : Visibility.Collapsed;
            notice.Title = vm.NoticeTitle;
            notice.Message = vm.NoticeMessage;
            notice.Severity = vm.NoticeSeverity;
            sync();
        };
        notice.Visibility = Visibility.Collapsed;
        if (title == "分享订阅")
        {
            panel.Children.Add(Text("将订阅规则公开分享到 MoviePilot 社区。"));
            var shareTitle = new TextBox { Header = "标题", IsReadOnly = true,
                Text = card.Subscription.Media.Title + (card.Subscription.Media.Season is { } season ? $" S{season:00}" : "") };
            var comment = new TextBox { Header = "分享说明", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90 };
            var user = new TextBox { Header = "分享人" };
            Id(shareTitle, "SubscriptionShareTitle"); Id(comment, "SubscriptionShareComment"); Id(user, "SubscriptionShareUser");
            panel.Children.Add(shareTitle); panel.Children.Add(comment); panel.Children.Add(user);
            dialog.PrimaryButtonText = "分享";
            sync = () =>
            {
                dialog.IsPrimaryButtonEnabled = !vm.Busy && !string.IsNullOrWhiteSpace(comment.Text) && !string.IsNullOrWhiteSpace(user.Text);
                comment.IsEnabled = user.IsEnabled = !vm.Busy;
            };
            comment.TextChanged += (_, _) => sync(); user.TextChanged += (_, _) => sync(); sync();
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                args.Cancel = true;
                if (vm.Busy) return;
                var deferral = args.GetDeferral();
                try { args.Cancel = !await vm.ShareAsync(comment.Text, user.Text); }
                finally { deferral.Complete(); }
            };
            dialog.Closing += (_, args) => { if (vm.Busy) args.Cancel = true; };
        }
        else if (title == "订阅活动")
        {
            var filters = new WrapRow { Spacing = 12 };
            var days = new ComboBox { Header = "时间范围", ItemsSource = new[] { "最近 7 天", "最近 30 天", "全部" }, SelectedIndex = 0, MinWidth = 140 };
            var kind = new ComboBox { Header = "活动类型", ItemsSource = new[] { "全部", "创建", "更新", "状态变更", "完成", "刷新", "搜索" }, SelectedIndex = 0, MinWidth = 140 };
            Id(days, "SubscriptionActivityDays"); Id(kind, "SubscriptionActivityKind");
            var retry = Button("刷新", "SubscriptionActivityRefresh");
            filters.Children.Add(days); filters.Children.Add(kind); filters.Children.Add(retry);
            panel.Children.Add(filters);
            var rows = new StackPanel { Spacing = 8 };
            var empty = Text("暂无订阅活动。", "SubscriptionActivityEmpty");
            var more = Button("加载更多", "SubscriptionActivityMore");
            panel.Children.Add(empty); panel.Children.Add(rows); panel.Children.Add(more);
            var kinds = new string?[] { null, "created", "updated", "status", "completed", "refresh", "search" };
            var detailButtons = new List<Button>();
            async Task Read(bool next)
            {
                if (vm.Busy) return;
                await vm.LoadActivityAsync(days.SelectedIndex switch { 0 => 7, 1 => 30, _ => null }, kinds[Math.Max(0, kind.SelectedIndex)], next);
                if (!next) { rows.Children.Clear(); detailButtons.Clear(); }
                foreach (var activity in vm.Activities.Skip(rows.Children.Count))
                {
                    var content = new StackPanel { Spacing = 8 };
                    content.Children.Add(Text(activity.Summary));
                    var details = Text("");
                    var load = Button("查看详情", "SubscriptionActivityDetails");
                    load.Click += async (_, _) =>
                    {
                        var value = await vm.LoadDetailsAsync(activity);
                        if (value is null) return;
                        details.Text = string.IsNullOrWhiteSpace(value) ? "暂无详细记录。" : value;
                        load.Visibility = Visibility.Collapsed;
                    };
                    detailButtons.Add(load); content.Children.Add(load); content.Children.Add(details);
                    var row = new Expander { Header = activity.Title + " · " + activity.StartedAt?.ToLocalTime().ToString("MM-dd HH:mm"),
                        Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                    Id(row, "SubscriptionActivityRow"); rows.Children.Add(row);
                }
                sync();
            }
            sync = () =>
            {
                days.IsEnabled = kind.IsEnabled = retry.IsEnabled = more.IsEnabled = !vm.Busy;
                foreach (var button in detailButtons) button.IsEnabled = !vm.Busy;
                more.Visibility = vm.HasMore ? Visibility.Visible : Visibility.Collapsed;
                empty.Visibility = vm.IsReady && !vm.Busy && !vm.NoticeOpen && vm.Activities.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            days.SelectionChanged += async (_, _) => await Read(false);
            kind.SelectionChanged += async (_, _) => await Read(false);
            retry.Click += async (_, _) => await Read(false);
            more.Click += async (_, _) => await Read(true);
            dialog.Opened += async (_, _) => await Read(false);
            sync();
        }
        else
        {
            var retry = Button("刷新", "SubscriptionFilesRefresh");
            var rows = new StackPanel { Spacing = 8 };
            panel.Children.Add(retry); panel.Children.Add(rows);
            async Task Read()
            {
                var files = await vm.LoadFilesAsync();
                if (files is null) return;
                rows.Children.Clear(); rows.Children.Add(Text(files.Summary));
                foreach (var episode in files.Episodes)
                {
                    var row = new Expander { Header = $"{episode.Label} · {episode.Status}", HorizontalAlignment = HorizontalAlignment.Stretch,
                        Content = Text(string.IsNullOrEmpty(episode.Files) ? "暂无入库或下载文件。" : episode.Files) };
                    Id(row, "SubscriptionFileRow"); rows.Children.Add(row);
                }
            }
            sync = () => retry.IsEnabled = !vm.Busy;
            retry.Click += async (_, _) => await Read();
            dialog.Opened += async (_, _) => await Read();
        }
        void Invalidate() { vm.Cancel(); dialog.Hide(); }
        void Unloaded(object sender, RoutedEventArgs args) => Invalidate();
        void AvailabilityChanged(object? sender, EventArgs args)
        {
            if (!service.Available || !service.IsCurrentSubscription(card.Subscription))
                owner.DispatcherQueue.TryEnqueue(Invalidate);
        }
        owner.Unloaded += Unloaded;
        service.AvailabilityChanged += AvailabilityChanged;
        try { await MoviePilotDialog.ShowAsync(dialog, service); }
        finally
        {
            service.AvailabilityChanged -= AvailabilityChanged;
            owner.Unloaded -= Unloaded;
            root.Changed -= SizeChanged;
            vm.Cancel();
        }
    }

    private static void Id(DependencyObject element, string id) => AutomationProperties.SetAutomationId(element, id);
    private static TextBlock Text(string text, string? id = null)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
            Style = (Style)Application.Current.Resources["EgBodyStyle"] };
        if (id is not null) Id(block, id);
        return block;
    }
    private static Button Button(string text, string id)
    {
        var button = new Button { Content = text, Style = (Style)Application.Current.Resources["EgSecondaryButtonStyle"] };
        Id(button, id);
        return button;
    }
}
