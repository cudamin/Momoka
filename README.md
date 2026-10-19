# Momoka

第三方媒体库客户端，支持 Emby、Jellyfin（实服播放与写入未验证）、飞牛影视（实播未验证）和 Plex（未对真实服务器核对），界面是原生 WinUI 3，播放器内核是 libmpv。由天才程序员Claude、GPT、DeepSeek、GLM完成开发。

telegram 发布频道：[https://t.me/Ebizuka_Tomo](https://t.me/Ebizuka_Tomo)

[微软商店](https://apps.microsoft.com/detail/9nqjl7k0h8x0?hl=zh-CN&gl=CN)

观影交流群：[1039548970](https://qm.qq.com/q/vXph3vMyFW)


## 设计参考

- UI 界面参考 **Emby网页端和小秘** 设计。
- 播放器架构参考 [**mpv-winui-player**](https://github.com/ikas-mc/mpv-winui-player) 和 **小幻影视** 设计。
- 内置lua脚本使用 @dyphire[**mpv-config**](https://github.com/dyphire/mpv-config)的脚本进行魔改
- mpv 相关设置参考 @dyphire[**mpv-config**](https://github.com/dyphire/mpv-config) 进行设置
- 内置 [**mpv-winbuild**](https://github.com/zhongfly/mpv-winbuild/releases/tag/2026-10-14-270367e8af)（LGPL 构建）

## 主要功能

**着色器** — 着色器随程序打包（ArtCNN、ravu、CfL、SSimDownscaler 等），按放大倍数分四档 × 真人/动画 × 显卡档，每一档都做色度重建。

**字幕筛选** — 字幕支持设置自定义优先级进行筛选，默认排除繁体中文，优先自动选择双语字幕、特效字幕。

**视频筛选** — 视频支持设置自定义优先级进行筛选，默认优先播放最后入库的版本，支持按照不同组名来筛选。

**MoviePilot API调用** — 通过调用MoviePilot API内置多个功能的快捷键，集成手动整理、搜索不同版本等功能。主页最下方集成网页端订阅列表，播放订阅一体化管理。

**界面** — 原生 WinUI 3，内置ui动画和五套深色配色墨绿、纯黑、午夜、石墨、紫夜，还有首页轮播图。

**多账号** — 支持多台服务器、多个账号同时在线；登录新账号不会退出已登录账号，主页的媒体库列表与各库最近添加覆盖全部在线账号。

**服务端界面** — 只有一台服务器在线时使用它自己的界面（Emby 与 Jellyfin 已复刻各自的管理入口与网页控制台目录，飞牛影视作为唯一在线服务器时有自己的「帐号／影视服务器」原生设置目录，Plex 暂用通用界面）；同时连接多台服务器时主界面切换为通用界面，设置左栏则在 Momoka 组下面按服务器各列一个可折叠组，默认全部收起，展开哪一组就使用那一台现有的 Emby／Jellyfin 网页控制台或飞牛原生设置页。没有库管理权限的服务器/账号不列对应管理入口。

**服务端** — 支持多种服务端，Emby、Jellyfin（实服播放与写入未验证）、飞牛影视（实播未验证）和 Plex（未对真实服务器核对）

## 系统要求

- Windows 10 1809 及以上，仅 x64
- Emby 4.10.x
- MoviePilot 3.x.x
- Jellyfin 已做协议与离线界面适配，实服播放、写入及网页控制台登录未验证
- 飞牛影视按网页 0.9.8 / 服务 0.8.43 的官方前端核对，当前原画适配的实播未验证；这组版本是核对记录，不是全版本兼容保证
- Plex 只做了协议与离线测试适配，没有对任何真实服务器核对过；连接、浏览与播放的实际行为未经证实，边界见 [Plex 支持](docs/Plex支持.md)
- 可选：「网页控制台」页需要 WebView2 Runtime，没有则提示改用浏览器打开

## 下载

到 [Releases](https://github.com/cudamin/Momoka/releases) 下载最新的 `Momoka_windows-x64_x.x.x.exe` 安装包，或通过 [Microsoft Store](https://apps.microsoft.com/detail/9nqjl7k0h8x0?hl=zh-CN&gl=CN) 安装。

## 开发与验证

开发使用 .NET 10 SDK，具体版本以 [global.json](global.json) 为准；构建、离线测试、隔离界面探针及发布命令见 [开发与验证](docs/开发与验证.md)。本地播放与发布需要另行准备匹配的 x64 `libmpv-2.dll`，该文件不入库。

## 许可证
**本软件按 `MIT` 分发**，许可正文见 [LICENSE](LICENSE)。

随包的播放内核是 **LGPL 构建的 mpv / libmpv**（LGPL-2.1-or-later，静态链 LGPL-3.0 的 FFmpeg）。
LGPL 允许链接而不传染，所以本软件自身按 MIT 分发；那一份 dll 仍是 LGPL 作品，它的许可正文
（`LICENSE.LGPL-2.1.txt`、`LICENSE.LGPL-3.0.txt`、`LICENSE.GPL-3.0.txt`）与对应源代码地址随包提供。

随包还包含多个第三方组件（uosc、Material Icons、着色器、vulkan-1.dll 等），逐个组件的许可证、
许可正文在包内的位置、以及本软件对它们做过的修改，都列在
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)；审计过程与判定依据见
[docs/第三方组件许可合规.md](docs/第三方组件许可合规.md)。其中 `mpv-ui` 里那份 uosc 是 LGPL-2.1，
本软件改过它，修改部分本身也以 LGPL-2.1 授权。

完整对应源代码见本仓库。

## 免责声明

**请在使用 Momoka 前完整阅读本节。下载、安装或使用本软件，即表示你已阅读、理解并同意以下全部内容；若你不同意，请立即停止使用并卸载。**

**1. 软件按「现状」提供。** Momoka 是免费提供的开源软件，按「现状」（AS IS）和「现有可用」提供，不附带任何明示或默示的担保，包括但不限于对适销性、特定用途适用性、无错误运行、不中断以及安全性的担保。开发者不保证本软件在所有环境与版本组合（Emby、MoviePilot、显卡驱动、Windows 版本等）下都能正常工作。

**2. 本软件不提供任何内容。** Momoka 只是一个客户端界面，本身不包含、不托管、不分发、不索引任何影视、音乐或其他媒体内容。所有内容都来自你自己搭建或你已获授权使用的媒体服务器；内容的来源与合法性由服务器搭建者与使用者自行负责。

**3. 与第三方无任何关联。** Momoka 是独立的第三方客户端，与 Emby LLC、MoviePilot、The Movie Database (TMDB)、豆瓣、IMDb、Microsoft 等均无隶属、授权、赞助、合作或背书关系。文中出现的所有产品名、商标与图标归其各自所有者，此处仅用于说明兼容性或作为引用。

**4. 使用者自负合规与版权责任。** 你有责任确保对所使用的服务器及其中的内容拥有合法权利或授权，并遵守你所在国家或地区的法律法规。**请勿将本软件用于观看、获取、传播盗版、侵权或任何违法内容。** 因使用本软件（包括但不限于访问服务器、播放、下载、截图、上报播放记录等行为）产生的一切法律后果，由使用者自行承担。

**5. 责任限制。** 在适用法律允许的最大范围内，开发者不对因使用或无法使用本软件而造成的任何直接、间接、附带、特殊、惩罚性或后果性损害承担责任，包括但不限于数据丢失、播放记录或续播点变化、服务器账号被限制或封禁、设备或数据损坏、服务中断，以及任何第三方索赔。使用本软件的风险由你自行承担。

**6. 第三方组件。** 本软件随包包含 mpv / libmpv（**LGPL-2.1-or-later**，静态链 LGPL-3.0 的 FFmpeg）、uosc（**LGPL-2.1**）与 Material Icons Round 字体（Apache-2.0）、ArtCNN 与 CfL 着色器（MIT）、ravu 与 igv 的 SSimDownscaler / SSimSuperRes（LGPL-3.0-or-later）、an3223 着色器（LGPL-2.1-or-later）、igv adaptive-sharpen（BSD-2-Clause）、vulkan-1.dll（Apache-2.0），以及 Microsoft Windows App SDK、WebView2 Runtime 等第三方组件。它们各自遵循其许可，版权归各自作者，相关许可与归属文本随包提供（清单见 `THIRD-PARTY-NOTICES.md`）。**其中 mpv / libmpv 是 LGPL 构建，LGPL 允许链接而不传染，本软件自身按 MIT 分发；那一份 dll 仍是 LGPL 作品，其许可正文与对应源代码地址随包提供。** 使用这些组件不改变本声明。

**7. 第三方接口依赖。** 部分功能依赖第三方服务器接口（如 Emby、MoviePilot、TMDB 等），这些接口可能随时变更、限制或停止服务。由此导致的功能失效或不可用，开发者不作保证，也不承担责任。

**8. 隐私。** 本软件不收集、不上传、不分享你的任何数据，详见 [PRIVACY.md](PRIVACY.md)。

**9. 变更与终止。** 开发者保留随时修改、更新或停止分发本软件及本声明的权利，本声明的更新以本文件为准。

