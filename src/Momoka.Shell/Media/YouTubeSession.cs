using Microsoft.Web.WebView2.Core;
using Momoka.Diagnostics;
using Momoka.Infrastructure;

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
/// <para>
/// <b>代理服务器（2026-11-18，设置 → 关于 → 网络）在这里生效。</b>Chromium 的 <c>--proxy-server</c> 只在浏览器
/// 进程启动时读一次，所以这里记住建环境时用的是哪个地址；设置页改了值（或恢复默认/恢复配置改了它）之后，
/// 下一次需要环境时用新值重建一份 —— 已经打开的预告窗保留旧会话继续播，关掉再开就走新代理。
/// </para>
/// </summary>
internal sealed class YouTubeSession(string profileFolder, Func<string> proxyServer)
{
    private const string Category = "YouTube 会话";

    /// <summary>详情页右上角是没有用户手势的自动播放；登录窗与预览共用这一份环境，所以策略只能在一处定。</summary>
    private const string AutoplayArgument = "--autoplay-policy=no-user-gesture-required";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CoreWebView2Environment? _environment;
    private string? _proxyInUse;

    internal string ProfileFolder => profileFolder;

    internal async Task<CoreWebView2Environment> GetEnvironmentAsync(CancellationToken token)
    {
        var requested = Current();
        if (_environment is { } ready && string.Equals(_proxyInUse, requested, StringComparison.Ordinal)) return ready;

        await _gate.WaitAsync(token).ConfigureAwait(true);
        try
        {
            var desired = Current();
            if (_environment is { } existing)
            {
                if (string.Equals(_proxyInUse, desired, StringComparison.Ordinal)) return existing;

                // 代理变了：旧环境是拿旧参数起的浏览器进程，不能再交给新的视图。丢掉引用而不是 Dispose ——
                // 环境没有 Dispose，还在播的视图自己拖着它；新的一份在它们都关掉之后才建得起来。
                Log.Info(Category, "代理服务器已改变，按新地址重建 YouTube 会话");
                _environment = null;
            }

            Directory.CreateDirectory(profileFolder);
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = Arguments(desired)
            };
            _environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, profileFolder, options);
            _proxyInUse = desired;
            return _environment;
        }
        finally { _gate.Release(); }
    }

    /// <summary>此刻设置里填的代理地址；空白原样交给下面的判断，认不出的值在 <see cref="Arguments"/> 里被忽略。</summary>
    private string Current() => proxyServer() ?? "";

    private static string Arguments(string proxy) =>
        ProxyEndpoint.BrowserArgument(proxy) is { } proxyArgument
            ? AutoplayArgument + " " + proxyArgument
            : AutoplayArgument;
}
