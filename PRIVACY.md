# Momoka 隐私政策

最后更新：2026-11-26

## 一句话

Momoka 是一个本地播放客户端。**开发者不收集遥测、使用统计或账号数据**；应用连接你配置的媒体服务、你选择的 Plex 账号服务、可关闭的发布页更新检查、你明确开启的弹幕与字幕翻译服务，以及为弹弹play 下发内置凭据的 Momoka 端点。本机设置与凭据保存在你的电脑上；启用的第三方功能会向相应服务发送完成该功能所需的信息。

## 1. 适用范围

本政策适用于 Windows 桌面应用 Momoka（Microsoft Store 上架）。应用运行在你自己的设备上，配合你自己搭建或你已获授权使用的 Emby、Jellyfin、飞牛影视或 Plex 媒体服务器工作。

## 2. 开发者收集的数据

**没有。**

应用不包含遥测、使用统计、广告、崩溃上报或账号回传代码。除第 4 节第 7 项的内置弹幕凭据端点外，应用不访问开发者运营的服务；该端点只返回应用凭据，不接收作品名称、集数、播放记录、媒体服务器地址或弹幕内容。

## 3. 应用在你设备上保存的数据

全部保存在本机，默认位置是 `%LOCALAPPDATA%\Momoka`：

| 内容 | 位置 | 说明 |
| --- | --- | --- |
| 服务器地址、账号名、界面与播放设置 | `settings.json` | 明文保存地址、账号名和各项设置 |
| 密码、访问令牌 | `settings.json` | **用 Windows 数据保护接口（DPAPI）加密**，只有本机当前 Windows 账户能解密；不保存明文 |
| 字幕翻译 API 密钥 | `settings.json` | Windows DPAPI 加密，只在本机当前用户下解密；不回填、不写日志或偏好备份 |
| 内置弹弹play 凭据 | `cache\danmaku\builtin-credential.bin` | 从 Momoka 凭据端点取回后用 DPAPI 加密，到期前自动换新；不是你的账号凭据 |
| 设置备份 | `settings.backup.json` | 不含密码与令牌 |
| 日志 | `logs\` | 请求地址只记到「协议 + 主机 + 路径」，查询参数被去掉；密码与令牌不写日志 |
| 海报与背景图缓存 | `cache\images\` | 从你自己的服务器下载的图片 |
| 着色器编译缓存 | `cache\shaders\` | 本机生成 |
| 弹幕与搜索结果缓存 | `cache\danmaku\` | 从弹弹play 取得的结果，有效期两小时，最多保留 16 份 |
| 截图 | `screenshots\`（可在设置里改到别处） | 你在播放时主动截的图 |
| 下载的媒体文件 | 系统「视频」文件夹中的 Momoka 子目录 | 你主动发起的下载；目前没有自选下载目录入口 |
| 网页控制台页与 YouTube 登录的浏览数据 | `webview2\`（含 `webview2\youtube\`） | 内嵌 WebView2 打开你服务器自己的网页控制台，或你在预告失败提示里主动登录 YouTube 时产生 |

应用还会读取本机已安装字体列表（用于字幕字体选择），仅在本机显示，不外传。

打格子记录包含时间、站点、作品和同步结果，Bangumi 记录还包含集号。记录保存在应用内存中，退出应用时清空。启用任一站点后，可在「设置 → MoviePilot」打开「打格子失败或跳过时发送系统通知」。此开关默认关闭，保存在本机设置中，可随无凭据的偏好备份导出和恢复。同步失败或跳过时，应用只把站点和固定原因交给 Windows 通知。可在设置中关闭后续通知，并在 Windows 通知中心清除已显示的通知。

## 4. 网络连接

应用只与以下地址通信：

1. **你自己的 Emby / Jellyfin / 飞牛影视 / Plex 服务器** —— 你在应用里填的地址。用于登录、读取媒体库、上报播放进度、同步续播位置与「看过」标记。
2. **Plex 的账号服务 `plex.tv`**（只有你用 Plex 账号登录时才会连） —— Plex 服务器本身不校验账号密码，账号由 Plex 的账号服务认证，所以填了 Plex 服务器并按下登录时，应用会向 `https://plex.tv/api/v2/users/signin` 发一次请求，正文里是你的 Plex 账号与密码，请求头里是客户端名、应用版本号和本机的设备 ID。这是**由 Plex 运营的第三方服务**，不是开发者运营的，也不是你填的那个地址；不登录 Plex 就不会有任何请求发到那里。
3. **你自己配置的第三方片库管理服务**（可选功能） —— 你在应用里填的地址。用于搜索、订阅与整理。
4. **你的服务器在元数据里给出的图片地址** —— 海报与背景图可能由你的服务器指向第三方图片服务（例如 `image.tmdb.org`）。应用只是按服务器给出的地址取图，不自行决定去哪个第三方。
5. **本项目的发布页**（可关闭，默认开启） —— 应用在每次启动时会向本项目的发布页源（`github.com/cudamin/Momoka/releases.atom`）查询有没有比当前这一版新的版本；「设置 → 关于」里的「检查新版本」按钮也会在你按下的那一刻发一次同样的请求。这个请求**不携带任何身份信息**：请求头里只有客户端名与应用版本号，不含账号、令牌、设备 ID、计算机名或你的服务器地址。不想要这个检查，把「设置 → 关于」里的「启动时自动检查新版本」关掉即可 —— 关掉之后，应用不会再向该地址发出任何请求（那一格旁边的手动按钮仍会按你的意思发一次）。

6. **弹弹play 官方开放弹幕网络**（默认关闭；应用凭据由 Momoka 统一提供，你不需要填写任何 AppId 或 AppSecret）—— 开启后的自动匹配与手动搜索会向 `api.dandanplay.net` 发送作品名称与集数；读取弹幕发送弹幕库 ID 和关联弹幕选项。请求头是应用 ID、时间戳与签名，不直接传明文 AppSecret。官方弹幕接口可能跳转到 `dandanplay.net` 的 HTTPS 加速子域，跨来源不传鉴权头。不上传视频、媒体服务器地址、账号或访问令牌；没有启用时不访问该服务。用户点击发送或在输入栏按回车时，会额外发送弹幕文字、该弹幕库中的时间、固定的白色滚动模式和应用昵称 Momoka，写入本应用的私有弹幕库；不自动重发。登录与播放历史接口没有接入。

7. **Momoka 内置弹幕凭据端点**（只在启用弹弹play时连接）—— 应用先向 Momoka 官网部署的端点 `https://yuanshenqidon.dpdns.org/api/danmaku/credential` 发一次 GET，应用只主动加上 `Momoka/版本` 的 User-Agent，HTTP 栈可能附带标准的 Accept／编码头；端点返回 AES-GCM 加密的弹弹play 应用凭据，客户端解密后在本机缓存。该端点不接收作品名称、集数、媒体服务器地址、账号、访问令牌或弹幕内容。Cloudflare 作为托管方可能按其政策记录网络层信息（IP、时间）。

8. **你自己部署的 danmu_api**（可选、默认关闭）—— 选择该来源或合并来源后，应用向你填写的服务地址发送作品查询、条目编号与弹幕读取请求。可选访问令牌按该服务协议放在 URL 路径中，不能当作弹弹play 的 AppId 或 AppSecret；该服务及其代理可能看到路径令牌，应只使用专用令牌。Momoka 不提供公共 danmu_api 服务；合并来源会同时请求弹弹play 和 danmu_api。发送弹幕仅走弹弹play，不向 danmu_api 写入。

9. **YouTube（只有你播放 YouTube 预告时）** —— 详情页右上角的预告预览与「播放」窗会加载 `www.youtube.com` 的公开页面。默认匿名加载；如果 YouTube 要求登录才能播放，详情页的提示条会给你一个「登录 YouTube」按钮，登录在应用内的 WebView2 页面里完成，账号 Cookie 保存在 `webview2\youtube\`，只供这支预告配置使用；不读取、不记录 Cookie 值。登录不是必须的，你也可以不登录而把视频交给系统浏览器打开，那时由浏览器自己的登录态与隐私设置决定。

10. **Bangumi 与豆瓣打格子（分别开启，默认关闭）** —— 播放符合条件的剧集时，应用通过你配置的 MoviePilot 服务只读查询作品。识别请求按需使用媒体库的作品名、原名、来源编号和季集坐标，年份用于核对候选。识别后，Bangumi 接收作品或单集编号及目标状态，豆瓣接收作品的「在看」或「看过」状态。Bangumi Access Token 用 DPAPI 加密保存在本机，认证时只发给 `api.bgm.tv`。豆瓣登录在内嵌浏览器中完成，会话由 `webview2\douban\` 下的独立 WebView2 配置管理。豆瓣更新会向本站提交它预填的收藏表单，以保留已有评分、短评、标签及隐私设置。在「设置 → MoviePilot」关闭对应站点可停用同步，也可清除已保存的 Bangumi 令牌或退出豆瓣。已写入的收藏和观看状态由对应站点保留，可在站点内管理。[打格子功能说明](docs/打格子.md)

发往媒体服务器的请求带有这些标识：客户端名 `Momoka`、**本机计算机名**、随机生成并保存在本机的设备 ID、应用版本号，以及你的访问令牌。这些只发给**你自己的服务器**，用途是让服务器认出「这是哪台设备」。

**开发者不会接收上述媒体服务凭据与使用数据。** 第 2、5、6 项是非媒体服务器地址：Plex 只在你选择登录时使用，更新源只带客户端名与版本号，弹弹play 只在明确开启后使用，带作品查询与应用鉴权信息。第 7 项是开发者运营的凭据端点，只用于下发应用凭据，不承载你的作品查询或播放数据。第 2、6 项的数据由对应第三方服务处理，不是开发者运营的服务。关于页的 GitHub 点星与弹弹play 网站按钮仅在点击后交给默认浏览器打开。

**实时字幕翻译（可选、默认关闭）**：你开启并配置 API 密钥后，当前显示的主文字字幕会逐句发给 Google Cloud Translation、Microsoft Translator，或你配置的兼容 OpenAI AI 服务。请求包含字幕原文、源/目标语言与服务所需配置；密钥只在请求头，不进入 URL、日志或子进程参数。谷歌/微软只允许官方端点，AI 自定义端点只允许 HTTPS 或本机 HTTP 回环地址。第三方可能按量收费并按其隐私政策处理原文。应用仅缓存每片最多 256 句译文于内存，切片清除，不保存字幕原文/译文文件；关闭、注销与切片撤销旧请求。没有文字字幕、PGS/VOBSUB 图形字幕不作 OCR 或语音识别。网络取消不能撤回第三方已经收到的文本。

## 5. 我们不做什么

- 不收集个人信息、使用统计或诊断数据
- 不含广告、内购或用户追踪
- 不向开发者回传播放、账号或媒体数据；唯一请求是第 4 节第 7 项的凭据端点，只带客户端版本与网络层信息
- 不读取与功能无关的文件

## 6. 儿童

应用不面向儿童，也不收集任何年龄相关信息。

## 7. 数据删除

- 卸载应用**不会**删除第 3 节列出的数据。
- 要清除本应用专属数据：退出应用后删除 `%LOCALAPPDATA%\Momoka` 整个目录。
- 自选截图目录可能是共享目录，只删除你确认由 Momoka 生成的截图文件；不要删除整个共享目录。下载文件在系统「视频」下的 Momoka 子目录，也只删除你确认不再需要的文件。
- 要撤销服务器上的登录：在你的媒体服务器上删除对应的设备或访问令牌；用 Plex 账号登录的，还要在 Plex 账号里撤销该设备的授权。

## 8. 第三方组件

应用内包含以下第三方组件，各自遵循其许可，随包附带许可与归属文本（`shaders\`、`mpv-ui\` 目录与 exe 根目录下，清单见 `THIRD-PARTY-NOTICES.md`）：mpv / libmpv（LGPL-2.1-or-later，静态链 LGPL-3.0 的 FFmpeg）、uosc（LGPL-2.1）、Material Icons Round 字体（Apache-2.0）、ArtCNN / CfL（MIT）、ravu 与 igv SSim 系列（LGPL-3.0-or-later）、an3223（LGPL-2.1-or-later）、igv adaptive-sharpen（BSD-2-Clause）、vulkan-1.dll（Apache-2.0），以及 Microsoft Windows App SDK 与 WebView2 Runtime。应用自身按 GPL-3.0-or-later 授权，并附 Windows App SDK / WebView2 的有限链接、组合分发附加许可（见 README 的许可证一节及 THIRD-PARTY-NOTICES.md）。

这些组件同样不收集数据，不改变本政策。

## 9. 政策变更

本政策如有修改，会更新本文件顶部的日期，并在项目仓库中留下记录。

## 10. 联系方式

问题或请求请通过项目仓库的议题页提出：<https://github.com/cudamin/Momoka/issues>

---

# Momoka Privacy Policy

Last updated: 2026-11-26

Momoka is a Windows desktop client for an Emby, Jellyfin, 飞牛影视 or Plex media server that you own or are authorized to use.

**The developer collects no telemetry, analytics, or account data.** Enabled features send the information needed for their function to your selected services. The app contains no telemetry, analytics, advertising, or crash reporting. Apart from the built-in DandanPlay credential endpoint described below — which only returns an application credential — it does not contact any service operated by the developer.

Local application data defaults to `%LOCALAPPDATA%\Momoka`: your server address, account name and preferences (`settings.json`); passwords, access tokens, Translation API keys and an optional danmu_api access token encrypted with Windows DPAPI; logs without credentials; poster/backdrop and shader caches; screenshots; and WebView2 browsing data. Screenshots may use a directory you select; downloaded media goes to the Momoka subdirectory of the system Videos folder. DandanPlay uses only Momoka's built-in application credential, cached under `cache/danmaku/builtin-credential.bin`, encrypted with Windows DPAPI and refreshed before it expires. User-provided DandanPlay AppId/AppSecret settings are retired. Its search/comment cache lives in `cache/danmaku`, expires after two hours, and keeps at most 16 files. Preference backups exclude credentials. The local font list is only displayed locally.

Scrobbling records contain the time, site, show title and sync result. Bangumi records also include the episode number. They stay in app memory and clear when you exit. Once you enable either scrobbling site, you can turn on Windows notifications for failed or skipped syncs in Settings → MoviePilot. The switch is off by default, is saved locally, and can be exported and restored in a credential-free preference backup. Momoka passes only the site and a standard reason to Windows. You can disable future notifications in Momoka settings and clear existing ones in Windows Notification Center.

The app connects to these services, and the optional DandanPlay service described below: (1) your own Emby, Jellyfin, 飞牛影视 or Plex server, at the address you enter, to sign in, browse your library, report playback progress, and sync resume positions; (2) Plex's account service `plex.tv` — only when you sign in with a Plex account: a Plex server does not verify account credentials itself, the account is authenticated by Plex's account service, so when you enter a Plex server and press 登录 the app sends one request to `https://plex.tv/api/v2/users/signin` with your Plex account and password in the body and the client name, app version and this machine's device ID in its headers; that is a third-party service operated by Plex, not by the developer and not the address you entered, and no request goes there unless you choose to sign in with a Plex account; (3) a third-party library-management service that you configure yourself, if you enable that feature, at the address you enter; (4) image URLs that your own server supplies in its metadata (for example `image.tmdb.org`); and (5) the project's own releases page (on by default, can be turned off) — on each launch the app asks the project's releases feed (`github.com/cudamin/Momoka/releases.atom`) whether a newer version exists, and the 「检查新版本」 button in Settings → About sends the same request when you press it. That request carries **no identifying information**: its headers hold only the client name and the app version — no account, no token, no device ID, no machine name, no server address. To stop it, turn off 「启动时自动检查新版本」 in Settings → About; with that switch off the app sends nothing to that address (the manual button next to it still sends one request when you press it).

Requests to your media server carry the client name `Momoka`, this computer's machine name, a randomly generated device ID stored locally, the app version, and your access token — sent only to your own server, so it can identify this device. The update feed in item (5) carries none of those identifiers; item (2) is also not an address you entered, but it is only reached when you choose to sign in with a Plex account, and it carries the Plex credentials you type.

When the optional DandanPlay source is enabled, the app also contacts Momoka's credential endpoint at `https://yuanshenqidon.dpdns.org/api/danmaku/credential`. The app adds only the `Momoka/<version>` User-Agent; the HTTP stack may add standard Accept and encoding headers. The endpoint returns an AES-GCM-encrypted application credential and receives no work title, episode number, media-server address, account, token, or comment text. Cloudflare, as the host, may log network-layer information under its own policy.

Uninstalling the app does not delete the data listed above. After exiting the app, you may delete its dedicated `%LOCALAPPDATA%\Momoka` folder. A screenshot location can be a shared folder: remove only files you confirm were generated by Momoka, never the entire shared folder. In the Momoka subdirectory of the system Videos folder, remove only downloaded files you no longer need. To revoke access, remove the corresponding device or token on your media server; for a Plex account, also revoke that device's access in your Plex account.

An additional optional destination is the official DandanPlay open comment network. It is off by default and uses only Momoka's built-in application credential. Once enabled, automatic matching and manual searches send a work title and episode number to `api.dandanplay.net`; fetching comments sends the comment-library ID and related-comment preference. Requests carry the application ID, timestamp and signature, not a plaintext AppSecret. Official HTTPS CDN redirects are allowed only within `dandanplay.net`; cross-origin requests carry no authentication headers. No video content, media-server address, media account or access token is sent. The comment network itself is a third-party service; the credential endpoint described above is operated by the developer. Posting is performed only when the user clicks Send or presses Enter in the comment input. It sends the text, comment-library time, fixed white scrolling mode and the application nickname Momoka to the application's private comment library, without automatic retries. DandanPlay login and playback-history APIs are not used. The built-in credential is shared across Momoka users and is not a confidentiality mechanism: it is distributed with the client and can be extracted, but it is kept out of the public source and can be rotated server-side. About-page website buttons open the default browser only when clicked.

Optional self-hosted danmu_api receives work searches, item identifiers and comment-reading requests at the address you configure. Its optional dedicated access token is placed in the URL path by that protocol; the service and its proxy may see it. It is separate from a DandanPlay application credential. Momoka provides no public danmu_api service. Combined source mode requests both destinations; comment posting uses only DandanPlay.

Bangumi and Douban scrobbling are separate options and both are off by default. During eligible episode playback, Momoka uses read-only lookups on your configured MoviePilot service to identify the show. The requests can use show titles, original titles, provider identifiers and season or episode numbers from your media library. The release year is used to verify candidates. Bangumi updates include a show or episode identifier and watch status. Douban updates set the show's status to watching or watched. A Bangumi access token is encrypted locally with Windows DPAPI and sent only to `api.bgm.tv` for authentication. Douban sign-in runs in the embedded browser, and WebView2 manages its session in a separate profile under `webview2\douban\`. Douban updates submit the site's prefilled collection form back to Douban to preserve the existing rating, review, tags and privacy settings. In Settings → MoviePilot, you can turn off a site to stop syncing, clear the saved Bangumi token or sign out of Douban. Existing collection and watch updates remain on their respective sites, where you can manage them. [Scrobbling feature details](docs/打格子.md)

Optional real-time Subtitle Translation is off by default. When enabled with an API key, the currently displayed primary text subtitle is sent to Google Cloud Translation, Microsoft Translator, or your configured OpenAI-compatible AI service. Requests contain the subtitle text, source/target language and required service configuration. Keys go in request headers, never URLs, logs or process arguments. Google/Microsoft use official endpoints; AI endpoints allow HTTPS or local HTTP loopback. Services may impose usage charges and process the text under their privacy policies. Up to 256 translated lines per file are held in memory and cleared on file changes; no original/translated subtitle file is saved. Disabling the feature, signing out or switching files cancels old requests, but cancellation cannot retract text already received by the provider. Image subtitles are not transcribed or processed with OCR.

Bundled third-party components — mpv / libmpv (LGPL-2.1-or-later, statically linked against an LGPL-3.0 FFmpeg), uosc (LGPL-2.1), the Material Icons Round font (Apache-2.0), the ArtCNN and CfL shaders (MIT), the ravu and igv SSim shaders (LGPL-3.0-or-later), the an3223 shaders (LGPL-2.1-or-later), igv's adaptive-sharpen (BSD-2-Clause), vulkan-1.dll (Apache-2.0), and the Microsoft Windows App SDK and WebView2 Runtime — follow their own licenses, with license and attribution texts shipped alongside the app (see `THIRD-PARTY-NOTICES.md`). mpv / libmpv remains an LGPL build; Momoka's own code is licensed under GPL-3.0-or-later with the limited additional permission for Microsoft Windows App SDK / WebView2 components in THIRD-PARTY-NOTICES.md (see the license section of the README). They do not collect data.

Questions: <https://github.com/cudamin/Momoka/issues>
