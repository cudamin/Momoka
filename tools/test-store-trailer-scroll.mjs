import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

// 执行本树实际匿名Api/Embed及生产宿主脚本；video/消息队列是明确边界，不替代WebView2原生运行。
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const probeSource = readFileSync(resolve(root, 'src/Momoka.Shell/ShellNavigationProbe.StoreTrailerWeb.cs'), 'utf8');
const documentSource = readFileSync(resolve(root, 'src/Momoka.Shell/Views/StoreTrailerDocument.cs'), 'utf8');
function rawConstant(name) {
  const match = probeSource.match(new RegExp(`private const string ${name} = """\\r?\\n([\\s\\S]*?)\\r?\\n\\s*""";`));
  assert.ok(match, `本树${name}源码边界缺失`);
  return match[1];
}
const api = rawConstant('Api');
const embed = rawConstant('Embed').match(/<script>([\s\S]*?)<\/script>/)?.[1];
const hostScript = documentSource.match(/<script>([\s\S]*?)<\/script>/)?.[1];
assert.ok(embed && hostScript, '实际iframe/生产宿主脚本边界缺失');

function runFixture(apiScript, embedScript) {
  const queue = [];
  const hostListeners = [];
  const hostWebListeners = [];
  const embedListeners = [];
  const messages = [];
  const videoEvents = new Map();
  let pauseEvents = 0;
  let lastState;
  const hostWindow = {};
  const embedWindow = { postMessage: data => queue.push(() => embedListeners.forEach(listener => listener({ data, source: hostWindow }))) };
  const nativeMessage = data => queue.push(() => hostWebListeners.forEach(listener => listener({ data })));
  const hostChrome = { webview: {
    addEventListener: (name, listener) => { assert.equal(name, 'message'); hostWebListeners.push(listener); },
    postMessage: data => {
      messages.push(data);
      if (data === 'ready') { nativeMessage('mute'); nativeMessage('play'); }
      if (String(data).startsWith('state:')) lastState = JSON.parse(data.substring(6));
    }
  } };
  const emitVideo = name => queue.push(() => (videoEvents.get(name) ?? []).forEach(listener => listener()));
  const video = {
    paused: true, muted: true,
    addTextTrack: (kind, label, language) => ({ kind, label, language, mode: 'disabled', addCue() {} }),
    addEventListener: (name, listener) => { if (!videoEvents.has(name)) videoEvents.set(name, []); videoEvents.get(name).push(listener); },
    requestVideoFrameCallback: callback => queue.push(callback),
    play() { if (this.paused) { this.paused = false; emitVideo('playing'); } },
    pause() { if (!this.paused) { this.paused = true; pauseEvents++; emitVideo('pause'); } }
  };
  const parent = { postMessage: data => queue.push(() => hostListeners.forEach(listener => listener({ data, source: embedWindow }))) };
  const embedContext = vm.createContext({ document: { querySelector: () => video, addEventListener() {} }, parent,
    addEventListener: (name, listener) => { assert.equal(name, 'message'); embedListeners.push(listener); },
    VTTCue: class {}, JSON });
  const hostContext = vm.createContext({ window: {}, chrome: hostChrome,
    document: { getElementById: () => ({ contentWindow: embedWindow }), addEventListener() {} },
    addEventListener: (name, listener) => { assert.equal(name, 'message'); hostListeners.push(listener); },
    setInterval: callback => { queue.push(callback); return 1; }, clearInterval() {},
    queueMicrotask: callback => queue.push(callback), JSON });
  hostContext.window = hostContext;
  const flush = () => {
    let turns = 0;
    while (queue.length) { assert.ok(turns++ < 200, '匿名消息队列没有落定'); queue.shift()(); }
  };
  vm.runInContext(embedScript, embedContext, { timeout: 1000, filename: 'actual-embed.js' });
  vm.runInContext(hostScript, hostContext, { timeout: 1000, filename: 'actual-production-host.js' });
  vm.runInContext(apiScript, hostContext, { timeout: 1000, filename: 'actual-api.js' });
  flush();
  const read = () => {
    lastState = undefined;
    nativeMessage('probe-state');
    flush();
    assert.ok(lastState, '实际probe-state没有经iframe回到原生宿主');
    return lastState;
  };
  const rows = [];
  const baseline = read();
  assert.equal(baseline.paused, false);
  assert.equal(baseline.muted, true);
  assert.ok(baseline.playCalls >= 1);
  assert.equal(baseline.pauseCalls, 0);
  rows.push(baseline);

  nativeMessage('pause'); flush();
  const paused = read();
  assert.equal(paused.paused, true);
  assert.equal(paused.pauseCalls, baseline.pauseCalls + 1);
  assert.ok(messages.includes('paused'), '实际暂停事件没有经过生产通知');
  rows.push(paused);
  nativeMessage('pause'); flush();
  const repeated = read();
  assert.equal(repeated.pauseCalls, paused.pauseCalls + 1, '重复API暂停调用未被计数');
  assert.equal(pauseEvents, 1, 'video边界重复pause不应再发pause事件');
  rows.push(repeated);
  nativeMessage('play'); flush();
  const resumed = read();
  assert.equal(resumed.paused, false);
  assert.equal(resumed.playCalls, baseline.playCalls + 1);
  rows.push(resumed);
  const notifications = messages.filter(message => message === 'paused').length;
  nativeMessage('probe-pause'); flush();
  const autonomous = read();
  assert.equal(autonomous.paused, true);
  assert.equal(autonomous.pauseCalls, resumed.pauseCalls, '网页自主暂停误算为API调用');
  assert.equal(messages.filter(message => message === 'paused').length, notifications + 1);
  rows.push(autonomous);
  nativeMessage('caption:zh'); flush();
  const caption = read();
  assert.equal(caption.caption, 'zh');
  assert.equal(caption.apiCaption, 'zh');
  rows.push(caption);
  return { rows, pauseEvents, messageCount: messages.length };
}

const result = runFixture(api, embed);
let negativeControls = 0;
for (const [name, badApi, badEmbed] of [
  ['API重复暂停计数缺失', api.replace('pauseCalls++;', ''), embed],
  ['实际video回读伪称仍播放', api, embed.replace('paused:v.paused', 'paused:false')],
  ['实际暂停生产通知缺失', api, embed.replace("v.addEventListener('pause',()=>parent.postMessage('paused','*'));", '')]
]) {
  assert.throws(() => runFixture(badApi, badEmbed), assert.AssertionError, name);
  negativeControls++;
}
const output = resolve(root, 'artifacts/captions-verification/store-scroll/js-api-readings.json');
mkdirSync(dirname(output), { recursive: true });
writeFileSync(output, JSON.stringify({ passed: true, ...result, negativeControls,
  probeSha256: createHash('sha256').update(probeSource).digest('hex'),
  documentSha256: createHash('sha256').update(documentSource).digest('hex'),
  realWebView: false, realNetwork: false, mainPlayback: false }, null, 2) + '\n');
console.log(`实际匿名JS脚本成功路径通过，${negativeControls}个预期失败对照均拒绝；video和消息边界为替身。`);
console.log(output);
