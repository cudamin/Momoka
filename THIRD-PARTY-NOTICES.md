# 第三方组件与归属

本文件列出 Momoka 发行包内含的第三方组件、各自的许可证，以及许可证正文在包内的位置。
每条都记了出处，便于升级时重新核对。

---

## 〇、本项目自身的许可证

**Momoka 整体按 `MIT` 分发**，许可正文是仓库根目录的 `LICENSE`
（随发布拷到 exe 根，与 `libmpv-2.dll` 同级）。Copyright © 2026 Momoka 贡献者。

这不是「随包组件都宽松」得出的结论，而是**选定的**：随包的 `libmpv-2.dll` 是 LGPL 构建
（LGPL-2.1-or-later，静态链 LGPL-3.0 的 FFmpeg），LGPL 允许「链接而不传染」，所以本项目
可以自选许可。2026-10-14 之前随包的是 GPL 构建，那时整个应用被迫按 GPL-2.0-or-later 分发；
两轮判定的过程与依据见 `docs/第三方组件许可合规.md`。

**仍受约束的一角**：`mpv-ui` 里那份 uosc 是 LGPL-2.1，本项目改过它，按 LGPL-2.1 第 2 条，
修改部分本身也以 LGPL-2.1 授权（见第二节）。这一条不随内核更换而改变。

完整对应源代码：<https://github.com/cudamin/Momoka>

---

## 一、播放内核与运行时

### mpv / libmpv — `LGPL-2.1-or-later`（静态链 LGPL-3.0 的 FFmpeg，合起来按 LGPL-3.0 处理）

| 项 | 值 |
|---|---|
| 文件 | `libmpv-2.dll`（exe 根目录，101,242,880 字节 / 96.6 MiB） |
| 上游 | <https://github.com/mpv-player/mpv> |
| 构建 | <https://github.com/zhongfly/mpv-winbuild>（基于 shinchiro/mpv-winbuild-cmake） |
| 版本 | mpv v0.41.0-1163-g270367e8a（提交 `270367e8af…`），构建于 2026-10-14 03:08:54 |
| 发布 | release `2026-10-14-270367e8af` 的 `mpv-dev-lgpl-x86_64-20261014-git-270367e8af.7z` |
| 归档 SHA256 | `1488b4458279561a4a0134ddf837989dcc8045308ac521657a98a284bcd8529d`（与发布页 `sha256.txt` 一致） |
| 文件 SHA256 | `fe583af31036bf3f3adacf6659e1bd448b568091b0334675573a3e303ec89d17` |
| 版权 | Copyright © 2000–2026 mpv/MPlayer/mplayer2 projects |
| 许可正文 | `LICENSE.LGPL-2.1.txt`、`LICENSE.LGPL-3.0.txt`、`LICENSE.GPL-3.0.txt`（exe 根目录） |
| 判定依据 | `assets/mpv-runtime/NOTICE.txt` |

**这份是 LGPL 构建，不是 GPL 构建。** 依据是这份二进制自己报出来的构建配置行 ——
`--vo=help` 那类清单随 `-Dcplayer=false` 一起没了，所以改从 libmpv 的日志接口读启动横幅：

```
-Dgpl=false -Ddvdnav=disabled -Drubberband=disabled -Dopenal=disabled
-Dcplayer=false -Dlibmpv=true ...
```

它的功能表里没有 `gpl` / `direct3d` / `dvdnav` / `rubberband`；二进制里 `libx264`、`libx265`、
`libdvdcss`、`dvdread` 这四个 GPL-only 组件的字符串出现 **0** 次（换掉的那份 GPL 构建是
5 / 10 / 3 / 6 次）。这几条与「LGPL 构建」的说法互相印证。

**义务：** LGPL 允许动态链接而不传染，所以 Momoka 自身可以按 MIT 分发；但随包这一份仍是 LGPL 作品，
要随二进制提供许可正文（上面三份文本）与对应源代码的获取地址 —— mpv、它链入的 FFmpeg 与
构建脚本都在上表的地址。**这份 dll 按上游原样分发，本项目没有改动它。**

**与 uosc 那半无关。** 内核换不换，`mpv-ui` 里的 uosc 修改部分仍是 LGPL-2.1（见第二节）。

2026-10-14 之前随包的是 GPL 构建（`mpv v0.41.0-923-g7b8915bc1`，dyphire/mpv-winbuild），
那时整个应用被迫按 GPL-2.0-or-later 分发；换成 LGPL 构建的来龙去脉与两轮判定见
`docs/第三方组件许可合规.md`。

### vulkan-1.dll — `Apache-2.0`

| 项 | 值 |
|---|---|
| 文件 | `vulkan-1.dll`（exe 根目录） |
| 上游 | <https://github.com/KhronosGroup/Vulkan-Loader> |
| 版本 | 1.4.359.0（1.4.359.Dev Build） |
| 许可正文 | `LICENSE.Apache-2.0.txt`（exe 根目录） |

`libmpv-2.dll` 唯一的非系统依赖（静态导入，缺它 libmpv 连加载都失败）。出处与升级后重查导入表
的办法见 `assets/mpv-runtime/README.md`。

---

## 二、播放控件（Lua）

### uosc — `LGPL-2.1`

| 项 | 值 |
|---|---|
| 文件 | `mpv-ui\scripts\uosc\`（main.lua + lib + elements + 翻译） |
| 上游 | <https://github.com/tomasklaen/uosc>，版本 5.12.0 |
| 分发来源 | 取自 dyphire/mpv-config 2026.08.12 整合包的 `portable_config/scripts/uosc` |
| 许可正文 | `mpv-ui\LICENSE.LGPL`、`mpv-ui\scripts\uosc\LICENSE.LGPL` |

**已修改。** 本项目对 uosc 做了大量改动（宿主通道、控制条排布、无边框顶栏、配色、菜单等），
每处改动都在源码里用 `MOMOKA[槽名]` 标记，逐条清单在 `main.lua` 头部，用
`grep -rn "MOMOKA\[" assets/mpv-ui/scripts/uosc` 可枚举全部落点；散文说明见
`assets/mpv-ui/README.md`。按 LGPL-2.1 第 2 条，**这些修改部分本身也以 LGPL-2.1 授权**，
完整对应源码见 <https://github.com/cudamin/Momoka> 的 `assets/mpv-ui` 目录。

### Material Icons Round（字体） — `Apache-2.0`

| 项 | 值 |
|---|---|
| 文件 | `mpv-ui\fonts\MaterialIconsRound-Regular.otf` |
| 上游 | <https://github.com/google/material-design-icons> |
| 许可正文 | `mpv-ui\LICENSE-MaterialIcons.txt` |

### stats.lua（播放统计，简体中文覆盖版） — `LGPL-2.1-or-later`

| 项 | 值 |
|---|---|
| 文件 | `mpv-ui\scripts\stats.lua` |
| 上游 | mpv 内置脚本 `player/lua/stats.lua`（原出处 <https://github.com/Argon-/mpv-stats>） |
| 分发来源 | 取自 dyphire/mpv-config 所用的上游版本 |
| 许可正文 | `LICENSE.LGPL-2.1.txt`（exe 根目录） |

**已修改。** 本文件是 mpv 内置 `stats.lua` 的中文覆盖版：统计项（页面 / 字段 / 顺序 / 计算逻辑）
与上游一致，仅把展示文案译为简体中文。改动说明写在脚本头部。它随 mpv 的许可发布 ——
该文件本身没有独立版权头，按 mpv 仓库 `Copyright` 的规则归 `LGPL-2.1-or-later`，
与随包内核（LGPL 构建）一致。2026-10-14 之前随包的是 GPL 构建，那时这里保守地按
GPL-2.0-or-later 处理；换内核后按文件本身的许可写。

---

## 三、着色器 — 分项许可

全部以可编辑的 GLSL / HOOK 源码分发，各自版权与许可声明保留在源文件内，不随包提供编译后的
着色器缓存。逐项清单、上游地址与本地改动见 `shaders\NOTICE.txt`。

| 文件 | 许可 |
|---|---|
| `shaders\ArtCNN\*.glsl` | MIT |
| `shaders\CfL\*.glsl` | MIT |
| `shaders\Anime4K\glsl\Restore\Anime4K_Restore_CNN_Soft_M.glsl`、`Anime4K_Clamp_Highlights.glsl` | MIT（bloc97） |
| `shaders\igv\KrigBilateral.glsl` | LGPL-3.0-or-later（Shiandow） |
| `shaders\igv\SSimDownscaler.glsl`、`SSimSuperRes.glsl` | LGPL-3.0-or-later |
| `shaders\igv\adaptive-sharpen.glsl` | BSD-2-Clause |
| `shaders\ravu\*.hook` | LGPL-3.0-or-later |
| `shaders\an3223\hdeband.glsl`、`nlmeans_light.glsl` | LGPL-2.1-or-later |

许可正文：`shaders\LICENSE.LGPL-3.0.txt`、`shaders\LICENSE.GPL-3.0.txt`、
`shaders\LICENSE.LGPL-2.1.txt`。

---

## 四、框架与托管依赖

| 组件 | 许可 | 说明 |
|---|---|---|
| Microsoft Windows App SDK | Microsoft 软件许可条款（非开源） | 随包 |
| Microsoft WebView2 Runtime | Microsoft 软件许可条款（非开源） | 可选，用于「网页控制台」页 |
| CommunityToolkit.Mvvm / WinUI.* | MIT | NuGet |
| Microsoft.Extensions.DependencyInjection | MIT | NuGet |
| System.Security.Cryptography.ProtectedData | MIT | NuGet |

托管依赖各自遵循其许可，版权归各自作者。它们的许可正文由 NuGet 包自身携带。

---

## 五、许可证正文位置一览

| 文件 | 覆盖的组件 |
|---|---|
| `LICENSE` | 本项目自身（MIT） |
| `LICENSE.LGPL-2.1.txt` | libmpv-2.dll 里的 mpv 代码、an3223 着色器 |
| `LICENSE.LGPL-3.0.txt` | libmpv-2.dll 里静态链入的 FFmpeg、ravu 与 igv SSim 系列 |
| `LICENSE.GPL-3.0.txt` | 上一行 LGPL-3.0 正文引用的 GPL-3.0 条款、双许可的另一支 |
| `LICENSE.Apache-2.0.txt` | vulkan-1.dll |
| `NOTICE.txt` | 两个 dll 的出处、归属与构建判定依据 |
| `mpv-ui\LICENSE.LGPL` | uosc |
| `mpv-ui\LICENSE-MaterialIcons.txt` | Material Icons Round 字体 |
| `shaders\LICENSE.LGPL-3.0.txt` | ravu、igv SSim 系列 |
| `shaders\LICENSE.GPL-3.0.txt` | 同上（双许可的另一支） |
| `shaders\LICENSE.LGPL-2.1.txt` | an3223 着色器 |
| `shaders\NOTICE.txt` | 着色器逐项归属与本地改动 |

三份 LGPL/GPL 正文在 exe 根与 `shaders\` 下各有一份，**内容逐字节相同**：前者是
`libmpv-2.dll` 的许可，后者是那批着色器源文件指向的那一份。改一处必须同步另一处，
`tools/verify-publish.ps1` 会比对两处，防止漂移。

完整源代码：<https://github.com/cudamin/Momoka>

许可正文取自 SPDX license-list-data（MIT 与 Apache-2.0 于 2026-10-14 获取，
LGPL/GPL 三份于 2026-10-04 获取），除 `mpv-ui\LICENSE.LGPL` 为随上游分发原样保留。
