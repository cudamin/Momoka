# 内置凭据端点

客户端常量在 `src/Momoka.Core/Danmaku/DandanPlayBuiltIn.cs`，解密与缓存实现在 `DandanPlayBuiltInCredentialSource.cs`。端点部署在独立仓库 `cudamin/Mmoka-web-cloudflare-`，自定义域 `yuanshenqidon.dpdns.org`；2026-10-26 提交 `438ebfc` 已推送，生产端点实测 200。

## 角色与边界

- 端点只下发 Momoka 应用的 AppId/AppSecret，不代理搜索、弹幕读取或发送；真正的请求仍由客户端直连 `api.dandanplay.net`。
- 它把 AppSecret 从公开仓库与安装包里移到 Cloudflare Secret，并允许轮换；**不提供保密性**。AES key 随客户端分发，愿意提取的人仍能解出 AppSecret。
- 所有用户共享同一份应用额度；端点本身不计量、不签发用户级密钥、不做防滥用代理。
- 端点只应看到请求时间、IP、`Momoka/<version>` User-Agent 和标准 HTTP 头；不接收作品名、集数、媒体服务器信息、账号或弹幕内容。

## 四个 Secret

在 Cloudflare 项目的**生产环境**配置，四条缺一就会返回 503：

| 名称 | 内容 |
| --- | --- |
| `DANDANPLAY_APP_ID` | 弹弹play 应用 ID（当前公开值 `dav282msq5`，以开发者中心为准） |
| `DANDANPLAY_APP_SECRET` | 开发者中心的 AppSecret；只填进 Cloudflare，绝不入仓库、聊天或日志 |
| `DANDANPLAY_CREDENTIAL_KEY` | 32 字节 AES-GCM key 的 Base64；必须与 `DandanPlayBuiltIn.Key` 完全一致 |
| `DANDANPLAY_CREDENTIAL_KID` | 密钥标识，用于缓存隔离与诊断，例如 `momoka-2026-10` |

`DANDANPLAY_CREDENTIAL_KEY` 不是可以单独轮换的服务器秘密：客户端把它编译在常量里。换 key 必须同步发布客户端新版；只改 Worker 会让所有旧客户端解密失败。

## 部署形态

仓库同时提供两个入口，共用 `worker/credential.js`：

- Workers 入口：`worker/index.js` 路由 `GET /api/danmaku/credential`。
- Pages Function：`functions/api/danmaku/credential.js`。

当前项目是带静态资产的 Worker，绑定里只有 `ASSETS`；`/api/danmaku/credential` 走 Worker 路由，静态资源回退到 `ASSETS`。只接受 GET，其他方法返回 405。

Cloudflare 控制台路径：Worker 详情 → 设置 → 变量和密钥 → 添加 → 类型「密钥」→ 填名称和值 → **添加变量并部署**。中文界面里左边的「密钥」输入框是变量名，值右侧的小「密钥」勾选框才是按密文保存。四条都放生产环境；预览环境不是自定义域的落点。

## 信封与加密

成功响应：

```json
{ "v": 1, "kid": "momoka-2026-10", "exp": 1793602871, "data": "<base64>" }
```

- `exp` 是 Unix 秒，Worker 默认发 7 天有效期。
- `data` = 12 字节随机 nonce + AES-256-GCM 密文 + 16 字节 tag。
- 明文载荷：`{ "appId": "...", "appSecret": "...", "exp": <与信封相同> }`。
- AAD：`momoka-danmaku-credential-v1|<kid>|<exp>`，UTF-8。
- 客户端上限：响应 64 KiB、30 秒；`exp` 必须在当前时间 1 分钟到 30 天之间；载荷 `exp` 必须与信封一致；`appId`、`appSecret`、`kid` 不得含控制字符。
- 缺任一 Secret 时返回 503 `{"error":"credential endpoint is not configured"}`，响应带 `cache-control: no-store`。

## 客户端缓存与失败回退

- 缓存文件：`<app paths>/cache/danmaku/builtin-credential.bin`，DPAPI 加密，内容含 `kid`、`exp`、`refreshAt`、`appId`、`appSecret`。
- 内存与磁盘都缓存；`refreshAt = exp - 1 天`（剩余不足 1 天时取剩余时间的一半，最少 1 秒）。**`refreshAt` 必须持久化**，否则每次从当前时间重算会让旧缓存永远不刷新。
- 端点失败且缓存未过期：继续用缓存，避免一次网络抖动打断播放；缓存过期则把错误交给用户。
- 弹弹play 返回 401/403：GET 丢弃缓存、重取一份再试一次；POST 不自动重试，提示用户再发送一次。
- `Invalidate()` 同时清内存与磁盘，下一次 `GetAsync` 重新取。

## 轮换矩阵

| 变更 | 操作 | 客户端后果 |
| --- | --- | --- |
| 换 AppSecret / AppId | 改 Cloudflare Secret 并部署 | 旧缓存下一次 401/403 时被丢弃并重取；最多等到用旧凭据的下一次调用 |
| 换 `kid` | 改 Secret 并部署 | 只影响新取到的凭据的缓存隔离与诊断；旧凭据未失效前仍可用 |
| 换加密 key | 改 Worker + 改 `DandanPlayBuiltIn.Key` + 发新版客户端 | 旧客户端无法解密新响应；不能只改服务器 |
| 下线端点 | 移除路由或返回 503 | 未过期缓存继续用；过期后内置模式不可用，用户可改用自定义凭据 |

## 离线回归

在官网仓库根目录运行：

```bash
node --test tests/danmaku-credential.test.mjs
```

当前 5 项：未配置环境变量时拒绝、错误密钥无法解密、Worker 只接受 GET 并回退 ASSETS、Pages Function 接受 GET 并拒绝其他方法，以及正常下发。它不是线上部署证明，线上仍需下面的状态与解密检查。

## 在线验证

只看 HTTP 状态：

```bash
curl -sS -o /dev/null -w '%{http_code}\n' --max-time 30 \
  https://yuanshenqidon.dpdns.org/api/danmaku/credential
```

解密校验（不打印密钥，保存为临时 `.mjs` 后运行）：

```js
import fs from 'node:fs';
import crypto from 'node:crypto';

const source = fs.readFileSync('src/Momoka.Core/Danmaku/DandanPlayBuiltIn.cs', 'utf8');
const key = Buffer.from(source.match(/Key\s*=\s*"([^"]+)"/)[1], 'base64');
const response = await (await fetch('https://yuanshenqidon.dpdns.org/api/danmaku/credential')).json();
const data = Buffer.from(response.data, 'base64');
const decipher = crypto.createDecipheriv('aes-256-gcm', key, data.subarray(0, 12));
decipher.setAAD(Buffer.from(`momoka-danmaku-credential-v1|${response.kid}|${response.exp}`));
decipher.setAuthTag(data.subarray(-16));
const plain = JSON.parse(Buffer.concat([
  decipher.update(data.subarray(12, -16)), decipher.final(),
]).toString('utf8'));

console.log({
  v: response.v, kid: response.kid, exp: response.exp,
  appId: plain.appId, appIdExpected: plain.appId === 'dav282msq5',
  secretLength: plain.appSecret.length,
  expMatches: plain.exp === response.exp,
  notExpired: response.exp > Date.now() / 1000,
});
```

预期：`v=1`、`appIdExpected=true`、`secretLength>0`、`expMatches=true`、`notExpired=true`。任何一项不对，按下面的症状排查。

## 排查

| 症状 | 原因 | 处理 |
| --- | --- | --- |
| 404 | 路由未部署或域名指错项目 | 检查 Worker/Pages 部署与自定义域 |
| 503 `not configured` | 四个 Secret 缺一条，或只配了预览环境 | 配到生产环境并重新部署 |
| 200 但客户端“无法解密” | key 与客户端常量不一致、AAD 字段不一致、`exp` 超范围 | 对齐 `DANDANPLAY_CREDENTIAL_KEY`、`kid`、`exp`；换 key 要发客户端 |
| 200 但弹弹play 401/403 | AppId/AppSecret 错、应用未上线、密钥被重置 | 开发者中心核对应用状态与密钥 |
| 429 | 共享额度/频率耗尽 | 等功能恢复或改用自定义凭据；不要循环重试 |
| 间歇连接超时 | 本机到自定义域的链路抖动 | 重试一次；客户端有 30 秒上限与未过期缓存兜底，不据此判定端点故障 |

## 安全与提交

- AppSecret 只能通过 Cloudflare Secret 或 Wrangler secret 配置；不进入 `.env`、仓库、构建产物、聊天记录或命令历史。
- 不把 AppId/Secret 拼进 URL；客户端只发无查询参数的 GET，Worker 只接受 GET。
- 验证输出只显示 `appId`、密钥长度、`kid`、`exp` 与布尔断言，不打印密钥。
- 修改端点协议时，同时更新 `worker/credential.js`、客户端解析、跨语言固定向量测试与本文档；只改一侧会出现线上 200 但客户端全部失败。
