using Microsoft.Web.WebView2.Core;

namespace Momoka.Shell.Media;

/// <summary>
/// YouTube 预告与登录窗共用的一份 WebView2 配置，落在 <c>webview2/youtube</c>。
/// <para>
/// 详情页右上角的嵌入页用的是 <c>www.youtube.com/embed</c>，和登录窗同一个站点，所以登录窗里写下的
/// Cookie 下一次起播就能用；匿名配置在需要登录的网络上只会拿到 150。只含 Google／YouTube 的 Cookie，
/// 不进设置文件、不写日志。
/// </para>
/// <para>
/// <see cref="CoreWebView2Environment"/> 由这里单飞创建：同一个用户数据目录在同一进程里建第二个环境会失败，
/// 而登录窗、详情页预览和播放窗是同时可能存在的。
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

    internal string ProfileFolder => profileFolder;

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
}
