using Momoka.Emby;

namespace Momoka.Shell.ViewModels;

/// <summary>飞牛服务器管理页：在已登录的飞牛账号之上再要求管理员身份。</summary>
public abstract class FnNasAdminViewModel : FnNasSessionViewModel
{
    protected override bool Allowed(EmbySessionScope scope) => scope.IsAdministrator == true;
    protected override bool TracksPermissions => true;
    protected override string DeniedTitle => "需要飞牛影视管理员账号";
    protected override string DeniedMessage => "请在服务器中连接飞牛影视，并使用管理员账号打开管理页面。";
}
