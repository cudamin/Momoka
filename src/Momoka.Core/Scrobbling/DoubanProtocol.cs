using System.Text.Json;

namespace Momoka.Scrobbling;

/// <summary>Only public metadata and operation status can leave the trusted Douban page.</summary>
public static class DoubanProtocol
{
    // Douban nav/2.0.1/shire/bundle.js uses _GLOBAL_NAV.USER_ID and .nav-user-account .bn-more.
    // Read the trusted homepage only; neither account identifiers nor cookies leave the page.
    public static string LoginStateScript => """
        (() => {
          if (location.origin !== 'https://movie.douban.com' || location.pathname !== '/') return 'unknown';
          const nav = document.querySelector('#db-global-nav');
          if (!nav) return 'unknown';
          const login = nav.querySelector('a.nav-login');
          const account = nav.querySelector('.nav-user-account .bn-more');
          const user = String(window._GLOBAL_NAV?.USER_ID ?? '');
          const identified = /^[1-9]\d*$/.test(user);
          if (identified && account && !login) return 'connected';
          if (login && !identified && !account) {
            try {
              const url = new URL(login.href);
              if (url.origin === 'https://accounts.douban.com' && url.pathname === '/passport/login') return 'signed-out';
            } catch { }
          }
          return 'unknown';
        })();
        """;

    public static bool IsTrustedNavigation(string? address, bool login = false) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == "https"
        && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && (uri.Host == "movie.douban.com"
            || uri.Host == "search.douban.com" && uri.AbsolutePath == "/movie/subject_search"
                && System.Web.HttpUtility.ParseQueryString(uri.Query)["cat"] == "1002"
            || login && uri.Host is "accounts.douban.com" or "www.douban.com");

    /// <summary>Reads bounded site search results; no cookies, DOM fragments, or mutations leave WebView.</summary>
    public static string SearchScript(string query, string nonce) => $$"""
        (() => {
          const nonce = {{JsonSerializer.Serialize(nonce)}};
          const query = {{JsonSerializer.Serialize(query)}};
          const send = value => chrome.webview.postMessage(JSON.stringify({ nonce, ...value }));
          if (location.origin !== 'https://search.douban.com' || location.pathname !== '/movie/subject_search'
              || new URLSearchParams(location.search).get('cat') !== '1002'
              || new URLSearchParams(location.search).get('search_text') !== query) { send({state:'origin'}); return; }
          const data = window.__DATA__;
          if (!data || !Array.isArray(data.items)) { send({state:'search'}); return; }
          const candidates = [];
          for (const item of data.items.slice(0, 20)) {
            if (!item || !Number.isSafeInteger(item.id) || item.id <= 0 || typeof item.title !== 'string'
                || !item.title.trim() || typeof item.url !== 'string') continue;
            const labels = Array.isArray(item.labels) ? item.labels.map(label => label?.text).filter(value => typeof value === 'string') : [];
            if (!labels.includes('剧集')) continue;
            const url = new URL(item.url, location.origin);
            if (url.origin !== 'https://movie.douban.com' || url.pathname !== '/subject/'+item.id+'/') continue;
            const yearMatch = item.title.match(/[（(](\d{4})[）)]\s*$/);
            candidates.push({id:item.id,title:item.title.trim(),year:yearMatch ? Number(yearMatch[1]) : null,detail:typeof item.abstract === 'string' ? item.abstract.slice(0,240) : null});
          }
          send({state:'results',candidates});
        })();
        """;

    public static string PrepareScript(long id, string nonce) => $$"""
        (() => {
          const nonce = {{JsonSerializer.Serialize(nonce)}};
          const id = {{id}};
          const send = value => chrome.webview.postMessage(JSON.stringify({ nonce, ...value }));
          if (location.origin !== 'https://movie.douban.com' || location.pathname !== '/subject/'+id+'/') { send({state:'origin'}); return; }
          const title = document.querySelector('h1 [property="v:itemreviewed"]')?.textContent?.trim();
          const canonical = document.querySelector('link[rel="canonical"]')?.href;
          if (!title || (canonical && new URL(canonical).pathname !== '/subject/'+id+'/')) { send({state:'metadata'}); return; }
          const info = document.querySelector('#info')?.innerText || '';
          const count = info.match(/(?:^|\n)\s*集数\s*[:：]\s*(\d+)\s*(?:\n|$)/);
          send({state:'metadata', title, count: count ? Number(count[1]) : null});
        })();
        """;

    // Douban.init_collect_btn / remote_submit_json in douban.751471eacdfd.js.
    // The form and CSRF value stay inside the same-origin browser. Native code rechecks permission
    // between reading the form and issuing the mutation.
    public static string PrepareMarkScript(long id, bool completed, string nonce) => $$"""
        (() => {
          const nonce = {{JsonSerializer.Serialize(nonce)}};
          const id = {{id}};
          const interest = {{JsonSerializer.Serialize(completed ? "collect" : "do")}};
          const send = value => chrome.webview.postMessage(JSON.stringify({ nonce, ...value }));
          if (location.origin !== 'https://movie.douban.com' || location.pathname !== '/subject/'+id+'/') { send({state:'origin'}); return; }
          (async () => {
            try {
              const response = await fetch('/j/subject/'+id+'/interest', {credentials:'same-origin', redirect:'error', signal:AbortSignal.timeout(8000)});
              if (!response.ok) { send({state:'login'}); return; }
              const data = await response.json();
              if ((data.r !== undefined && data.r !== 0) || typeof data.html !== 'string') { send({state:'login'}); return; }
              const doc = new DOMParser().parseFromString(data.html, 'text/html');
              const form = doc.querySelector('form');
              if (!form) { send({state:'form'}); return; }
              const current = form.querySelector('input[name="interest"]:checked')?.value;
              if (current === 'collect' || current === interest) { send({state:'done'}); return; }
              const choices = [...form.querySelectorAll('input[name="interest"]')];
              if (!choices.some(input => input.value === interest)) { send({state:'form'}); return; }
              const action = new URL(form.getAttribute('action') || '', location.origin);
              if (action.origin !== location.origin || !['/j/subject/'+id+'/interest', '/subject/'+id+'/interest'].includes(action.pathname) || action.search || action.hash) { send({state:'form'}); return; }
              if (Array.isArray(data.tags)) {
                const tags = form.querySelector('input[name="tags"]');
                if (tags) tags.value = data.tags.join(' ');
              }
              const body = new URLSearchParams();
              for (const [key, value] of new FormData(form)) { if (typeof value !== 'string') { send({state:'form'}); return; } body.append(key,value); }
              body.set('interest', interest);
              for (const key of [...body.keys()]) if (/^(share|sync|facebook|twitter|weibo)/i.test(key)) body.delete(key);
              const ck = document.cookie.split(';').map(v => v.trim()).find(v => v.startsWith('ck='));
              if (!ck) { send({state:'login'}); return; }
              body.set('ck', decodeURIComponent(ck.slice(3)));
              window.__momokaCollection = {nonce, action:action.href, body};
              send({state:'ready'});
            } catch { send({state:'request'}); }
          })();
        })();
        """;

    public static string CommitScript(string nonce) => $$"""
        (() => {
          const nonce = {{JsonSerializer.Serialize(nonce)}};
          const send = state => chrome.webview.postMessage(JSON.stringify({nonce,state}));
          const pending = window.__momokaCollection;
          delete window.__momokaCollection;
          if (location.origin !== 'https://movie.douban.com' || !pending || pending.nonce !== nonce) { send('origin'); return; }
          (async () => {
            try {
              const response = await fetch(pending.action, {method:'POST', credentials:'same-origin', redirect:'error', body:pending.body, signal:AbortSignal.timeout(8000)});
              const result = response.ok ? await response.json() : null;
              send(result?.r === 0 ? 'done' : 'rejected');
            } catch { send('request'); }
          })();
        })();
        """;
}
