import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

// This fixture implements only browser HTML form controls used by the real protocol.
// It executes the C#-generated JavaScript without network, cookies, or a browser profile.
const scripts = JSON.parse(readFileSync(process.argv[2], 'utf8'));
const Secret = 'synthetic-private-csrf';
const Nonce = 'fixture-nonce';
const tests = [];
const test = (name, run) => tests.push({ name, run });

function loginState({ user, account = false, login = false, nav = true, origin = 'https://movie.douban.com', pathname = '/', loginUrl = 'https://accounts.douban.com/passport/login' } = {}) {
  const context = vm.createContext({
    location: { origin, pathname }, URL,
    document: {
      get cookie() { throw new Error('Login verification must not read cookies'); },
      querySelector(selector) {
        assert.equal(selector, '#db-global-nav');
        return nav ? { querySelector(selector) {
          if (selector === 'a.nav-login') return login ? { href: loginUrl } : null;
          if (selector === '.nav-user-account .bn-more') return account ? {} : null;
          throw new Error(`Unexpected login selector: ${selector}`);
        } } : null;
      }
    },
    fetch() { throw new Error('Login verification must not send mutations'); }
  });
  context.window = context;
  context._GLOBAL_NAV = user === undefined ? {} : { USER_ID: user };
  return vm.runInContext(scripts.LoginState, context);
}

test('登录检查：账号导航和有效身份共同确认已登录，只返回状态', () => {
  assert.equal(loginState({ user: '123456', account: true }), 'connected');
});
test('登录检查：明确官方登录入口确认未登录', () => {
  assert.equal(loginState({ login: true }), 'signed-out');
  assert.equal(loginState({ user: '0', login: true }), 'signed-out');
});
test('登录检查：身份、账号入口、登录入口缺失或冲突均不能冒充成功', () => {
  for (const options of [{}, { nav: false }, { user: '123456' }, { account: true },
    { user: '123456', account: true, login: true }, { user: 'bad-id', account: true }])
    assert.equal(loginState(options), 'unknown');
});
test('登录检查：外站、错误路径和伪造登录地址无法确定登录态', () => {
  for (const options of [{ origin: 'https://evil.invalid', user: '123456', account: true },
    { pathname: '/subject/8/', user: '123456', account: true }, { login: true, loginUrl: 'https://evil.invalid/passport/login' }])
    assert.equal(loginState(options), 'unknown');
});

const decode = value => value.replace(/&(?:amp|lt|gt|quot|#39);/g, match =>
  ({ '&amp;': '&', '&lt;': '<', '&gt;': '>', '&quot;': '"', '&#39;': "'" })[match]);

function attributes(source) {
  const result = {};
  const pattern = /([^\s=<>/]+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+)))?/g;
  for (const match of source.matchAll(pattern)) result[match[1].toLowerCase()] = decode(match[2] ?? match[3] ?? match[4] ?? '');
  return result;
}

class Element {
  constructor(tag, attrs, text = '') {
    this.tagName = tag.toUpperCase();
    this.attrs = attrs;
    this.name = attrs.name ?? '';
    this.type = attrs.type ?? (tag === 'input' ? 'text' : '');
    this.value = attrs.value ?? text;
    this.checked = Object.hasOwn(attrs, 'checked');
    this.disabled = Object.hasOwn(attrs, 'disabled');
  }
  getAttribute(name) { return this.attrs[name] ?? null; }
}

class Form {
  constructor(attrs, html) {
    this.attrs = attrs;
    this.elements = [];
    const pattern = /<input\b([^>]*)>|<textarea\b([^>]*)>([\s\S]*?)<\/textarea>|<select\b([^>]*)>([\s\S]*?)<\/select>/gi;
    for (const match of html.matchAll(pattern)) {
      if (match[1] !== undefined) this.elements.push(new Element('input', attributes(match[1])));
      else if (match[2] !== undefined) this.elements.push(new Element('textarea', attributes(match[2]), decode(match[3])));
      else {
        const attrs = attributes(match[4]);
        const options = [...match[5].matchAll(/<option\b([^>]*)>([\s\S]*?)<\/option>/gi)].map(option => ({ attrs: attributes(option[1]), text: decode(option[2]) }));
        const selected = options.find(option => Object.hasOwn(option.attrs, 'selected')) ?? options[0];
        this.elements.push(new Element('select', { ...attrs, value: selected?.attrs.value ?? selected?.text ?? '' }));
      }
    }
  }
  getAttribute(name) { return this.attrs[name] ?? null; }
  querySelectorAll(selector) {
    const match = /^(input|textarea|select)?(?:\[name="([^"]+)"\])?(?::checked)?$/.exec(selector);
    if (!match) throw new Error(`Unsupported fixture selector: ${selector}`);
    return this.elements.filter(element => (!match[1] || element.tagName === match[1].toUpperCase())
      && (!match[2] || element.name === match[2]) && (!selector.endsWith(':checked') || element.checked));
  }
  querySelector(selector) { return this.querySelectorAll(selector)[0] ?? null; }
}

class DOMParserFixture {
  parseFromString(html) {
    const match = /<form\b([^>]*)>([\s\S]*?)<\/form>/i.exec(html);
    const form = match ? new Form(attributes(match[1]), match[2]) : null;
    return { querySelector: selector => { assert.equal(selector, 'form'); return form; } };
  }
}

class FormDataFixture {
  constructor(form) {
    this.entries = form.elements.filter(element => element.name && !element.disabled
      && !['submit', 'button', 'reset', 'image'].includes(element.type)
      && (!['radio', 'checkbox'].includes(element.type) || element.checked))
      .map(element => [element.name, element.value]);
  }
  *[Symbol.iterator]() { yield* this.entries; }
}

function formHtml({ action = '/j/subject/8/interest', current = 'wish' } = {}) {
  return `<form method="POST" action="${action}">
    <input type="radio" name="interest" value="wish" ${current === 'wish' ? 'checked' : ''}>
    <input type="radio" name="interest" value="do" ${current === 'do' ? 'checked' : ''}>
    <input type="radio" name="interest" value="collect" ${current === 'collect' ? 'checked' : ''}>
    <input type="hidden" name="rating" value="5">
    <textarea name="comment">已写的短评 &amp; 保留</textarea>
    <input name="tags" value="old">
    <input type="checkbox" name="private" value="1" checked>
    <input type="hidden" name="foldcollect" value="U">
    <select name="comment_status"><option value="0">公开</option><option value="1" selected>仅自己</option></select>
    <input type="checkbox" name="share-shuo" value="1" checked>
    <input type="checkbox" name="sync-douban" value="1" checked>
    <input type="hidden" name="facebook" value="1">
    <input type="hidden" name="twitter" value="1">
    <input type="hidden" name="weibo" value="1">
    <input type="submit" name="save" value="保存">
  </form>`;
}

function environment(options = {}) {
  const messages = [];
  const requests = [];
  let notify = null;
  const subject = { textContent: options.title ?? '测试作品 第一季' };
  const document = {
    cookie: options.cookie ?? `other=synthetic; ck=${encodeURIComponent(Secret)}`,
    querySelector(selector) {
      if (selector === 'h1 [property="v:itemreviewed"]') return subject;
      if (selector === 'link[rel="canonical"]') return { href: options.canonical ?? 'https://movie.douban.com/subject/8/' };
      if (selector === '#info') return { innerText: options.info ?? '季数: 1\n集数: 12\n单集片长: 24分钟' };
      throw new Error(`Unexpected document selector: ${selector}`);
    }
  };
  const context = vm.createContext({
    document,
    location: { origin: options.origin ?? 'https://movie.douban.com', pathname: options.pathname ?? '/subject/8/' },
    URL, URLSearchParams,
    AbortSignal: { timeout: () => undefined },
    DOMParser: DOMParserFixture, FormData: FormDataFixture,
    chrome: { webview: { postMessage(raw) {
      const message = JSON.parse(raw);
      messages.push(message);
      if (notify) { const resolve = notify; notify = null; resolve(message); }
    } } },
    fetch: async (url, init = {}) => {
      const request = { url: String(url), method: init.method ?? 'GET', credentials: init.credentials, redirect: init.redirect,
        body: init.body ? [...init.body] : null };
      requests.push(request);
      if (request.method === 'GET') return { ok: options.getOk ?? true, json: async () => options.payload ?? { html: formHtml(options), tags: ['动画', 'fixture'] } };
      assert.equal(request.method, 'POST');
      return { ok: options.postOk ?? true, json: async () => options.postResult ?? { r: 0 } };
    }
  });
  context.window = context;
  return {
    context, requests, messages,
    async run(script) {
      assert.equal(notify, null);
      const response = new Promise(resolve => { notify = resolve; });
      vm.runInContext(script, context, { timeout: 1000 });
      const message = await Promise.race([response, new Promise((_, reject) => {
        const timer = setTimeout(() => reject(new Error('Protocol did not produce a reply')), 1000); timer.unref();
      })]);
      assert.equal(message.nonce, Nonce);
      return message;
    }
  };
}

test('metadata exposes title and confirmed episode count', async () => {
  const fixture = environment();
  const message = await fixture.run(scripts.Metadata);
  assert.equal(message.state, 'metadata');
  assert.equal(message.title, '测试作品 第一季');
  assert.equal(message.count, 12);
  assert.equal(fixture.requests.length, 0);
});

test('metadata keeps unknown count null', async () => {
  const fixture = environment({ info: '季数: 1\n单集片长: 24分钟' });
  const message = await fixture.run(scripts.Metadata);
  assert.equal(message.state, 'metadata');
  assert.equal(message.count, null);
});

test('search returns only validated TV subjects with public metadata', () => {
  const messages = [];
  const query = '测试作品';
  const context = vm.createContext({
    location: { origin: 'https://search.douban.com', pathname: '/movie/subject_search',
      search: '?search_text=' + encodeURIComponent(query) + '&cat=1002' },
    URL, URLSearchParams,
    window: { __DATA__: { items: [
      { id: 8, title: '测试作品 (2024)', url: 'https://movie.douban.com/subject/8/',
        labels: [{ text: '剧集' }], abstract: '中国大陆 / 剧情 / 12分钟' },
      { id: 9, title: '电影结果', url: 'https://movie.douban.com/subject/9/', labels: [{ text: '电影' }] },
      { id: 10, title: '错误目标', url: 'https://movie.douban.com/subject/11/', labels: [{ text: '剧集' }] },
      { id: 12, title: '外部目标', url: 'https://evil.invalid/subject/12/', labels: [{ text: '剧集' }] },
      { id: 0, title: '无效编号', url: 'https://movie.douban.com/subject/0/', labels: [{ text: '剧集' }] }
    ] } },
    chrome: { webview: { postMessage(raw) { messages.push(JSON.parse(raw)); } } }
  });
  vm.runInContext(scripts.Search, context, { timeout: 1000 });
  assert.equal(messages.length, 1);
  assert.deepEqual(Object.keys(messages[0]).sort(), ['candidates', 'nonce', 'state']);
  assert.equal(messages[0].nonce, Nonce);
  assert.equal(messages[0].state, 'results');
  assert.deepEqual(JSON.parse(JSON.stringify(messages[0].candidates)), [
    { id: 8, title: '测试作品 (2024)', year: 2024, detail: '中国大陆 / 剧情 / 12分钟' }
  ]);
  assert.equal(scripts.Search.includes('document.cookie'), false);
  assert.equal(scripts.Search.includes('fetch('), false);
});

test('search rejects a different host, route, or query before reading results', () => {
  const encoded = encodeURIComponent('测试作品');
  for (const location of [
    { origin: 'https://evil.invalid', pathname: '/movie/subject_search', search: '?search_text=' + encoded + '&cat=1002' },
    { origin: 'https://search.douban.com', pathname: '/movie/other', search: '?search_text=' + encoded + '&cat=1002' },
    { origin: 'https://search.douban.com', pathname: '/movie/subject_search', search: '?search_text=x&cat=1002' }
  ]) {
    const messages = [];
    const context = vm.createContext({ location, URL, URLSearchParams,
      window: { __DATA__: { items: [{ id: 8 }] } },
      chrome: { webview: { postMessage(raw) { messages.push(JSON.parse(raw)); } } }
    });
    vm.runInContext(scripts.Search, context, { timeout: 1000 });
    assert.equal(messages.length, 1);
    assert.equal(messages[0].state, 'origin');
    assert.equal(Object.hasOwn(messages[0], 'candidates'), false);
  }
});

for (const action of ['/j/subject/8/interest', '/subject/8/interest']) test(`prepare accepts exact same-origin action ${action}`, async () => {
  const fixture = environment({ action });
  assert.equal((await fixture.run(scripts.PrepareDoing)).state, 'ready');
  assert.equal(fixture.requests.filter(request => request.method === 'POST').length, 0);
});

for (const withR of [false, true]) test(`prepare accepts form JSON ${withR ? 'with r=0' : 'without r=0'}`, async () => {
  const payload = { html: formHtml(), tags: ['动画', 'fixture'] };
  if (withR) payload.r = 0;
  const fixture = environment({ payload });
  assert.equal((await fixture.run(scripts.PrepareDoing)).state, 'ready');
  assert.deepEqual(fixture.requests.map(request => request.method), ['GET']);
});

test('prepare rejects explicit login error response', async () => {
  const fixture = environment({ payload: { r: 1, code: 403 } });
  assert.equal((await fixture.run(scripts.PrepareDoing)).state, 'login');
  assert.equal(fixture.requests.length, 1);
});

test('commit preserves score, comment, tags, private and existing fields', async () => {
  const fixture = environment();
  assert.equal((await fixture.run(scripts.PrepareDoing)).state, 'ready');
  assert.equal((await fixture.run(scripts.Commit)).state, 'done');
  const posted = new URLSearchParams(fixture.requests[1].body);
  assert.equal(posted.get('rating'), '5');
  assert.equal(posted.get('comment'), '已写的短评 & 保留');
  assert.equal(posted.get('tags'), '动画 fixture');
  assert.equal(posted.get('private'), '1');
  assert.equal(posted.get('foldcollect'), 'U');
  assert.equal(posted.get('comment_status'), '1');
  assert.equal(posted.get('interest'), 'do');
  assert.equal(posted.get('ck'), Secret);
  assert.equal(posted.has('save'), false);
});

test('collected subject never downgrades to doing', async () => {
  const fixture = environment({ current: 'collect' });
  assert.equal((await fixture.run(scripts.PrepareDoing)).state, 'watched');
  assert.equal((await fixture.run(scripts.PrepareDone)).state, 'watched');
  assert.equal(fixture.requests.filter(request => request.method === 'POST').length, 0);
  assert.equal(fixture.context.__momokaCollection, undefined);
});

test('already doing subject requires no mutation', async () => {
  const fixture = environment({ current: 'do' });
  assert.equal((await fixture.run(scripts.PrepareDoing)).state, 'watching');
  assert.equal(fixture.requests.filter(request => request.method === 'POST').length, 0);
});

test('doing subject can still be marked watched after the last episode', async () => {
  const fixture = environment({ current: 'do' });
  assert.equal((await fixture.run(scripts.PrepareDone)).state, 'ready');
  assert.equal((await fixture.run(scripts.Commit)).state, 'done');
  assert.equal(new URLSearchParams(fixture.requests[1].body).get('interest'), 'collect');
});

for (const action of ['https://evil.invalid/j/subject/8/interest', '//evil.invalid/j/subject/8/interest',
  '/j/subject/9/interest', '/j/subject/8/interest?next=other', '/j/subject/8/interest#fragment'])
  test(`prepare rejects unsafe form action ${action}`, async () => {
    const fixture = environment({ action });
    assert.equal((await fixture.run(scripts.PrepareDoing)).state, 'form');
    assert.equal(fixture.requests.filter(request => request.method === 'POST').length, 0);
  });

test('cookie and form details never enter native messages', async () => {
  const fixture = environment();
  await fixture.run(scripts.PrepareDone);
  await fixture.run(scripts.Commit);
  const messages = JSON.stringify(fixture.messages);
  for (const forbidden of [Secret, 'ck=', '已写的短评', 'rating', 'private', '动画 fixture']) assert.equal(messages.includes(forbidden), false);
  assert.deepEqual(Object.keys(fixture.messages[0]).sort(), ['nonce', 'state']);
});

test('prepare is read only and commit sends exactly one POST', async () => {
  const fixture = environment();
  assert.equal((await fixture.run(scripts.PrepareDone)).state, 'ready');
  assert.equal(fixture.requests.length, 1);
  assert.equal(fixture.requests[0].method, 'GET');
  assert.equal((await fixture.run(scripts.Commit)).state, 'done');
  assert.equal((await fixture.run(scripts.Commit)).state, 'origin');
  assert.equal(fixture.requests.filter(request => request.method === 'POST').length, 1);
  assert.equal(new URLSearchParams(fixture.requests[1].body).get('interest'), 'collect');
});

test('all sharing and sync controls are excluded from the POST', async () => {
  const fixture = environment();
  await fixture.run(scripts.PrepareDone);
  await fixture.run(scripts.Commit);
  const body = new URLSearchParams(fixture.requests[1].body);
  for (const key of ['share-shuo', 'sync-douban', 'facebook', 'twitter', 'weibo']) assert.equal(body.has(key), false);
});

test('POST nonzero r is rejected', async () => {
  const fixture = environment({ postResult: { r: 7, detail: 'server body must stay internal' } });
  await fixture.run(scripts.PrepareDone);
  assert.equal((await fixture.run(scripts.Commit)).state, 'rejected');
  assert.equal(JSON.stringify(fixture.messages).includes('server body'), false);
});

test('missing CSRF cookie prevents readiness and mutation', async () => {
  const fixture = environment({ cookie: 'only=non-secret-fixture' });
  assert.equal((await fixture.run(scripts.PrepareDone)).state, 'login');
  assert.equal(fixture.requests.filter(request => request.method === 'POST').length, 0);
});

test('origin mismatch never reads or writes the network', async () => {
  const fixture = environment({ origin: 'https://evil.invalid' });
  assert.equal((await fixture.run(scripts.PrepareDone)).state, 'origin');
  assert.equal(fixture.requests.length, 0);
});

test('requests require same origin credentials and reject redirects', async () => {
  const fixture = environment();
  await fixture.run(scripts.PrepareDone);
  await fixture.run(scripts.Commit);
  for (const request of fixture.requests) {
    assert.equal(request.credentials, 'same-origin');
    assert.equal(request.redirect, 'error');
  }
});

let failed = 0;
for (const { name, run } of tests) {
  try { await run(); console.log(`PASS ${name}`); }
  catch (error) { failed++; console.error(`FAIL ${name}\n${error.stack}`); }
}
console.log(`${tests.length} tests, ${tests.length - failed} passed, ${failed} failed; actual generated protocol, offline VM fixture`);
process.exitCode = failed === 0 ? 0 : 1;
