import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const [payloadArgument, outputArgument, managerArgument] = process.argv.slice(2);
if (!payloadArgument || !outputArgument) throw new Error('Usage: browser-tests.mjs <payload.json> <output-directory> [anonymous Emby maintabs snapshot]');
const payloadPath = path.resolve(payloadArgument);
const payload = JSON.parse(await fs.readFile(payloadPath, 'utf8'));
const output = path.resolve(outputArgument);
const insideTree = value => !path.relative(payload.tree, value).startsWith('..') && !path.isAbsolute(path.relative(payload.tree, value));
assert(insideTree(payloadPath) && insideTree(output), 'All evidence must stay in the exported worktree');
const sha = text => crypto.createHash('sha256').update(text).digest('hex').toUpperCase();
for (const [file, hash] of Object.entries(payload.sources)) assert.equal(sha(await fs.readFile(path.join(payload.tree, file))), hash, `Source changed after export: ${file}`);
assert.equal(sha(await fs.readFile(payload.coreAssembly)), payload.coreHash, 'The exported Release Core assembly changed');
const moduleRoot = process.env.MOMOKA_NODE_MODULES;
const require = moduleRoot ? createRequire(path.join(path.resolve(moduleRoot), '_momoka-tests.cjs')) : createRequire(import.meta.url);
const { chromium } = require('playwright');
const managerSource = managerArgument ? await fs.readFile(path.resolve(managerArgument), 'utf8') : null;
if (managerSource) assert(managerSource.includes('headerMiddle-withSectionTabs') && managerSource.includes('setTabVisible'), 'Expected the anonymous Emby maintabs snapshot, not an account/configuration file');
const launchOptions = { headless: true, args: ['--disable-background-networking', '--no-first-run'] };
if (process.env.MOMOKA_BROWSER_EXE) launchOptions.executablePath = path.resolve(process.env.MOMOKA_BROWSER_EXE);
else launchOptions.channel = 'msedge';
await fs.mkdir(output, { recursive: true });
const profile = path.join(output, 'browser-profile');
const context = await chromium.launchPersistentContext(profile, {
  ...launchOptions, viewport: { width: 1000, height: 640 }, serviceWorkers: 'block'
});
const browserVersion = context.browser()?.version();
const fixtureHash = sha(await fs.readFile(path.join(path.dirname(fileURLToPath(import.meta.url)), 'anonymous.html')));
const runnerHash = sha(await fs.readFile(fileURLToPath(import.meta.url)));
const fixturePath = path.resolve(path.dirname(fileURLToPath(import.meta.url)), 'anonymous.html');
const cases = [];
const errors = [];
let networkRequests = 0;
await context.route(/^(https?|wss?):/i, async route => { networkRequests++; await route.abort(); });
await fs.mkdir(output, { recursive: true });

async function createPage(product, options = {}) {
  const page = await context.newPage();
  page.on('pageerror', error => errors.push(error.message));
  await page.setViewportSize({ width: options.width || 1000, height: 640 });
  // Same document-created order as DashboardPage: native skin, then drawer/header policy.
  for (const script of payload.scripts[product]) await page.addInitScript({ content: script });
  const url = new URL(pathToFileURL(fixturePath));
  url.searchParams.set('product', product);
  if (options.nested === false) url.searchParams.set('nested', 'false');
  if (options.legacy) url.searchParams.set('legacy', 'true');
  if (options.absolute) url.searchParams.set('absolute', 'true');
  if (options.realHeader) url.searchParams.set('realHeader', 'true');
  await page.goto(url.href);
  if (managerSource && product === 'emby' && !options.withoutManager) await page.evaluate(source => fixture.installManager(source), managerSource);
  return page;
}
const tick = async page => { await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))); };
const visible = (page, selector) => page.locator(selector).isVisible();
async function noGlobalChrome(page) {
  for (const id of ['globalTitle', 'globalMenu', 'globalUser', 'serverDrawer', 'serverFooter']) assert.equal(await visible(page, `#${id}`), false, `Duplicate global chrome escaped: ${id}`);
}
async function test(name, run) {
  const started = Date.now();
  try { await run(); cases.push({ name, passed: true, elapsedMs: Date.now() - started }); console.log(`PASS ${name}`); }
  catch (error) { cases.push({ name, passed: false, error: error.message }); console.error(`FAIL ${name}: ${error.message}`); }
}
async function tabsVisible(page, expected) {
  const observed = await page.evaluate(() => fixture.tabs.map(tab => getComputedStyle(tab).display !== 'none' && tab.getClientRects().length > 0));
  assert.deepEqual(observed, expected, 'Header ancestors must not hide available tabs or reveal denied tabs');
}
async function contentBelowHeader(page) {
  const geometry = await page.evaluate(() => {
    const header = document.getElementById('globalHeader').getBoundingClientRect();
    const title = document.querySelector('#pageContent .pageTitle').getBoundingClientRect();
    return { headerBottom: header.bottom, titleTop: title.top, width: document.documentElement.clientWidth, overflow: document.documentElement.scrollWidth };
  });
  assert(geometry.titleTop >= geometry.headerBottom, `Header overlaps content: ${JSON.stringify(geometry)}`);
  assert(geometry.overflow <= geometry.width + 1, 'Narrow tabs must not widen the whole document');
}

try {
  await test('emby: actual contained header, page actions, headroom and drawer offsets remain usable', async () => {
    const page = await createPage('emby', { realHeader: true });
    try {
      await page.evaluate(() => {
        fixture.setTabs(['下载', '设置']);
        document.querySelector('.view').classList.add('page-withFullDrawer');
        window.backClicks = 0;
        document.getElementById('pageBack').addEventListener('click', () => backClicks++);
      });
      await tick(page);
      const check = async () => {
        const geometry = await page.evaluate(() => {
          const header = document.getElementById('globalHeader').getBoundingClientRect();
          const tab = fixture.tabs[1];
          const rect = tab.getBoundingClientRect();
          return { height: header.height, top: rect.top, bottom: rect.bottom, headerBottom: header.bottom,
            hit: tab.contains(document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2)),
            left: document.querySelector('.view').getBoundingClientRect().left };
        });
        assert(geometry.height > 0 && geometry.top >= 0 && geometry.bottom <= geometry.headerBottom && geometry.hit,
          `Original tabs must be painted and receive input inside the header: ${JSON.stringify(geometry)}`);
        assert.equal(geometry.left, 0, 'A hidden docked drawer must return its width to content');
        assert.equal(await visible(page, '#pageHeading'), true);
        assert.equal(await visible(page, '#globalMenu'), false);
        assert.equal(await visible(page, '#globalUser'), false);
        await page.locator('#tabsHost .emby-tab-button').nth(1).click();
        assert.equal(await page.locator('#panels .is-active').getAttribute('data-panel-index'), '1');
        await contentBelowHeader(page);
      };
      await check();
      await page.evaluate(() => document.getElementById('globalHeader').classList.add('headroom-hidden', 'headroom-scrolling'));
      await tick(page); await check();
      await page.locator('#pageBack').click();
      assert.equal(await page.evaluate(() => backClicks), 1);
      await page.screenshot({ path: path.join(output, 'emby-actual-header-wide.png') });
      await page.setViewportSize({ width: 360, height: 640 }); await tick(page); await check();
      await page.screenshot({ path: path.join(output, 'emby-actual-header-narrow.png') });
      await page.evaluate(() => { fixture.clear(); document.getElementById('pageHeading').textContent = '插件设置'; });
      await tick(page);
      assert.equal(await visible(page, '#pageHeading'), true, 'A page without tabs still owns its heading/actions');
      await page.evaluate(() => document.querySelector('.headerLeft').classList.add('hide'));
      await tick(page);
      assert.equal(await visible(page, '#globalHeader'), false, 'Upstream permissions also own title/actions');
      await page.evaluate(() => document.querySelector('.headerLeft').classList.remove('hide'));
      await tick(page);
      assert.equal(await visible(page, '#pageHelp'), true);
      const idleMutations = await page.evaluate(() => new Promise(resolve => {
        let count = 0;
        const observer = new MutationObserver(records => count += records.length);
        observer.observe(document.getElementById('globalHeader'), { attributes: true, attributeFilter: ['class'] });
        let frames = 0;
        function frame() {
          if (++frames < 8) requestAnimationFrame(frame);
          else { observer.disconnect(); resolve(count); }
        }
        requestAnimationFrame(frame);
      }));
      assert.equal(idleMutations, 0, 'An idle page must not rewrite header classes every animation frame');
    } finally { await page.close(); }
  });
  for (const product of ['emby', 'jellyfin']) await test(`${product}: long group headings and outlined inputs keep their content`, async () => {
    const page = await createPage(product, { width: 320 });
    try {
      await page.evaluate(() => {
        document.getElementById('pageContent').innerHTML = '<form class="dashboardGeneralForm"><div class="fieldsets"><fieldset id="settingCard"><legend id="longHeading">下载与转换设置：保留非常长的插件分组标题，窄窗口也要完整显示</legend><div class="fieldset-fields"><div class="fieldset-field"><label class="inputLabel" for="settingInput">下载保存目录</label><input class="emby-input" id="settingInput"><p class="fieldDescription">输入内容和功能说明应保持可见。</p></div></div></fieldset></div><div class="MuiOutlinedInput-root"><input aria-label="MUI input"><fieldset class="MuiOutlinedInput-notchedOutline" id="muiOutline"><legend><span>服务端地址</span></legend></fieldset></div></form>';
      });
      await tick(page);
      const geometry = await page.evaluate(() => {
        const title = document.getElementById('longHeading').getBoundingClientRect();
        const label = document.querySelector('.inputLabel').getBoundingClientRect();
        const input = document.getElementById('settingInput').getBoundingClientRect();
        const outline = getComputedStyle(document.getElementById('muiOutline'));
        return { titleBottom: title.bottom, labelTop: label.top, inputRight: input.right,
          viewport: innerWidth, overflow: document.documentElement.scrollWidth, outlinePadding: outline.paddingTop };
      });
      assert(geometry.titleBottom <= geometry.labelTop, `Wrapped group heading must not overlap the first field: ${JSON.stringify(geometry)}`);
      assert(geometry.inputRight <= geometry.viewport && geometry.overflow <= geometry.viewport + 1,
        `Nested settings padding must not push controls beyond the viewport: ${JSON.stringify(geometry)}`);
      assert.notEqual(geometry.outlinePadding, '48px', 'MUI outlined borders are not settings cards');
      await page.locator('#settingInput').fill('保留编辑中的目录');
      assert.equal(await page.locator('#settingInput').inputValue(), '保留编辑中的目录');
      await page.screenshot({ path: path.join(output, `${product}-settings-form-narrow.png`) });
    } finally { await page.close(); }
  });
  for (const product of ['emby', 'jellyfin']) {
    for (const options of product === 'emby' ? [{}, { nested: false }] : [{}, { legacy: true }, { legacy: true, absolute: true }]) {
      const label = product + (options.absolute ? '-legacy-absolute' : options.legacy ? '-legacy' : options.nested === false ? '-middle-sibling' : '-nested');
      await test(`${label}: parent visible, original nodes/clicks, global chrome hidden`, async () => {
        const page = await createPage(product, options);
        try {
          await page.evaluate(() => { fixture.setTabs(['转换', '设置', '转码', '色调映射']); window.originalList = fixture.list; window.originalTabs = fixture.tabs; });
          await tick(page);
          await page.screenshot({ path: path.join(output, `${label}-wide.png`) });
          await tabsVisible(page, [true, true, true, true]);
          await noGlobalChrome(page);
          assert.equal(await page.evaluate(() => fixture.list === originalList && fixture.tabs.every((tab, i) => tab === originalTabs[i])), true, 'The production script must not clone/reparent/replace tabs');
          await page.locator('#tabsHost .emby-tab-button, #tabsHost [role="tab"]').nth(3).click();
          assert.equal(await page.locator('#panels .is-active').getAttribute('data-panel-index'), '3', 'The existing tab handler must switch its panel');
          await contentBelowHeader(page);
          if (product === 'jellyfin' && !options.legacy) assert.equal(await visible(page, '#legacyMirror'), false, 'React must not reveal the hidden legacy mirror');
        } finally { await page.close(); }
      });
      await test(`${label}: SPA inserts/removes/reenters after retry timers, no-tab page has no header gap`, async () => {
        const page = await createPage(product, options);
        try {
          await noGlobalChrome(page);
          assert.equal(await visible(page, '#globalHeader'), false);
          await page.waitForTimeout(2800);
          await page.evaluate(() => fixture.setTabs(['新增插件页', '页面级设置']));
          await tick(page);
          await tabsVisible(page, [true, true]);
          await page.evaluate(() => fixture.clear());
          await tick(page);
          assert.equal(await visible(page, '#globalHeader'), false);
          assert.equal(await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--momoka-page-tabs-height').trim()), '0px');
          await page.evaluate(() => fixture.setTabs(['网络', '证书', '代理']));
          await tick(page);
          await tabsVisible(page, [true, true, true]);
          await noGlobalChrome(page);
          assert.equal(await page.evaluate(() => document.querySelectorAll('#tabsHost .tabs-viewmenubar, #tabsHost [role="tablist"]').length), 1);
        } finally { await page.close(); }
      });
      await test(`${label}: upstream per-tab hide, hidden, CSS and parent permissions survive and restore`, async () => {
        const page = await createPage(product, options);
        try {
          await page.evaluate(() => {
            fixture.setTabs(['可用', '未授权', '动态权限'], [1]);
            fixture.tabs[2].hidden = true;
          });
          await tick(page);
          await tabsVisible(page, [true, false, false]);
          await page.evaluate(() => { fixture.tabs[0].style.display = 'none'; });
          await tick(page);
          assert.equal(await visible(page, '#globalHeader'), false, 'All denied tabs must collapse the header');
          await page.evaluate(() => { fixture.tabs[2].hidden = false; });
          await tick(page);
          await tabsVisible(page, [false, false, true]);
          for (const method of ['hide-class', 'hidden-attribute', 'inline-display', 'stylesheet-display', 'stylesheet-visibility']) {
            await page.evaluate(method => {
              const host = document.getElementById('tabsHost');
              if (method === 'hide-class') host.classList.add('hide');
              if (method === 'hidden-attribute') host.hidden = true;
              if (method === 'inline-display') host.style.display = 'none';
              if (method.startsWith('stylesheet')) {
                const style = document.createElement('style'); style.id = 'permission-style';
                style.textContent = '#tabsHost { ' + (method.endsWith('visibility') ? 'visibility:hidden' : 'display:none') + ' !important; }';
                document.head.append(style);
              }
            }, method);
            await tick(page);
            await tabsVisible(page, [false, false, false]);
            assert.equal(await visible(page, '#globalHeader'), false, `${method} must remain authoritative`);
            await page.evaluate(() => {
              const host = document.getElementById('tabsHost'); host.classList.remove('hide'); host.hidden = false; host.style.removeProperty('display');
              document.getElementById('permission-style')?.remove();
            });
            await tick(page);
            await tabsVisible(page, [false, false, true]);
          }
          await page.evaluate(() => document.getElementById('globalHeader').classList.add('hide'));
          await tick(page);
          await tabsVisible(page, [false, false, false]);
          await page.evaluate(() => document.getElementById('globalHeader').classList.remove('hide'));
          await tick(page);
          await tabsVisible(page, [false, false, true]);
          if (product === 'emby') {
            await page.evaluate(() => document.documentElement.classList.add('hiddenViewMenuBar'));
            await tick(page); await tabsVisible(page, [false, false, false]);
            await page.evaluate(() => document.documentElement.classList.remove('hiddenViewMenuBar'));
            await tick(page); await tabsVisible(page, [false, false, true]);
          }
          await noGlobalChrome(page);
        } finally { await page.close(); }
      });
      await test(`${label}: 320px horizontal scroll and keyboard reach the last tab`, async () => {
        const page = await createPage(product, { ...options, width: 320 });
        try {
          await page.evaluate(() => fixture.setTabs(['下载', '设置', '转换', '转码', '色调映射', '高级插件选项']));
          await tick(page);
          await tabsVisible(page, [true, true, true, true, true, true]);
          const buttons = page.locator('#tabsHost .emby-tab-button, #tabsHost [role="tab"]');
          await buttons.first().focus();
          for (let i = 0; i < 5; i++) await page.keyboard.press(product === 'jellyfin' && !options.legacy ? 'ArrowRight' : 'Tab');
          assert.equal(await buttons.last().evaluate(node => document.activeElement === node), true, 'Keyboard must still reach the final tab');
          await page.keyboard.press('Enter');
          assert.equal(await page.locator('#panels .is-active').getAttribute('data-panel-index'), '5');
          const scroll = await page.evaluate(() => {
            const last = fixture.tabs.at(-1).getBoundingClientRect();
            const scroller = document.querySelector('#tabsHost .MuiTabs-scroller, #tabsHost .tabs-viewmenubar');
            return { left: last.left, right: last.right, width: innerWidth, overflow: scroller.scrollWidth > scroller.clientWidth, scrolled: scroller.scrollLeft };
          });
          assert(scroll.overflow && scroll.scrolled > 0, `The existing horizontal scroller must remain usable: ${JSON.stringify(scroll)}`);
          assert(scroll.left >= -1 && scroll.right <= scroll.width + 1, 'Keyboard-focused tab is clipped by its header parent');
          await contentBelowHeader(page);
          await noGlobalChrome(page);
          await page.screenshot({ path: path.join(output, `${label}-narrow.png`) });
          await page.setViewportSize({ width: 1920, height: 640 }); await tick(page); await contentBelowHeader(page);
          const firstTop = await buttons.first().evaluate(node => node.getBoundingClientRect().top);
          assert(firstTop >= 0, 'Legacy wide-layout negative headerTabs margin must not lift tabs above the viewport');
          await page.screenshot({ path: path.join(output, `${label}-large.png`) });
        } finally { await page.close(); }
      });
    }
  }
  await test('jellyfin: local AppBar and dialog backdrop are not global navigation', async () => {
    const page = await createPage('jellyfin');
    try {
      await page.evaluate(() => {
        fixture.setTabs(['媒体库', '显示']);
        document.getElementById('pageContent').insertAdjacentHTML('beforeend', '<div id="localToolbar" class="MuiPaper-root MuiAppBar-root"><button>页面操作</button></div><div role="dialog"><div id="dialogBackdrop" class="MuiBackdrop-root"></div></div>');
      });
      await tick(page); await tabsVisible(page, [true, true]);
      assert.equal(await visible(page, '#localToolbar'), true, 'Do not hide every MUI AppBar');
      assert.equal(await visible(page, '#dialogBackdrop'), true, 'Do not hide every MUI backdrop');
      await noGlobalChrome(page);
    } finally { await page.close(); }
  });
  await test('both: repeated production script injection is idempotent', async () => {
    for (const product of ['emby', 'jellyfin']) {
      const page = await createPage(product);
      try {
        await page.evaluate(() => fixture.setTabs(['下载', '设置'])); await tick(page);
        for (const script of payload.scripts[product]) await page.evaluate(script);
        await tick(page); await tabsVisible(page, [true, true]);
        assert.equal(await page.locator('#momoka-page-tabs').count(), 1);
        assert.equal(await page.evaluate(() => document.querySelectorAll('#tabsHost .tabs-viewmenubar, #tabsHost [role="tablist"]').length), 1);
        await noGlobalChrome(page);
      } finally { await page.close(); }
    }
  });
} finally {
  await context.close();
  const report = { coreHash: payload.coreHash, sources: payload.sources, browser: process.env.MOMOKA_BROWSER_EXE || 'msedge', browserVersion, fixtureHash, runnerHash, upstreamManagerHash: managerSource ? sha(managerSource) : null, networkRequests, pageErrors: errors, cases, passed: cases.filter(row => row.passed).length, failed: cases.filter(row => !row.passed).length, skipped: 0 };
  await fs.writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2) + '\n');
  console.log(`Total ${cases.length}: passed ${report.passed}, failed ${report.failed}, skipped 0; network requests ${networkRequests}; page errors ${errors.length}`);
  if (report.failed || networkRequests || errors.length) process.exitCode = 1;
}
