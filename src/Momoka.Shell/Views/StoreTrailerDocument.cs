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
            <script>let p,active=false,muted=true,captionReady=false,captionLanguage='';
            function captions(){if(!p||typeof p.getOption!=='function')return;
            try{const list=p.getOption('captions','tracklist')||[];const tracks=[],seen=new Set();
            for(const t of list){const language=(t&&t.languageCode)||'';if(!language||seen.has(language))continue;seen.add(language);
            tracks.push({language:language,name:t.displayName||t.languageName||language});}
            chrome.webview.postMessage('captions:'+JSON.stringify({language:captionLanguage,tracks:tracks}));}catch(_){}}
            function onYouTubeIframeAPIReady(){p=new YT.Player('video',{events:{onReady:()=>{if(muted)p.mute();else p.unMute();chrome.webview.postMessage('ready');if(active)p.playVideo();
            if(p.loadModule)p.loadModule('captions');captions()},
            onStateChange:e=>{if(e.data===1)chrome.webview.postMessage('playing');else if(e.data===2)chrome.webview.postMessage('paused')},
            onApiChange:()=>{if(!captionReady&&p&&typeof p.setOption==='function'){captionReady=true;captionLanguage='';
            try{p.setOption('captions','track',{})}catch(_){}}captions()},onError:e=>chrome.webview.postMessage('error:'+e.data),
            onAutoplayBlocked:()=>chrome.webview.postMessage('blocked')}})};
            chrome.webview.addEventListener('message',e=>{if(typeof e.data!=='string')return;
            if(e.data==='play')active=true;if(e.data==='pause')active=false;if(e.data==='mute')muted=true;if(e.data==='unmute')muted=false;
            if(e.data.startsWith('caption:')){if(p&&typeof p.setOption==='function'){const language=e.data.substring(8);
            try{p.setOption('captions','track',language?{languageCode:language}:{});captionLanguage=language}catch(_){}}return}
            if(!p)return;switch(e.data){case'play':p.playVideo();break;case'pause':p.pauseVideo();break;
            case'mute':p.mute();break;case'unmute':p.unMute();break}});</script><script src="https://www.youtube.com/iframe_api" onerror="chrome.webview.postMessage('error')"></script></body></html>
            """;
    }
}
