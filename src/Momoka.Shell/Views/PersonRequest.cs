using Momoka.Emby;

namespace Momoka.Shell.Views;

/// <summary>
/// 人物页的导航参数。带的是演职人员卡上那张存根（名字、id、头像标签和来源）—— 页面先用它把名字和
/// 一张可能在缓存里的头像摆出来，完整的人物条目再按 id 问服务器。
/// <para>
/// 和 <see cref="DetailRequest"/> 一样把服务容器交给页面，而不是让页面去够静态单例：这一页以后要是
/// 开第二个窗口，它知道的仍然只是「从哪儿取服务」，不是「当前那一个」。
/// </para>
/// </summary>
internal sealed record PersonRequest
{
    public required IServiceProvider Services { get; init; }

    /// <summary>演职人员卡上那一条：人物类型、id、名字，以及可能有的 PrimaryImageTag。</summary>
    public required EmbyItem Person { get; init; }

    public string Title => Person.Name;

    public static PersonRequest For(IServiceProvider services, EmbyItem person) => new()
    {
        Services = services,
        Person = person
    };
}
