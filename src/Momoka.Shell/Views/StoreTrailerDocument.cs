namespace Momoka.Shell.Views;

/// <summary>YouTube官方IFrame API的固定背景文档；不接受服务器URL或凭据。</summary>
internal static class StoreTrailerDocument
{
    internal static string Build(string id)
    {
        if (id.Length != 11 || !id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            throw new ArgumentException("无效的预告片ID", nameof(id));
        return $$$$"""
            <!doctype html><html><head><meta name="referrer" content="strict-origin-when-cross-origin">
            <style>html,body{margin:0;width:100%;height:100%;overflow:hidden;background:transparent}
            iframe{position:absolute;left:50%;top:50%;width:100vw;height:100vh;transform:translate(-50%,-50%);border:0;pointer-events:none}
            @media(min-aspect-ratio:16/9){iframe{height:56.25vw}}@media(max-aspect-ratio:16/9){iframe{width:177.777778vh}}</style></head>
            <body><iframe id="video" tabindex="-1" aria-hidden="true" src="https://www.youtube-nocookie.com/embed/{{{{id}}}}?enablejsapi=1&amp;origin=https%3A%2F%2Fmomoka&amp;playsinline=1&amp;autoplay=0&amp;controls=0&amp;disablekb=1&amp;fs=0&amp;loop=1&amp;playlist={{{{id}}}}"
            allow="autoplay; encrypted-media" referrerpolicy="strict-origin-when-cross-origin"></iframe>
            <script>let p,active=false,muted=true;function onYouTubeIframeAPIReady(){p=new YT.Player('video',{events:{onReady:()=>{if(muted)p.mute();else p.unMute();chrome.webview.postMessage('ready');if(active)p.playVideo()},
            onStateChange:e=>{if(e.data===1)chrome.webview.postMessage('playing')},onError:()=>chrome.webview.postMessage('error'),
            onAutoplayBlocked:()=>chrome.webview.postMessage('error')}})};
            chrome.webview.addEventListener('message',e=>{if(e.data==='play')active=true;if(e.data==='pause')active=false;if(e.data==='mute')muted=true;if(e.data==='unmute')muted=false;if(!p)return;switch(e.data){case'play':p.playVideo();break;case'pause':p.pauseVideo();break;
            case'mute':p.mute();break;case'unmute':p.unMute();break}});</script><script src="https://www.youtube.com/iframe_api" onerror="chrome.webview.postMessage('error')"></script></body></html>
            """;
    }
}
