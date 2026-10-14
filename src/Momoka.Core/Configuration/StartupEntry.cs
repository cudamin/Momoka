namespace Momoka.Configuration;

/// <summary>
/// 「开机自启」这件事里能被单测钉住的那一半：写进 Windows 启动项的那一行长什么样，以及读回来的那一行算不算
/// 「这台机器登录之后会把本程序拉起来」。
/// <para>
/// 真正的注册表读写在外壳（<c>Momoka.Shell.Platform.StartupRegistration</c>）—— Core 不碰 Windows API。这里
/// 只有判断，因为这里每一个判断都有一个「写错了屏上什么也看不出来」的坏法：路径里带空格时少了引号，那一格就
/// 从空格处被截断，Windows 会去找一个不存在的程序，而设置页上的开关照样是开的；程序换了目录之后还认那一格，
/// 报的就是「会自启」，起来的却是旧的那一份。
/// </para>
/// <para>
/// 这一条值住 <c>HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run</c>，名字见
/// <see cref="ValueName"/>。任务管理器「启动」页把它关掉时不删这一格，而是往
/// <c>…\Explorer\StartupApproved\Run</c> 的**同名**值里写一份二进制标记，所以「关掉了」这件事只看 Run 那一格
/// 是看不出来的 —— 见 <see cref="TurnedOff"/>。这也是为什么本功能不另存一份偏好：注册表里那两格就是事实，
/// 另存一份迟早会和它对不上，而用户看任务管理器时看到的是那两格。
/// </para>
/// </summary>
public static class StartupEntry
{
    /// <summary>
    /// 那条值的名字。任务管理器「启动」页上显示的就是它，所以取程序自己的名字。
    /// </summary>
    public const string ValueName = AppIdentity.Title;

    /// <summary>
    /// <c>StartupApproved</c> 那一份二进制里「被关掉」的头一个字节（另一档是 2，表示开着）。整份是 12 字节，
    /// 后面那 8 个字节是关掉它的时间，与判断无关。
    /// </summary>
    private const byte Disabled = 3;

    /// <summary>
    /// 要写进 Run 那一格的那一行：程序的完整路径，两头加引号。
    /// <para>
    /// 引号不是装饰。那一格是一整条命令行，Windows 按命令行的规矩拆它；路径里有空格时没有引号就会从空格处
    /// 截断，比如 <c>C:\Program Files\Momoka\Momoka.exe</c> 会被当成 <c>C:\Program</c> 加一个参数。
    /// </para>
    /// </summary>
    public static string Command(string exePath)
    {
        var path = (exePath ?? "").Trim().Trim('"').Trim();
        if (path.Length == 0) throw new ArgumentException("可执行文件路径是空的", nameof(exePath));

        return $"\"{path}\"";
    }

    /// <summary>
    /// 那一行是不是就是 <paramref name="exePath"/> 这个程序。
    /// <para>
    /// 只认头一个词：我们自己写进去的永远是「引号 + 路径」，但手改过的那一格、或者别的程序留下的那一格后面
    /// 可能还拖着参数，所以带引号就取到后引号，不带就取到第一个空白。比的是整条路径、不分大小写 —— 路径的
    /// 大小写在 Windows 上不区分。
    /// </para>
    /// <para>
    /// 比的是「本程序」，所以程序换了地方（整个目录被搬走、或者从旧版本目录换到新版本目录）之后这里答「不是」：
    /// 那一格指着的仍是旧的那一份，这一次登录起来的不会是当前这个程序。设置页上那一行于是显示「关」，用户拨
    /// 一下就会把路径改写成现在这一份。
    /// </para>
    /// </summary>
    public static bool Names(string? stored, string exePath)
    {
        var path = (exePath ?? "").Trim().Trim('"').Trim();
        if (path.Length == 0) return false;

        var line = (stored ?? "").Trim();
        if (line.Length == 0) return false;

        var token = line.StartsWith('"')
            ? line[1..].Split('"', 2)[0]
            : line.Split(' ', 2)[0];

        return token.Trim().Equals(path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 任务管理器「启动」页把这一条关掉了没有。那一份二进制的头一个字节是 3 就是关；整格不在（null）表示
    /// 从来没被关过，也就是开着。
    /// </summary>
    public static bool TurnedOff(byte[]? approval) => approval is { Length: > 0 } bytes && bytes[0] == Disabled;

    /// <summary>
    /// 这台机器登录之后会不会真的把当前这个程序拉起来：那一格在、指的就是本程序，而且没在任务管理器里被关掉。
    /// </summary>
    public static bool IsEnabled(string? stored, byte[]? approval, string exePath) =>
        !TurnedOff(approval) && Names(stored, exePath);
}
