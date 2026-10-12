using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

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
    /// 把订阅管理那一组项填进给定的项集合。分成「建浮层」和「装项」两半，是为了让调用方自己拿主意浮层怎么来
    /// （封面那颗更多键走 <see cref="Create"/>，可用性探针直接把项读出来）。
    /// <para>
    /// 这一份是**封面更多键**的项集合，落点就在封面所在的那一页（「媒体详情」在这里是打开订阅详情页）。订阅
    /// 详情页那颗更多键原先也装这一份，用户令 2026-10-12「更多按钮不要折叠，去掉重复的选项」之后不再共用：
    /// 那边摊平成一层、重复的项去掉，「媒体详情」由落点更准的「文件统计」替掉，见
    /// <c>MoviePilotSubscriptionPage.BuildMoreMenu</c>。
    /// </para>
    /// </summary>
    internal static void Fill(IList<MenuFlyoutItemBase> items, FrameworkElement owner, MoviePilotSubscriptionsViewModel vm,
        MoviePilotSubscriptionCard card, Action open, MoviePilotService? service)
    {
        var needsReview = vm.NeedsReview(card);
        var pauseAction = card.Subscription.Paused ? MoviePilotSubscriptionAction.Resume : MoviePilotSubscriptionAction.Pause;
        if (needsReview) Add("核对后继续", "\uE946", () => _ = vm.ReviewAsync(card), false);
        Add("编辑", "\uE70F", () => _ = vm.EditAsync(card, subscription => MoviePilotSubscriptionEditor.ShowAsync(owner, subscription, service)));
        Add("搜索", "\uE721", () => _ = vm.ManageAsync(card, MoviePilotSubscriptionAction.Search));
        Add(card.Subscription.Paused ? "恢复" : "暂停", card.Subscription.Paused ? "\uE768" : "\uE769",
            () => _ = vm.ManageAsync(card, pauseAction));
        Add("重置", "\uE777", () => _ = vm.ManageAsync(card, MoviePilotSubscriptionAction.Reset));
        Add("媒体详情", "\uE946", open, false);
        // 「文件统计」只在电视剧上有落点：它滚到订阅季折叠栏里那份统计。电影那块独立面板已按用户令
        // （2026-10-12）删掉，这一项在电影上无处可去，跟着一起收起（不是置灰）。
        if (card.Subscription.IsSeries) Add("文件统计", "\uE9F9", open, false);
        items.Add(new MenuFlyoutSeparator());
        Add("取消订阅", "\uE74D", () => _ = vm.ManageAsync(card, MoviePilotSubscriptionAction.Delete));
        return;

        void Add(string title, string glyph, Action action, bool write = true)
        {
            var item = new MenuFlyoutItem { Text = title, Icon = new FontIcon { Glyph = glyph }, IsEnabled = vm.Owns(card) && (!write || vm.CanManage && !needsReview) };
            AutomationProperties.SetAutomationId(item, "Subscription" + title);
            item.Click += (_, _) => action();
            items.Add(item);
        }
    }
}
