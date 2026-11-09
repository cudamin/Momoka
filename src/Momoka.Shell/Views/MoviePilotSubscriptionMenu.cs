using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

internal static class MoviePilotSubscriptionMenu
{
    /// <summary>
    /// 主页／订阅列表封面上那颗更多键的菜单：一份自己的浮层，外加「MoviePilot 掉线就把浮层收掉」那一条。
    /// </summary>
    internal static MenuFlyout Create(FrameworkElement owner, MoviePilotSubscriptionsViewModel vm,
        MoviePilotSubscriptionCard card, Action open, MoviePilotService? service = null)
    {
        var menu = new MenuFlyout();
        Watch(menu, service);
        Fill(menu.Items, owner, vm, card, open, service);
        return menu;
    }

    /// <summary>
    /// 掉线时把这层浮层收掉。<see cref="Fill"/> 装出来的项自己不知道宿主是谁，所以这一条由建浮层的人接。
    /// </summary>
    internal static void Watch(MenuFlyout menu, MoviePilotService? service)
    {
        if (service is null) return;
        var dispatcher = menu.DispatcherQueue;
        void AvailabilityChanged(object? sender, EventArgs args)
        {
            if (!service.Available) dispatcher.TryEnqueue(() => menu.Hide());
        }
        menu.Opened += (_, _) =>
        {
            service.AvailabilityChanged += AvailabilityChanged;
            AvailabilityChanged(null, EventArgs.Empty);
        };
        menu.Closed += (_, _) => service.AvailabilityChanged -= AvailabilityChanged;
    }

    /// <summary>
    /// 按官方订阅卡的动作顺序构建封面菜单。详情导航与文件统计各有落点。
    /// </summary>
    internal static void Fill(IList<MenuFlyoutItemBase> items, FrameworkElement owner, MoviePilotSubscriptionsViewModel vm,
        MoviePilotSubscriptionCard card, Action open, MoviePilotService? service)
    {
        var needsReview = vm.NeedsReview(card);
        var pauseAction = card.Subscription.Paused ? MoviePilotSubscriptionAction.Resume : MoviePilotSubscriptionAction.Pause;
        if (needsReview) Add("核对后继续", "\uE946", () => _ = vm.ReviewAsync(card), false);
        Add("编辑", "\uE70F", () => _ = vm.EditAsync(card,
            (subscription, save, remove) => MoviePilotSubscriptionEditor.ShowAsync(owner, subscription, service, save, remove)));
        Add("搜索", "\uE721", () => _ = vm.ManageAsync(card, MoviePilotSubscriptionAction.Search));
        Add(card.Subscription.Paused ? "恢复" : "暂停", card.Subscription.Paused ? "\uE768" : "\uE769",
            () => _ = vm.ManageAsync(card, pauseAction), color: "EgInfoBrush");
        Add("重置", "\uE777", () => _ = vm.ManageAsync(card, MoviePilotSubscriptionAction.Reset), color: "EgWarningBrush");
        if (service is not null)
        {
            if (card.Subscription.IsSeries)
                Add("分享", "\uE72D", () => _ = MoviePilotSubscriptionActions.ShareAsync(owner, vm, card, service), color: "EgAccentBrush");
            Add("订阅活动", "\uE9D9", () => _ = MoviePilotSubscriptionActions.ActivityAsync(owner, vm, card, service), false);
        }
        Add("媒体详情", "\uE946", open, false);
        if (service is not null) Add("文件统计", "\uE9F9", () => _ = MoviePilotSubscriptionActions.FilesAsync(owner, vm, card, service), false);
        Add("取消订阅", "\uE74D", () => _ = vm.ManageAsync(card, MoviePilotSubscriptionAction.Delete), color: "EgDangerBrush");
        return;

        void Add(string title, string glyph, Action action, bool write = true, string? color = null)
        {
            var item = new MenuFlyoutItem { Text = title, Icon = new FontIcon { Glyph = glyph }, IsEnabled = vm.Owns(card) && !vm.Working && (!write || vm.CanManage && !needsReview) };
            AutomationProperties.SetAutomationId(item, "Subscription" + title);
            if (color is not null) item.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[color];
            item.Click += (_, _) => action();
            items.Add(item);
        }
    }
}
