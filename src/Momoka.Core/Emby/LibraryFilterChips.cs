namespace Momoka.Emby;

/// <summary>
/// 工具栏下方那一行「正在生效的筛选」归谁。Emby 的筛选面板整块盖住页面，收起后只剩这一行能说明
/// 「这一屏为什么少了东西」；飞牛的面板在工具栏下面展开，选中的值在面板里亮着，收起后按钮上的数字
/// 也还在（用户 2026-10-19 令「点击选项后不要在界面添加标签」），所以不铺这一行。
/// </summary>
public static class LibraryFilterChips
{
    public static bool ShowsActiveChips(bool fnNas) => !fnNas;
}
