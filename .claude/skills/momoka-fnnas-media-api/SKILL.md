---
name: "momoka-fnnas-media-api"
description: "Develop or review Momoka's 飞牛影视 / fnOS media backend: Core/FnNas transport, authx, login, media mapping and playback, plus the native account and server-settings pages shown in exclusive mode. Use when changing FnNasClient, FnNasMediaAdapter, FnNasPasswordViewModel / FnNasPreferencesViewModel, FnNasLibrariesPage / FnNasUsersPage / FnNasSettingsPage / FnNasTasksPage, or diagnosing trimemedia login and signatures. Excludes the fnOS system WebSocket API; shared playback and shell lifetimes belong to their domain skills."
---

# Momoka — 飞牛影视媒体协议

项目规则以 [CLAUDE.md](../../../CLAUDE.md) 为准。这里只管理飞牛影视媒体服务（`trim.media` / `trimemedia`），不包含 fnOS 的系统用户、文件管理和 WebSocket 管理接口。

## 证据与版本

2026-10-15 只读核对用户指定服务器：网页 **0.9.8**、媒体服务 **0.8.43**，读取官方前端及网页设置的四个管理分区，没有修改真实服务器或播放媒体。2026-10-19 重新读取同一版官方前端，确认设置抽屉是「帐号」（修改密码、播放偏好、外观）与「影视服务器」（媒体库、用户、设置、任务计划）两组，并在真实服务器上只读打开七项页面核对渲染与返回。版本是观测值，不是兼容范围承诺；离线测试不证明真实播放或真实写入。

新部署先读 `/v/` HTML 引用的资源，再读 `sys/version`。本次官方资源：SDK/签名 `14a77ac952fc83b785b65592c335e16d-BGXwY7Sz.js`，库编辑/播放 `74c95604043427f0bee1d0e16bfa53af-DZn1lJtt.js`，用户 `7dfb4cf67742cb0660305e56ef816c53-BJTi2E5L.js`，服务器设置 `b3eacd33433b31b5252351032c9b3e7a-DRui1aGd.js`，任务计划 `0ebb429fa86d481c2630fac53db1c91c-Bc6lVx_J.js`。这些仅用于追溯，不能硬编码为下载地址。社区文档和 MoviePilot 只能补充线索，不能覆盖当前官方实现。

## 实现归属

`src/Momoka.Core/FnNas/`：`FnNasEndpoint` 管入口和路径段，`FnNasSigner` 管纯签名/密码摘要，`FnNasHttp` 是唯一 HTTP 边界，`FnNasClient` 管路径和字段。共享目录/详情模型和会话通过适配层接入，不把飞牛 API 路径塞进 Emby 请求逻辑。

条目「更多」菜单的写入面（2026-10-19）：`FnNasItemActions` 拼刷新、删除、手动匹配的正文，`FnNasMetadataEdit` 管编辑元数据四格，`FnNasClient` 管 `media/itemfile`、`item/refresh`、`item/getEditDetail`／`saveEditDetail`、`scrap/*` 与带 `media_guids` 的删除。协议取自官方前端 0.9.8；这些调用的真实写入没有在用户服务器上执行过。

会话/DI/显隐见 [momoka-winui-shell](../momoka-winui-shell/SKILL.md)；播放请求、内置与外部后端和管线见 [momoka-playback](../momoka-playback/SKILL.md)。

## 登录与签名

入口统一以 `/v/` 结尾；粘贴 `/v/login?redirect_uri=…` 时剥除页面和查询。现行登录 `POST api/v2/user/loginByPassword` 发送 `username`、`password = SHA256(明文)` 小写十六进制、`app_name = trimemedia-web`。返回 token 可用于 v1 业务接口；密码错误不能回退废弃的 v1 明文登录。

官方 authx 已定案：`nonce=<六位数字>&timestamp=<毫秒>&sign=<小写 MD5>`；签名是 `MD5(secret + "_" + pathname + "_" + nonce + "_" + timestamp + "_" + MD5(data) + "_" + appKey)`，不含用户 token。常量是前端公开的应用身份，不是用户凭据。

- GET 遍历 `URLSearchParams`，先跳过值为字面量 `undefined`/`null` 的项，再按 key 存储（后值覆盖），key 排序、重建查询并解码后取摘要。`?a=old&a=null` 仍是 `a=old`。
- POST/PUT/DELETE 对实际发送的 JSON 原文取摘要；空正文与 `{}` 不等价。pathname 包括部署前缀和 `/v`。覆盖非 ASCII、空白、加号、重复 key 和空正文测试。
- 官方 SDK 对 `/sys/img/`、`/img/`、`/concat/`、`/media/` 前缀豁免签名；原始 `media/range` 仍使用 Authorization，免签名不等于免身份。

细节及历史分歧见 [传输与鉴权](references/http-and-auth.md)。

## 响应与凭据

先核 HTTP，再核 `{code,msg,data}` 的 `code == 0`；HTTP 200 不证明业务成功。data 可能是对象、列表或标量。错误文案由客户端生成，不把可能回显凭据的 msg 原样记录或展示。令牌只走 Authorization；图片进程内下载，播放器头走现有安全传递路径；重定向核来源。访问码 Cookie 与 token 分开，`/c/{code}` 在设备根路径，不在 `/v/` 下。

## 原生设置目录（2026-10-16 曾退役，2026-10-19 接回）

飞牛影视独占时，设置左栏照官方 0.9.8 设置抽屉分成两组：

- **帐号**：修改密码、播放偏好、外观。对所有已登录的飞牛账号可见。
  - 修改密码：`POST /api/v2/user/passwd`，正文 `{password: sha256(明文)}`，`data` 是新令牌。成功后必须同时替换 `EmbySession` 里这条来源的令牌和已保存密码（`ApplyPasswordChange`），否则下一次自动重登会拿旧密码。
  - 播放偏好：读 `GET /api/v1/user/info` 的 `lan` / `audio_lan` 和 `GET /api/v1/sys/config` 的 `supported_languages`；保存 `POST /api/v1/user/info`，正文只含 `{lan, audio_lan}`，空串表示任意语言。
  - 外观：不新增网页外观页，落到 Momoka 自己的「界面」卡（配色、动效、侧边栏等）。
- **影视服务器**：媒体库、用户、设置、任务计划。只对 `is_admin == 1` 的账号显示；四页是 WinUI 原生页，不是网页内嵌。
  - 媒体库：`mdb/*` 与管理端目录浏览；页面还包含 IPTV 源，但没有恢复网盘/直链。
  - 用户：`manager/user/*` 与默认媒体库权限。
  - 设置：服务器名称、语言、元数据/缓存存储空间、文件监控、搜索索引重建；保存时保留未知字段。**不要**恢复网盘直链开关、GPU 转码或流解析。
  - 任务计划：`task/schedule/*` 与每日开关。

同时连接多台服务器、或界面风味是通用时，两组一起收起。`--show-settings` 在飞牛独占时接受这七个页名；服务器四项仍要求管理员。左栏选择、身份刷新和页面缓存归 [momoka-winui-shell](../momoka-winui-shell/SKILL.md)。

浏览与播放仍按 [momoka-verification](../momoka-verification/SKILL.md) 验证协议、错误、重定向、身份/取消、分页和播放进度。区分只读服务器证据、离线测试和真实播放/写入覆盖。

协议变化记录版本与一手来源；维护本技能按 [momoka-skill-creator](../momoka-skill-creator/SKILL.md) 做结构/事实检查。不得将本机地址、账号、Cookie 或 token 写入仓库。
