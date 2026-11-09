# 弹弹play内置凭据端点

返回 [弹幕技能](../SKILL.md)。本页只负责弹弹play应用凭据；自部署访问令牌见 [danmu_api](danmu-api.md)。

客户端参数在 `src/Momoka.Core/Danmaku/DandanPlayBuiltIn.cs`，解密与缓存位于 `DandanPlayBuiltInCredentialSource.cs`。本仓已跟踪 `website/worker/credential.js`、`website/worker/index.js` 和 `website/worker/credential.test.mjs`，可以用于干净检出的离线验证。生产域名从 `DandanPlayBuiltIn.Endpoint` 读取；历史部署仓库 `cudamin/Mmoka-web-cloudflare-` 和过去的 HTTP 200 记录都不能证明当前部署版本与本树一致。

## 角色与边界

- 端点只下发 Momoka 应用的 AppId/AppSecret，不代理搜索、弹幕读取或发送；客户端继续直连官方来源。
- AppSecret 配在 Cloudflare Secret，AES key 随客户端分发，所以密文分发不提供密钥保密保证；所有内置用户共享应用额度。
- 客户端使用无查询参数的 GET，不发送作品名、集数、媒体服务器信息或评论；Worker 按路径和方法路由，并未禁止查询串。网络侧仍可看到时间、IP、User-Agent 和标准 HTTP 头。
- 设置页没有用户自己的 AppId/AppSecret。端点不可用或额度受限时等待恢复；已配置的 danmu_api 是独立来源，其令牌不代替官方应用凭据。

## Worker 与四个 Secret

`website/worker/index.js` 将 `GET /api/danmaku/credential` 交给 `credentialResponse`；该路径其他方法返回 405，其余请求回退 `env.ASSETS.fetch`。当前本仓没有 Pages Function 入口，不沿用历史 Pages 文件路径作为依赖。

| 名称 | 内容 |
| --- | --- |
| `DANDANPLAY_APP_ID` | 开发者中心分配的应用 ID |
| `DANDANPLAY_APP_SECRET` | 应用密钥，只写入部署平台 Secret |
| `DANDANPLAY_CREDENTIAL_KEY` | 32 字节 AES-GCM key 的 Base64，须与 `DandanPlayBuiltIn.Key` 一致 |
| `DANDANPLAY_CREDENTIAL_KID` | 密钥版本标识，用于信封、缓存身份与诊断 |

四项任一缺失或空白时返回 503。检查实际域名绑定的生产环境；只修改预览环境不等于生产生效。核对 key 是否一致时只输出布尔值，不打印 Secret。

## 信封与缓存

成功响应形状：

```json
{ "v": 1, "kid": "fixture-kid", "exp": 1793602871, "data": "<base64>" }
```

例中的时间仅说明格式。`credentialResponse` 默认签发当前时间起七天有效期：

- `data` = 12 字节 nonce + AES-256-GCM 密文 + 16 字节 tag。
- 明文为 `{appId, appSecret, exp}`；AAD 是 UTF-8 的 `momoka-danmaku-credential-v1|kid|exp`。
- 客户端校验版本、kid、信封和载荷的 exp 一致性、字段内容；响应上限 64 KiB、预算 30 秒。新响应有效期须在当前时间一分钟到三十天之间。
- 缓存写入 `<app paths>/cache/danmaku/builtin-credential.bin`，用 DPAPI 保护；内存同样保留凭据。
- `refreshAt` 随缓存持久化：到期前一天换新，剩余不足一天时取剩余时间的一半、最少一秒。每次读盘都从“现在”重算会推迟刷新，必须保留这个边界。
- 端点暂时失败且缓存未到期时继续用缓存；过期后报告错误。`Invalidate()` 同时清内存和磁盘。
- 官方 GET 返回 401/403 时使旧凭据失效、重取后最多重试一次；发送 POST 只使凭据失效，不自动重复写入。

## 轮换与排查

| 变更或症状 | 操作和后果 |
| --- | --- |
| 换 AppId/AppSecret | 更新生产 Secret 并部署；旧凭据下一次被官方拒绝时刷新，或到正常刷新时间换新 |
| 换 kid | 新信封与新缓存身份变化；不会远程立即清除所有旧缓存 |
| 换 AES key | 同步 Worker 配置、客户端常量、测试向量并发客户端新版；旧客户端不能解密新信封 |
| 端点下线/503 | 未过期缓存可继续用；检查四项 Secret 与生产部署，过期后等待恢复 |
| 404 | 核对当前端点常量、域名绑定和 Worker 路由 |
| HTTP 200 但解密失败 | 核对 key、AAD、kid、exp、信封版本与载荷；HTTP 状态不能代替解密验证 |
| 下发成功但官方 401/403 | 核对应用状态、密钥轮换、签名路径与系统时间 |
| 官方 429 | 共享额度或频率受限；等待恢复，不循环探测或恢复已退役的自有应用凭据入口 |

## 验证

在当前 Momoka 仓库根目录运行：

```powershell
node --test website/worker/credential.test.mjs
```

测试使用固定假凭据，覆盖加解密、AAD 绑定、未配置、错误 key 与 Worker 路由。与 `DanmakuTests` 的客户端跨语言向量一起维护；以实际测试结果为准，不固定历史测试数量。

确需验证线上时，先按 CLAUDE.md 确定授权范围，再使用客户端相同解析规则核对状态、版本、期限、kid 与解密结果。只输出应用 ID、密钥长度、有效期和布尔判据，禁止输出 AppSecret、key 或完整明文载荷。离线 Worker 测试不证明已部署，历史线上成功不证明当前可用。
