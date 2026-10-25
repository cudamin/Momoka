# Momoka 隐私政策

最后更新：2026-10-23

## 一句话

Momoka 是一个本地播放客户端。**开发者不收集遥测、使用统计或账号数据**；应用连接你配置的媒体服务、你选择的 Plex 账号服务、可关闭的发布页更新检查，以及你明确开启并配置凭据的弹弹play 弹幕服务。本机设置与凭据保存在你的电脑上；启用的第三方功能只向相应服务发送完成该功能所需的信息。

## 1. 适用范围

本政策适用于 Windows 桌面应用 Momoka（Microsoft Store 上架）。应用运行在你自己的设备上，配合你自己搭建或你已获授权使用的 Emby、Jellyfin、飞牛影视或 Plex 媒体服务器工作。

## 2. 开发者收集的数据

**没有。**

应用不包含遥测、使用统计、广告、崩溃上报或任何回传开发者服务器的代码，也不访问开发者运营的任何服务。

## 3. 应用在你设备上保存的数据

全部保存在本机，默认位置是 `%LOCALAPPDATA%\Momoka`：

| 内容 | 位置 | 说明 |
| --- | --- | --- |
| 服务器地址、账号名、界面与播放设置 | `settings.json` | 明文保存地址、账号名和各项设置 |
| 密码、访问令牌 | `settings.json` | **用 Windows 数据保护接口（DPAPI）加密**，只有本机当前 Windows 账户能解密；不保存明文 |
| 弹弹play AppId 与 AppSecret | `settings.json` | AppId 为公开应用标识；AppSecret 用 DPAPI 加密，不写日志或配置备份 |
| 设置备份 | `settings.backup.json` | 不含密码与令牌 |
| 日志 | `logs\` | 请求地址只记到「协议 + 主机 + 路径」，查询参数被去掉；密码与令牌不写日志 |
| 海报与背景图缓存 | `cache\images\` | 从你自己的服务器下载的图片 |
| 着色器编译缓存 | `cache\shaders\` | 本机生成 |
| 弹幕与搜索结果缓存 | `cache\danmaku\` | 从弹弹play 取得的结果，有效期两小时，最多保留 16 份 |
| 截图 | `screenshots\`（可在设置里改到别处） | 你在播放时主动截的图 |
| 下载的媒体文件 | 你指定的目录（默认「视频」文件夹） | 你主动发起的下载 |
| 网页控制台页的浏览数据 | `webview2\` | 内嵌 WebView2 打开你服务器自己的网页控制台时产生 |

应用还会读取本机已安装字体列表（用于字幕字体选择），仅在本机显示，不外传。

## 4. 网络连接

应用只与以下地址通信：

1. **你自己的 Emby / Jellyfin / 飞牛影视 / Plex 服务器** —— 你在应用里填的地址。用于登录、读取媒体库、上报播放进度、同步续播位置与「看过」标记。
2. **Plex 的账号服务 `plex.tv`**（只有你用 Plex 账号登录时才会连） —— Plex 服务器本身不校验账号密码，账号由 Plex 的账号服务认证，所以填了 Plex 服务器并按下登录时，应用会向 `https://plex.tv/api/v2/users/signin` 发一次请求，正文里是你的 Plex 账号与密码，请求头里是客户端名、应用版本号和本机的设备 ID。这是**由 Plex 运营的第三方服务**，不是开发者运营的，也不是你填的那个地址；不登录 Plex 就不会有任何请求发到那里。
3. **你自己配置的第三方片库管理服务**（可选功能） —— 你在应用里填的地址。用于搜索、订阅与整理。
4. **你的服务器在元数据里给出的图片地址** —— 海报与背景图可能由你的服务器指向第三方图片服务（例如 `image.tmdb.org`）。应用只是按服务器给出的地址取图，不自行决定去哪个第三方。
5. **本项目的发布页**（可关闭，默认开启） —— 应用在每次启动时会向本项目的发布页源（`github.com/cudamin/Momoka/releases.atom`）查询有没有比当前这一版新的版本；「设置 → 关于」里的「检查新版本」按钮也会在你按下的那一刻发一次同样的请求。这个请求**不携带任何身份信息**：请求头里只有客户端名与应用版本号，不含账号、令牌、设备 ID、计算机名或你的服务器地址。不想要这个检查，把「设置 → 关于」里的「启动时自动检查新版本」关掉即可 —— 关掉之后，应用不会再向该地址发出任何请求（那一格旁边的手动按钮仍会按你的意思发一次）。

6. **弹弹play 官方开放弹幕网络**（默认关闭，需填写 AppId 和 AppSecret）—— 开启后的自动匹配与手动搜索会向 `api.dandanplay.net` 发送作品名称与集数；读取弹幕发送弹幕库 ID 和关联弹幕选项。请求头是应用 ID、时间戳与签名，不直接传明文 AppSecret。官方弹幕接口可能跳转到 `dandanplay.net` 的 HTTPS 加速子域，跨来源不传鉴权头。不上传视频、媒体服务器地址、账号或访问令牌；没有启用或缺少凭据时不访问该服务。用户点击发送或在输入栏按回车时，会额外发送弹幕文字、该弹幕库中的时间、固定的白色滚动模式和应用昵称 Momoka，写入本应用的私有弹幕库；不自动重发。登录与播放历史接口没有接入。

发往媒体服务器的请求带有这些标识：客户端名 `Momoka`、**本机计算机名**、随机生成并保存在本机的设备 ID、应用版本号，以及你的访问令牌。这些只发给**你自己的服务器**，用途是让服务器认出「这是哪台设备」。

**开发者不会接收上述媒体服务凭据与使用数据。** 第 2、5、6 项是非媒体服务器地址：Plex 只在你选择登录时使用，更新源只带客户端名与版本号，弹弹play 只在明确开启并配置后使用，带作品查询与应用鉴权信息。数据由对应第三方服务处理，不是开发者运营的服务。关于页的 GitHub 点星与弹弹play 网站按钮仅在点击后交给默认浏览器打开。

## 5. 我们不做什么

- 不收集个人信息、使用统计或诊断数据
- 不含广告、内购或用户追踪
- 不向开发者回传任何数据
- 不读取与功能无关的文件

## 6. 儿童

应用不面向儿童，也不收集任何年龄相关信息。

## 7. 数据删除

- 卸载应用**不会**删除第 3 节列出的数据。
- 要彻底清除：删除 `%LOCALAPPDATA%\Momoka` 整个目录，以及你自己设置的截图目录与下载目录。
- 要撤销服务器上的登录：在你的媒体服务器上删除对应的设备或访问令牌；用 Plex 账号登录的，还要在 Plex 账号里撤销该设备的授权。

## 8. 第三方组件

应用内包含以下第三方组件，各自遵循其许可，随包附带许可与归属文本（`shaders\`、`mpv-ui\` 目录与 exe 根目录下，清单见 `THIRD-PARTY-NOTICES.md`）：mpv / libmpv（LGPL-2.1-or-later，静态链 LGPL-3.0 的 FFmpeg）、uosc（LGPL-2.1）、Material Icons Round 字体（Apache-2.0）、ArtCNN / CfL（MIT）、ravu 与 igv SSim 系列（LGPL-3.0-or-later）、an3223（LGPL-2.1-or-later）、igv adaptive-sharpen（BSD-2-Clause）、vulkan-1.dll（Apache-2.0），以及 Microsoft Windows App SDK 与 WebView2 Runtime。应用自身按 MIT 分发（见 README 的许可证一节）。

这些组件同样不收集数据，不改变本政策。

## 9. 政策变更

本政策如有修改，会更新本文件顶部的日期，并在项目仓库中留下记录。

## 10. 联系方式

问题或请求请通过项目仓库的议题页提出：<https://github.com/cudamin/Momoka/issues>

---

# Momoka Privacy Policy

Last updated: 2026-10-23

Momoka is a Windows desktop client for an Emby, Jellyfin, 飞牛影视 or Plex media server that you own or are authorized to use.

**The developer collects, uploads, and shares no data.** The app contains no telemetry, analytics, advertising, or crash reporting, and does not contact any service operated by the developer.

Everything the app stores stays on your machine, under `%LOCALAPPDATA%\Momoka`: your server address, account name and preferences (`settings.json`); your password, access token and optional DandanPlay AppSecret, encrypted with Windows DPAPI; logs without credentials; poster/backdrop and shader caches; screenshots; and WebView2 browsing data. DandanPlay's public AppId is stored as an application identifier, not a secret. Its search/comment cache lives in `cache/danmaku`, expires after two hours, and keeps at most 16 files. Preference backups contain neither its credentials nor its enabled connection state. The local font list is only displayed locally.

The app connects to these services, and the optional DandanPlay service described below: (1) your own Emby, Jellyfin, 飞牛影视 or Plex server, at the address you enter, to sign in, browse your library, report playback progress, and sync resume positions; (2) Plex's account service `plex.tv` — only when you sign in with a Plex account: a Plex server does not verify account credentials itself, the account is authenticated by Plex's account service, so when you enter a Plex server and press 登录 the app sends one request to `https://plex.tv/api/v2/users/signin` with your Plex account and password in the body and the client name, app version and this machine's device ID in its headers; that is a third-party service operated by Plex, not by the developer and not the address you entered, and no request goes there unless you choose to sign in with a Plex account; (3) a third-party library-management service that you configure yourself, if you enable that feature, at the address you enter; (4) image URLs that your own server supplies in its metadata (for example `image.tmdb.org`); and (5) the project's own releases page (on by default, can be turned off) — on each launch the app asks the project's releases feed (`github.com/cudamin/Momoka/releases.atom`) whether a newer version exists, and the 「检查新版本」 button in Settings → About sends the same request when you press it. That request carries **no identifying information**: its headers hold only the client name and the app version — no account, no token, no device ID, no machine name, no server address. To stop it, turn off 「启动时自动检查新版本」 in Settings → About; with that switch off the app sends nothing to that address (the manual button next to it still sends one request when you press it).

Requests to your media server carry the client name `Momoka`, this computer's machine name, a randomly generated device ID stored locally, the app version, and your access token — sent only to your own server, so it can identify this device. The update feed in item (5) carries none of those identifiers; item (2) is also not an address you entered, but it is only reached when you choose to sign in with a Plex account, and it carries the Plex credentials you type.

Uninstalling the app does not delete the data listed above; delete the `%LOCALAPPDATA%\Momoka` folder (and any screenshot or download folders you configured) to remove it. To revoke access, remove the corresponding device or token on your media server; for a Plex account, also revoke that device's access in your Plex account.

An additional optional destination is the official DandanPlay open comment network. It is off by default and requires an AppId and AppSecret. Once enabled, automatic matching and manual searches send a work title and episode number to `api.dandanplay.net`; fetching comments sends the comment-library ID and related-comment preference. Requests carry the application ID, timestamp and signature, not a plaintext AppSecret. Official HTTPS CDN redirects are allowed only within `dandanplay.net`; cross-origin requests carry no authentication headers. No video content, media-server address, media account or access token is sent. This is a third-party service, not a developer-operated endpoint. Posting is performed only when the user clicks Send or presses Enter in the comment input. It sends the text, comment-library time, fixed white scrolling mode and the application nickname Momoka to the application's private comment library, without automatic retries. DandanPlay login and playback-history APIs are not used. About-page website buttons open the default browser only when clicked.

Bundled third-party components — mpv / libmpv (LGPL-2.1-or-later, statically linked against an LGPL-3.0 FFmpeg), uosc (LGPL-2.1), the Material Icons Round font (Apache-2.0), the ArtCNN and CfL shaders (MIT), the ravu and igv SSim shaders (LGPL-3.0-or-later), the an3223 shaders (LGPL-2.1-or-later), igv's adaptive-sharpen (BSD-2-Clause), vulkan-1.dll (Apache-2.0), and the Microsoft Windows App SDK and WebView2 Runtime — follow their own licenses, with license and attribution texts shipped alongside the app (see `THIRD-PARTY-NOTICES.md`). mpv / libmpv is an LGPL build, which permits linking without the copyleft reaching this application; the application itself is distributed under MIT (see the license section of the README). They do not collect data.

Questions: <https://github.com/cudamin/Momoka/issues>
