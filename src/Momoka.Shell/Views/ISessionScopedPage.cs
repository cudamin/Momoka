namespace Momoka.Shell.Views;

/// <summary>
/// 内嵌页捕获了某一条登录身份。设置页在服务器/账号切换之后用它判断右半边挂着的还是不是当下那一台，
/// 不是就重新导航一趟。网页控制台与飞牛的原生页都实现它。
/// </summary>
internal interface ISessionScopedPage
{
    bool IsCurrentSession { get; }
}
