# [Momoka](https://yuanshenqidon.dpdns.org)

第三方媒体库客户端，支持 Emby、Jellyfin、飞牛影视和 Plex（实验性，未实服核对），界面是原生 WinUI 3，播放器内核是 libmpv。由天才程序员Claude、GPT、DeepSeek、GLM完成开发。

项目播放器基于https://github.com/dyphire/mpv-config 制作

[个人 Telegram 频道](https://t.me/Ebizuka_Tomo)  |  [微软商店](https://apps.microsoft.com/detail/9nqjl7k0h8x0?hl=zh-CN&gl=CN)  |  [QQ交流群：1039548970](https://qm.qq.com/q/vXph3vMyFW)

## 主要功能

**着色器** — 着色器随程序打包（ArtCNN、ravu、CfL、SSimDownscaler 等），按放大倍数分四档 × 真人/动画 × 显卡档，每一档都做色度重建。

**补帧** — 设置 → 视频输出 → 补帧可接入本机装好的 SVP（默认关闭），走 SVP 官方 mpvpipe 通道，集成与独占管线都可用；组件路径留空自动查找，Momoka 不安装、不下载，也不改系统环境。

**字幕筛选** — 字幕支持设置自定义优先级进行筛选，默认排除繁体中文，优先自动选择双语字幕、特效字幕。

**视频筛选** — 视频支持设置自定义优先级进行筛选，默认优先播放最后入库的版本，支持按照不同组名来筛选。

**播放器** — 内置 libmpv 可以嵌进主窗口，也可以用独立独占窗口（uosc）播放，或改成调用本机 mpv.exe；内置后端在独占模式下可加载自定义 mpv.conf，集成模式下这一项固定只读。播放页右键菜单覆盖轨道、画面、字幕、截图与章节制作；封面「更多」菜单还能把影片排成「下一项播放」或「追加到队尾」。

**播放工具** — 设置 → 播放工具里指定本机的 FFmpeg／FFprobe、ffsubsync 或 alass、faster-whisper，右键菜单就能导出无损片段、GIF 与整份字幕，裁切截图、按音轨同步字幕、生成 AI 字幕或翻译整份字幕；章节可以标记、改名并导出 CHP／OGM。

**预告片** — 设置 → 界面 → 预告片控制两处播放方式：封面悬停面板默认自动播放本地预告（静音、循环），商店模式详情页右上角默认自动播放预告。YouTube 预告需要登录时，详情页提示条里直接登录；刮削与预告共用的代理在设置 → 关于 → 网络里填。

**MoviePilot** — 通过 MoviePilot API 搜索资源、确认下载、管理订阅与订阅墙，手动／智能整理文件、查看媒体管理与历史；主页最下方是网页端的订阅列表，播放和订阅在同一处管理。

**AniBT** — 搜索页固定有 AniBT 一栏，按番剧目录查资源、核对字幕组后交 MoviePilot 下载；入库的剧集与动画电影还能用原文件名走 AniBT 识别，按 Bangumi 身份重新整理，成功入库后触发一次服务器扫库。

**片库管理** — 媒体库列表可多选批量标记已看、收藏、加入合集或播放列表、转换、删除、刷新元数据与扫库；条目菜单可改元数据、换封面（服务器图库按来源和语言筛选，也能上传本地图片）、管理字幕、查看遗漏集（剧与季），当前服务器不支持的项自动收起。标题栏的收藏页汇总所有在线账号的收藏。

**音乐库** — Emby、Jellyfin 与飞牛音乐共用同一套音乐界面和底部播放栏（Plex 为协议适配）：歌曲与收藏是曲目表，专辑、艺术家与歌单是封面墙；点播放栏的封面或歌名进音乐播放页，歌词先取服务器歌词表（飞牛音乐），没有就读音频文件自己的标签，带时间轴的按进度高亮。

**本机整理** — 设置 → 本机整理把本机磁盘或网络共享的目录建成媒体库，用 TMDB 或 MoviePilot 识别，按直接改名／硬链接／软链接整理，可以开启自动监控刮削入库；首页为每个本地库单列一张卡片，本地库目前只浏览已入库内容，不在应用内播放。

**界面** — 原生 WinUI 3，设置 → 界面 → 详情页外观可在沉浸模式与商店模式之间切换，商店模式的侧栏宽度可拖（180–480）；五套深色配色薄荷影院、纯黑、午夜、石墨、紫夜加跟随系统，首页轮播可选全宽大图或商店版式，封面进详情的关联动画保留圆角，折叠侧栏的库预览里封面悬停出遮罩与播放键。

**多账号** — 设置 → 服务器里的“聚合模式”默认关闭：开启后支持多个账户同时在线，聚合在主页与搜索栏中。

**弹幕** — 数据由 [弹弹play开放弹幕网络](https://www.dandanplay.com) 提供（内置凭据，不需要自己申请 AppId），同时支持[danmu_api](https://github.com/huangxd-/danmu_api)；弹幕走独立图层，不占主字幕或第二字幕轨。

**实时字幕翻译** — 设置 → 字幕可选谷歌、微软和兼容 OpenAI 的 AI 服务，保留原字幕；默认关闭，需要自备 API 密钥，可能收费且启用后外发文字字幕。

**播放器快捷键** — 截图、字幕开关、音字幕切轨、逐帧、片头、跳转回退、统计与弹幕控制；旧用户自定义按键优先，不被新增默认键覆盖。

## 系统要求

- Windows 10 1809 及以上，仅 x64
- Emby 4.10.x
- MoviePilot 3.x.x
- Jellyfin 10.10.7 已实测保存登录恢复、媒体库读取、原画播放和续播；管理写入未验证，内嵌网页控制台登录与界面实屏覆盖仍未确认，边界见 [Jellyfin 支持](docs/Jellyfin支持.md)
- 飞牛影视网页 0.9.8 / 服务 0.8.43 已实测保存登录恢复、媒体库读取、原画播放和续播，修复详情零时长的流时长回退；管理写入未验证。这组版本是实测记录，不是全版本兼容保证，详情见 [飞牛影视](docs/飞牛影视.md)
- Plex 只做了协议与离线测试适配，包括普通视频播放列表／合集的目录、成员和授权写入；没有对任何真实服务器核对过，连接、浏览、清单写入与播放的实际行为未经证实，边界见 [Plex 支持](docs/Plex支持.md)
- 可选：「网页控制台」页需要 WebView2 Runtime

## 下载

到 [Releases](https://github.com/cudamin/Momoka/releases) 下载最新的 `Momoka_windows-x64_x.x.x.exe` 安装包，或通过 [Microsoft Store](https://apps.microsoft.com/detail/9nqjl7k0h8x0?hl=zh-CN&gl=CN) 安装。

## 开发与验证

常设规则见 [CLAUDE.md](CLAUDE.md)。开发使用 .NET 10 SDK，具体版本以 [global.json](global.json) 为准；构建、离线测试、隔离界面探针及发布命令见 [开发与验证](docs/开发与验证.md)。本地播放与发布需要另行准备匹配的 x64 `libmpv-2.dll`，该文件不入库。

整体架构、模块职责、播放管线与验证体系见 [技术文档](docs/技术文档.md)。

主页翻档完成／取消的独立离线回归与运行边界见 [翻档夹具](tests/Momoka.HomeFoldCompletionTests/README.md)。

## 许可证
**Momoka 自身代码按 `GPL-3.0-or-later` 授权**：你可以选择 GNU 通用公共许可证第 3 版，或自由软件基金会发布的任何后续版本。Copyright (c) 2026 Momoka contributors。许可正文见 [LICENSE](LICENSE)。

Momoka is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

Windows App SDK / WinUI / Windows App Runtime 与 WebView2 的有限链接、组合分发附加许可，见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md#momoka-microsoft-components-permission)。这项许可仅适用于有权授权的 Momoka 代码，微软组件仍遵循各自条款。

随包的播放内核是 **LGPL 构建的 mpv / libmpv**（LGPL-2.1-or-later，静态链 LGPL-3.0 的 FFmpeg）。
本次选择不改变那一份 DLL 的 LGPL 许可，它的许可正文
（`LICENSE.LGPL-2.1.txt`、`LICENSE.LGPL-3.0.txt`、`LICENSE.GPL-3.0.txt`）与对应源代码地址随包提供。

随包还包含多个第三方组件（uosc、Material Icons、着色器、vulkan-1.dll 等），逐个组件的许可证、
许可正文在包内的位置、以及本软件对它们做过的修改，都列在
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)；审计过程与判定依据见
[docs/第三方组件许可合规.md](docs/第三方组件许可合规.md)。其中 `mpv-ui` 里那份 uosc 是 LGPL-2.1，
本软件改过它，修改部分本身也以 LGPL-2.1 授权。

完整对应源代码见本仓库。分发二进制时，须按 GPL 提供与该二进制对应的完整源码和构建资料；仅链接到持续变化的最新分支不代替这项义务。此前已按 MIT 发布的代码仍保留原有 MIT 授权，相关声明见第三方归属清单。

## 免责声明

**本节说明软件风险、第三方关系与法律责任，不对 GPL 授予的使用、修改或再分发权利增加限制；如有冲突，以适用的软件许可证为准。**

**1. 软件按「现状」提供。** Momoka 是免费提供的开源软件，按「现状」（AS IS）和「现有可用」提供，不附带任何明示或默示的担保，包括但不限于对适销性、特定用途适用性、无错误运行、不中断以及安全性的担保。开发者不保证本软件在所有环境与版本组合（Emby、MoviePilot、显卡驱动、Windows 版本等）下都能正常工作。

**2. 本软件不提供任何内容。** Momoka 只是一个客户端界面，本身不包含、不托管、不分发、不索引任何影视、音乐或其他媒体内容。所有内容都来自你自己搭建或你已获授权使用的媒体服务器；内容的来源与合法性由服务器搭建者与使用者自行负责。

**3. 与第三方无任何关联。** Momoka 是独立的第三方客户端，与 Emby LLC、MoviePilot、The Movie Database (TMDB)、豆瓣、IMDb、Microsoft 等均无隶属、授权、赞助、合作或背书关系。文中出现的所有产品名、商标与图标归其各自所有者，此处仅用于说明兼容性或作为引用。

**4. 使用者自负合规与版权责任。** 你有责任确保对所使用的服务器及其中的内容拥有合法权利或授权，并遵守你所在国家或地区的法律法规。观看、获取或传播媒体内容时，请尊重版权；这是法律提醒，不是对 GPL 授权的额外用途限制。因使用本软件（包括但不限于访问服务器、播放、下载、截图、上报播放记录等行为）产生的一切法律后果，由使用者依法承担。

**5. 责任限制。** 在适用法律允许的最大范围内，开发者不对因使用或无法使用本软件而造成的任何直接、间接、附带、特殊、惩罚性或后果性损害承担责任，包括但不限于数据丢失、播放记录或续播点变化、服务器账号被限制或封禁、设备或数据损坏、服务中断，以及任何第三方索赔。使用本软件的风险由你自行承担。

**6. 第三方组件。** 本软件随包包含 mpv / libmpv（**LGPL-2.1-or-later**，静态链 LGPL-3.0 的 FFmpeg）、uosc（**LGPL-2.1**）与 Material Icons Round 字体（Apache-2.0）、ArtCNN 与 CfL 着色器（MIT）、ravu 与 igv 的 SSimDownscaler / SSimSuperRes（LGPL-3.0-or-later）、an3223 着色器（LGPL-2.1-or-later）、igv adaptive-sharpen（BSD-2-Clause）、vulkan-1.dll（Apache-2.0），以及 Microsoft Windows App SDK、WebView2 Runtime 等第三方组件。它们各自遵循其许可，版权归各自作者，相关许可与归属文本随包提供（清单见 `THIRD-PARTY-NOTICES.md`）。**其中 mpv / libmpv 继续按其 LGPL 许可分发；Momoka 自身代码采用 GPL-3.0-or-later，并附上述微软组件的有限附加许可。** 各组件的许可正文与对应源码获取说明随包提供。

**7. 第三方接口依赖。** 部分功能依赖第三方服务器接口（如 Emby、MoviePilot、TMDB 等），这些接口可能随时变更、限制或停止服务。由此导致的功能失效或不可用，开发者不作保证，也不承担责任。

**8. 隐私。** 开发者不收集遥测、使用统计或账号数据；你启用的媒体、弹幕和字幕翻译功能会向所选服务发送必要信息，详见 [PRIVACY.md](PRIVACY.md)。

**9. 后续变更。** 开发者可更新或停止分发后续版本；本声明的更新不追溯撤销已授予的许可，许可证的终止与恢复以其自身条款为准。
