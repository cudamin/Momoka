---
name: "momoka-fnnas-media-api"
description: "Momoka 接入飞牛影视（fnOS 影视 / trim.media）媒体服务器时的 REST 协议面：入口基址与 /v 前缀、v2 登录（SHA256 密码）与已废弃的 v1 明文登录、authx 签名（公开实现互相矛盾）、响应正文里的 code/msg/data 错误语义与 -15、访问码校验，以及媒体库、条目、季集、搜索、播放、进度上报、图片的接口清单与规格缺口。Use when 新增或审查飞牛影视 / fnOS / trimemedia 后端、编写 src/Momoka.Core/FnNas/ 下的 HTTP 客户端、排查飞牛影视登录失败或签名报错；Emby 协议走 momoka-emby-api，fnOS 系统 WebSocket API 不在本技能范围。"
---

# Momoka — 飞牛影视（fnOS / trim.media）REST 协议层

规则归 [CLAUDE.md](../../../CLAUDE.md)：授权、凭据、闸门与交付以它为准。本技能只记协议层本身容易误判的地方，不另立安全或发布政策。

**当前状态：Momoka 尚未接入飞牛影视。** 全仓没有一行对应源码，因此本技能记的不是"已经跑通的实现"，而是**接入前必须先弄清的事实**。其中的接口与字段主要来自第三方抓包整理和上游实现，**未在本机服务器实测**；凡未实测处都标了来源。把它当地图用，不要当验收报告。

协议分家：本技能只讲**飞牛影视媒体服务**（应用内名 `trim.media` / `trimemedia`，登录 `app_name` 为 `trimemedia-web`）。**fnOS 系统的 WebSocket/HTTP 管理接口（`ws://host:5666/websocket?type=main`、文件、存储、用户管理）是另一套**，不在本技能范围。Emby 那套走 [momoka-emby-api](../momoka-emby-api/SKILL.md)。

## 开始之前：先拿规格，别猜

飞牛影视**不发布公开规格**（没有 OpenAPI/Swagger 端点可拉）。现有资料按可信度排序：

1. **官方前端 JS（一手）**。媒体服务的 Web 前端在 `{入口}/v/assets/` 下，登录、签名、字段名都在里面。接入前**优先抓这一份**核对，它是唯一能证明"当前服务器版本就是这么做的"来源。
2. **本机服务器自证（一手，需授权）**。`GET {入口}/api/v1/sys/version` 返回 `{version, mediasrvVersion}`（如前端 `0.9.7` / 服务端 `0.8.35`）。**版本号是观测值不是常量**，接口会随版本变，用之前重取一次并记下版本。
3. **社区抓包文档（二手）**。`jimboo7339/fntv_danmu_all` 的 `doc/飞牛影视API文档.md`，基于 v0.9.7 / mediasrv 0.8.35 抓包，接口面较全。
4. **上游实现（二手，但是跑通的）**。MoviePilot 的 `app/modules/trimemedia/`（`api.py` + `trimemedia.py`）是长期维护、带回归测试的生产实现，可交叉验证；注意它是**用一套硬编码应用密钥**签名的（见下）。

**这三者互相矛盾的地方，不要挑一个当权威**——具体分歧记在 [传输与鉴权](references/http-and-auth.md)，接入前用第 1、2 条定案。

## 地形（接入后的落点，与 Emby 侧对照）

目前没有代码，下表是**建议的落点**，照着 `src/Momoka.Core/Emby/` 的形状摆，别把两套协议混进同一个类：

| 建议文件 | 管什么 | Emby 侧对应 |
| --- | --- | --- |
| `src/Momoka.Core/FnNas/FnNasHttp.cs` | 唯一发 HTTP 的地方：逐请求头、超时、错误翻译、下载 | `EmbyHttp.cs` |
| `src/Momoka.Core/FnNas/FnNasApiException.cs` | 异常层次 | `EmbyApiException.cs` |
| `src/Momoka.Core/FnNas/FnNasEndpoint.cs` | 用户输入 → `ApiBase`（**恒以 `/v/` 结尾**） | `EmbyServerAddress.cs` |
| `src/Momoka.Core/FnNas/FnNasSigner.cs` | authx 签名的纯函数 | 无（Emby 不需要签名） |
| `src/Momoka.Core/FnNas/FnNasClient.cs` + 分部类 | 按领域分组的调用 | `EmbyClient.cs` |

把签名做成**纯函数并用测试钉死**：它是这套协议里最容易悄悄错、又最难从现象定位的一块（错签名的报错和错令牌很像）。

## 五件反直觉的事

### 1. v1 登录已废弃，`-15` 分不出是账号错还是密码错

新版服务端（抓包侧 0.8.37 起）废弃了 v1 明文登录：

| | v1（旧） | v2（现行） |
| --- | --- | --- |
| 路径 | `POST {base}/api/v1/login` | `POST {base}/api/v2/user/loginByPassword` |
| `password` | **明文** | **`sha256(明文)` 的小写十六进制** |
| 其余字段 | `username`、`app_name: "trimemedia-web"` | 同左 |
| 成功 | `data.token` | `data.token` |

- 新服务端上 v1 **对所有请求一律返回 `code: -15` / `Password Incorrect`**，连不存在的用户、空密码也一样 —— 所以拿到 `-15` **不能**断言"账号密码错"，也可能是"打到了已废弃的 v1"。
- v2 拿到的 token 可直接用于全部 v1 业务接口，**其余接口不用跟着迁移**。
- 上游 MoviePilot 的做法：**先 v2；v2 整体不可用（HTTP 层失败）才回退 v1；v2 返回业务错误码则直接失败、不回退**（新服务端回退也没用）。接入时照抄这个判定，别写成"v1 失败就再试 v1"。

### 2. authx 签名：公开资料给了两套互相矛盾的公式

所有请求都要带 `authx` 头，形状一致：

```
authx: nonce={6位随机数字}&timestamp={毫秒时间戳}&sign={小写十六进制MD5}
```

但 `sign` 怎么算，两份来源**完全不同**：

| 来源 | 公式 |
| --- | --- |
| 社区抓包文档 | `MD5(nonce + timestamp + token)` |
| MoviePilot 生产实现 | `MD5(APP_SECRET + "_" + api_path + "_" + nonce + "_" + timestamp + "_" + MD5(body或查询串) + "_" + APP_KEY)` |

后者把 `api_path`（含 `/v` 前缀的完整路径）、请求体摘要和一个**硬编码应用密钥**都揉进了签名。两者不可能同时对同一版本成立。

**不要在这上面猜。** 定案办法：读官方前端 `{入口}/v/assets/` 里的签名代码，或对目标服务器做一次最小登录 + 一次带签名的只读请求验证。在定案之前，把它当未决项写进代码注释和 PROGRESS.md，而不是先写一套再说。公式细节、常量与两种 GET/POST 摘要取法见 [传输与鉴权](references/http-and-auth.md)。

### 3. 错误码在正文里，HTTP 常常还是 200

响应统一信封：

```json
{ "code": 0, "msg": "", "data": { } }
```

- `code == 0` 才算成功；非 0 是业务错误（`-15` 密码错、等）。
- **HTTP 状态码基本不参与判错**——上游实现直接解析 JSON 的 `code`，`msg` 只用于日志。所以照 Emby 那套"按 401/403/404 翻异常"的写法在这里对不上号，要另建一套：**先看 HTTP 是否成功，再看正文 `code`**。
- 别把 `msg` 原样抛给界面：它可能回显请求内容，也可能就是空串。

### 4. 业务入口在 `/v` 前缀下的 `/api/v1`，登录是例外

- 所有业务接口是 `{入口}/api/v1/...`，而**入口本身带 `/v`**（例如 `http://host:5666/v`）。合成后形如 `http://host:5666/v/api/v1/mediadb/list`。
- 登录是唯一走 **`/api/v2`** 的：`{入口}/api/v2/user/loginByPassword`。
- 上游按"先试补 `/v`、再试原样"来兼容用户填的地址。归一化时把 `/v` 当**入口的一部分**（像 Emby 的 `/emby/`），别把它当成业务路径前缀再拼一遍，否则会拼出 `/v/v/api/...`。

### 5. 访问码校验在 `/c/{code}`，且不在 `/v` 下

服务端可以开启"访问码"（访问口令）。开启后**未通过校验连登录接口都够不着**：

```
GET {根}/c/{访问码}      # 根 = 入口去掉结尾的 /v；校验通过后靠会话 Cookie 记住
```

- 校验失败/码错时返回 **404**（不是 401）。
- 这是一个**会话 Cookie**，不是令牌，别和 `Authorization` 混在一起设计。
- 未开启访问码时跳过此步。接入时把它做成一个可选配置项，而不是必填。

## 别自己发明的地方

- **入口只在一处归一。** 定一个 `FnNasEndpoint.Normalize`，把用户输入统一成**恒以 `/v/` 结尾**的 `ApiBase`（`…/v` 先剥再拼回），业务路径相对它拼。别在每个调用点各自处理 `/v`。
- **令牌走头，签名密钥不是用户凭据。** `Authorization: <token>` 带令牌；`authx` 的签名用的是**应用级常量**（上游硬编码），与账号密码无关。两者都不得进 URL、启动参数或日志 —— 遵守 CLAUDE.md 的凭据规则，日志 URL 一律剥掉 query。
- **图片要带鉴权头，不能当普通静态图交给播放器。** 图片走 `{入口}/api/v1/sys/img{poster_path}`（可加 `?w=256` 等尺寸参数），**需要 `authx`/令牌**。所以不能像公共图片 URL 那样直接塞给 mpv 或外部浏览器 —— 要么经进程内 API 取字节，要么走已验证的进程间通道（与 Emby 侧同一套边界，见 [播放技能](../momoka-playback/SKILL.md)）。
- **guid 拼进路径前要过守卫。** `season/list/{guid}`、`item/{guid}` 这类把 id 直接拼进路径的写法，照抄 Emby 侧的 `LibraryId` / `UserSegment`：拒 `.`、`..` 和含 `/ \ ? #` 的 id，再 `Uri.EscapeDataString`。
- **播放地址有两种来源，别混。** 服务端 `POST /api/v1/stream` 返回 `qualities`（本机/转码流）与 `direct_link_qualities`（云盘直链，`url` 是外部网盘地址）。直链是**外部来源**，播放前要按 CLAUDE.md 的来源边界处理，不能默认可信。字段见 [接口清单](references/endpoints.md)。

细节：[传输与鉴权](references/http-and-auth.md)、[接口清单](references/endpoints.md)。

## 验证

接入后（现在还没有可跑的东西）：

- 新增或修改请求路径时，先 `GET {入口}/api/v1/sys/version` 记版本，再抓官方前端 JS 核对路径与字段；对不上就登记进 [接口清单](references/endpoints.md) 的缺口/矛盾表，而不是改成"猜的写法"。
- `FnNasSigner` 这类纯函数必须有单测（含 GET 与 POST 两种摘要、空正文、非 ASCII 路径），因为签名错误的现象和令牌错误几乎一样。
- 活服务器核对**先只读**（`sys/version`、`mediadb/list`）；登录、播放、进度上报、扫描、删除等有副作用的操作按 CLAUDE.md 的授权档位执行，不因为"只是查一下"就跳过授权。
- 报告实测、未覆盖与失败项；不把上游实现的行为当成"本机服务器已验证"，不把历史失败当通过。

## 交付

- 接入过程中每弄清一条事实（尤其第 1、2 条的两个悬案），回写本技能与 [传输与鉴权](references/http-and-auth.md)，并在 PROGRESS.md 记一行：版本、来源、实测与否。
- 本技能与 [momoka-emby-api](../momoka-emby-api/SKILL.md) 平级：Emby 与飞牛影视是**两套协议**，共享的是分层与凭据边界，不是请求形状。别把飞牛影视的路径塞进 `EmbyClient`。
- 新建或改动本技能时按 [momoka-skill-creator](../momoka-skill-creator/SKILL.md) 走：登记 CLAUDE.md 清单、跑结构检查。
