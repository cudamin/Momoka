using Microsoft.UI.Xaml;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    /// <summary>
    /// 标题栏左上那一排的显隐（2026-11-11 用户令「左上角统一留主页、收藏（没有收藏时隐藏该按钮）、设置、
    /// 搜索、返回」）：主页与返回每一页都在，设置与搜索只在没有侧边栏的档里在这一排 —— 侧边栏模式里那两颗
    /// 让给栏里的入口，收藏也让给栏里的「收藏」项，同一页不出两个同样的入口。
    /// <para>
    /// 收藏的判据是**内容**而不是能力：有收藏才露面。纯 Plex 这类没有收藏能力的来源永远查不到收藏，因此
    /// 自然收起；退出登录时收藏模型会归零（<c>FavoritesViewModel.Clear</c>），也不会留下半截状态。收放之外
    /// 不动别的 —— 返回走不动时只是变暗、不收起，那一档归 <see cref="ApplyChrome"/>。
    /// </para>
    /// </summary>
    private void SyncTitleActions()
    {
        var favorites = _session?.IsSignedIn == true && Favorites.HasItems && !SidebarShown;
        var visibility = favorites ? Visibility.Visible : Visibility.Collapsed;
        if (FavoritesButton.Visibility != visibility) FavoritesButton.Visibility = visibility;
    }
}
