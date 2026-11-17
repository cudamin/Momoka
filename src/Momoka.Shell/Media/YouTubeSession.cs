using Microsoft.Web.WebView2.Core;

namespace Momoka.Shell.Media;

/// <summary>
/// YouTube 登录与详情页预告共用的一份 WebView2 配置：设置页登录写进去的 Cookie，右上角预览下次起播就能用。
/// <para>
/// 单独一份配置而不是复用内嵌控制台那一份：两边都存第三方站点的 Cookie，混在一起时清理或排查会互相牵连。
/// 配置落在 <c>webview2/youtube</c>，只含 Google／YouTube 的 Cookie，不进设置文件、不写日志。
/// </para>
/// <para>
/// <see cref="CoreWebView2Environment"/> 由这里单飞创建：同一个用户数据目录在同一进程里建第二个环境会失败，
/// 而设置页登录窗和详情页预览是同时可能存在的。
/// </para>
/// </summary>
internal sealed class YouTubeSession(string profileFolder)
{
    private static readonly CoreWebView2EnvironmentOptions Options = new()
    {
        // 详情页右上角是没有用户手势的自动播放；登录窗与预览共用这一份环境，所以策略只能在一处定。
        AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required"
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CoreWebView2Environment? _environment;
    private bool _signedIn;

    internal string ProfileFolder => profileFolder;

    /// <summary>最近一次观察到的登录状态；页面用它决定失败后要不要提示去登录。</summary>
    internal bool IsSignedIn => _signedIn;

    /// <summary>登录或退出之后喊一声，已经开着、刚失败过的详情页可以立刻重试预览。</summary>
    internal event Action? Changed;

    internal async Task<CoreWebView2Environment> GetEnvironmentAsync(CancellationToken token)
    {
        if (_environment is { } ready) return ready;

        await _gate.WaitAsync(token).ConfigureAwait(true);
        try
        {
            if (_environment is null)
            {
                Directory.CreateDirectory(profileFolder);
                _environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, profileFolder, Options);
            }
            return _environment;
        }
        finally { _gate.Release(); }
    }

    /// <summary>登录窗或预览读回 Cookie 后把这个状态记下来；不变就不喊。</summary>
    internal void Report(bool signedIn)
    {
        if (_signedIn == signedIn) return;
        _signedIn = signedIn;
        Changed?.Invoke();
    }
}
