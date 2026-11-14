using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    /// <summary>
    /// 标题栏左上那一排的显隐（2026-11-11 用户令「左上角统一留主页、收藏（没有收藏时隐藏该按钮）、设置、
    /// 搜索、返回」）：主页与返回每一页都在，设置与搜索只在没有侧边栏的档里在这一排 —— 侧边栏模式里设置
    /// 让给栏的页脚，搜索 2026-11-13 起从栏里删除、只剩标题栏正中那颗框；收藏也让给栏里的「收藏」项，
    /// 同一页不出两个同样的入口。
    /// <para>
    /// 收藏的判据是**内容**而不是能力：有收藏才露面。纯 Plex 这类没有收藏能力的来源永远查不到收藏，因此
    /// 自然收起；退出登录时收藏模型会归零（<c>FavoritesViewModel.Clear</c>），也不会留下半截状态。收放之外
    /// 返回与前进在沉浸浏览态保留，走不动时禁用；启停归 <see cref="ApplyChrome"/>。
    /// </para>
    /// </summary>
    private void SyncTitleActions()
    {
        var favorites = _session?.IsSignedIn == true && Favorites.HasItems && !SidebarShown;
        var visibility = favorites ? Visibility.Visible : Visibility.Collapsed;
        if (FavoritesButton.Visibility != visibility) FavoritesButton.Visibility = visibility;

        // 前进仅在沉浸浏览态出现，主页也保留：从媒体库退回主页之后仍能补回刚才那一步。
        // 商店登录/播放时 SidebarShown 也可能为 false，不能用它代替显示模式。
        var forward = !_sidebarMode && !_sidebarPlaying && ContentHost.Visibility == Visibility.Visible;
        ForwardButton.Visibility = forward ? Visibility.Visible : Visibility.Collapsed;
        ForwardButton.Margin = new Thickness(0);
        BackButton.Visibility = ContentHost.Visibility == Visibility.Visible
            ? _sidebarMode ? BackVisibilityFor(_current) : Visibility.Visible
            : Visibility.Collapsed;

        // 用户指定还原v0.2.5（2ff1224）：沉浸主页/收藏/设置/搜索/返回/前进，42×32托盘、26路径。
        // 移动同一个原生控件，保持事件、自动化身份和默认Tab次序；商店保留返回/折叠布局。
        if (_sidebarMode)
        {
            MoveTitleAction(BackButton, 0);
            MoveTitleAction(ForwardButton, 1);
        }
        else
        {
            MoveTitleAction(ForwardButton, TitleActions.Children.Count - 1);
            MoveTitleAction(BackButton, TitleActions.Children.Count - 2);
        }
        foreach (var button in new[] { HomeButton, FavoritesButton, SettingsButton, SearchButton, BackButton, ForwardButton })
        {
            button.Width = _sidebarMode ? 40 : 42;
            button.Height = _sidebarMode ? 40 : 32;
            if (button.Content is not Viewbox box) continue;
            var size = !_sidebarMode ? 26 : button == BackButton || button == ForwardButton ? 20 : 28;
            box.Width = size;
            box.Height = size;
        }

        void MoveTitleAction(Button button, int target)
        {
            var index = TitleActions.Children.IndexOf(button);
            if (index != target) TitleActions.Children.Move((uint)index, (uint)target);
        }
    }
}
