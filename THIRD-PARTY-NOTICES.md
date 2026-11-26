# 第三方组件与归属

本文件列出 Momoka 发行包内含的第三方组件、各自的许可证，以及许可证正文在包内的位置。
每条都记了出处，便于升级时重新核对。

---

## MoviePilot 日历

月历布局和交互参考 MoviePilot-Frontend `e6a30ad4b6091859999f3bbbe7cffbcce782edf1`，以下保留其组件归属与完整 MIT 许可。本地使用 WinUI 原生控件，不分发 Vue 或 FullCalendar。

```text
MoviePilot calendar layout and interaction attribution
Source: MoviePilot-Frontend, commit e6a30ad4b6091859999f3bbbe7cffbcce782edf1
Component: src/views/subscribe/FullCalendarView.vue
Adapted using native WinUI controls in Momoka. Vue and FullCalendar are not bundled.

MIT License

Copyright (c) 2023 jxxghp

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## 〇、本项目自身的许可证

**自 2026-11-21 起，Momoka 自身代码采用 `GPL-3.0-or-later`**，并附下述微软组件的有限附加许可。GNU GPL 第 3 版正文是仓库根目录的 `LICENSE`；本文件包含明确的项目授权、附加许可和原有归属，两份文件均随发布拷到 exe 根。Copyright (c) 2026 Momoka contributors。

Momoka is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

Momoka is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details. You should have received a copy of the GNU General Public License along with Momoka; if not, see <https://www.gnu.org/licenses/>.

这是项目维护者的许可选择，不是更换播放内核的结果。随包 `libmpv-2.dll` 继续使用 LGPL 构建（LGPL-2.1-or-later，静态链 LGPL-3.0 的 FFmpeg），其许可及源码义务保持不变。2026-10-14 的两轮历史判定及本次选择见 `docs/第三方组件许可合规.md`。

### Momoka Microsoft components permission

Additional permission under GNU GPL version 3 section 7

For the portions of Momoka for which the Momoka copyright holders have authority to grant this permission, those copyright holders additionally permit you to link or combine those portions with the Microsoft Windows App SDK, including WinUI 3 and the Windows App Runtime, and with Microsoft WebView2 SDK, loader, and runtime components, in unmodified forms distributed by Microsoft, and to convey the resulting work.

This permission does not require the source code of those Microsoft components to be provided under the GNU General Public License. Those components remain subject to their respective Microsoft or other applicable licenses, and their redistribution must comply with those licenses.

This permission does not grant rights in third-party code or waive obligations under its licenses. In particular, the bundled libmpv, uosc, shaders, fonts, and other third-party components retain their own licenses. Except for the permission stated above, all requirements of the GNU General Public License continue to apply to the covered Momoka code.

这项附加许可仅允许有权授权的 Momoka 代码与上述微软原版组件链接、组合并分发，不授予第三方代码的新权利，不豁免微软组件自身的分发条款，也不把 libmpv、uosc 等组件改为带此例外的 GPL。GPL 本身允许移除附加许可；如移除后仍使用这些组件，分发者须另行确认兼容性。

### 先前 MIT 代码的归属

此前按 MIT 发布的代码仍保留原有授权。以下为原根目录 LICENSE 的完整 MIT 声明，仅用于该部分代码；它不把本次或后续仅按 GPL 授权的新代码重新许可为 MIT。

```text
MIT License

Copyright (c) 2026 Momoka contributors

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the
following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO
EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
USE OR OTHER DEALINGS IN THE SOFTWARE.
```

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
| 固定源码 | [mpv commit 270367e8af7e2f49fc3d3f8da03c985f5ae3b402](https://github.com/mpv-player/mpv/tree/270367e8af7e2f49fc3d3f8da03c985f5ae3b402)；[对应构建发布页](https://github.com/zhongfly/mpv-winbuild/releases/tag/2026-10-14-270367e8af) |
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

**义务：** Momoka 自身选择 GPL-3.0-or-later 不改变随包这一份 DLL 的 LGPL 许可，
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
| Microsoft Windows App SDK / WinUI / Windows App Runtime | 随包二进制适用 Microsoft 软件许可条款 | 随包；另见上文 Momoka 有限附加许可 |
| Microsoft WebView2 SDK / loader | BSD-3-Clause（所用 NuGet 包 LICENSE.txt） | 与 Runtime 分开，保留 NuGet 包许可 |
| Microsoft WebView2 Runtime | Microsoft 软件许可条款（非开源） | 可选，用于「网页控制台」页；另见上文附加许可 |
| CommunityToolkit.Mvvm / WinUI.* | MIT | NuGet |
| Microsoft.Extensions.DependencyInjection | MIT | NuGet |
| System.Security.Cryptography.ProtectedData | MIT | NuGet |

托管依赖各自遵循其许可，版权归各自作者。它们的许可正文由 NuGet 包自身携带。

---

## 五、许可证正文位置一览

| 文件 | 覆盖的组件 |
|---|---|
| `LICENSE` | Momoka 自身代码的 GPL 第 3 版正文；项目授权为 GPL-3.0-or-later |
| `THIRD-PARTY-NOTICES.md` | 项目明确授权、微软组件有限附加许可、先前 MIT 代码、第三方归属及 Bangumi–TMDB 映射的 CC BY 4.0 正文 |
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

## WinUI Gallery 关联动画样例

封面关联动画沿用 Microsoft WinUI Gallery（MIT）固定提交 6de91feccadfbb0a796b71caedead5a2284f730e 的 ItemsRepeaterCollectionPage / DetailedInfoPage 原生调用顺序。Copyright (c) Microsoft Corporation. All rights reserved. 来源与本地适配见 [封面关联动画](docs/封面关联动画.md)。

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the Software), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## WinUI Gallery 详情悬浮卡片动效

顶部缩略卡片沿用桌面 WinUI Gallery 2.10.0（产品提交7b4314a25904c784e5a1b4bb0627f535550d88bd）的 ControlExample.xaml 显示/隐藏动画配置。Copyright (c) Microsoft Corporation. All rights reserved. 适用上节同一MIT许可证全文；来源、必要的Translation/生命周期适配及完整许可见 [详情悬浮卡片](docs/详情悬浮卡片.md)。

## WinUI Gallery 商店详情组合

商店详情的单层透明遮罩、Grid布局、CommandBar、MediaPlayerElement、WebView2及文本截断ToolTip参考 Microsoft WinUI Gallery（MIT）固定提交14a4a1a2b8ddc527dc4a7d5f7e743d7c2bc97db7。Copyright (c) Microsoft Corporation. All rights reserved. 适用上节MIT许可证全文；本地取消与二维alpha适配见开发与验证文档。

## 音乐播放栏的 Fluent 矢量图标

随机、循环（含单曲）和收藏图标取自 Microsoft Fluent UI System Icons，固定提交 `67e6dec1ee956af3ab4c746a2b3bb6a2e5520a44` 的 `assets/Arrow Shuffle`、`Arrow Repeat All`、`Arrow Repeat 1`、`Heart` 下20px SVG。仅提取填充路径用于原生 PathIcon，前景由按钮/主题提供。Copyright (c) 2020 Microsoft Corporation，MIT；适用上节保留的完整 MIT 许可正文。
来源：https://github.com/microsoft/fluentui-system-icons/tree/67e6dec1ee956af3ab4c746a2b3bb6a2e5520a44

## Bangumi–TMDB 逐集映射

Momoka 内嵌基于 **open-ani contributors** 的 Bangumi–TMDB 映射，数据使用 [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/)。对应源码和数据可从下方固定版本获取。

- 原项目与归属：[open-ani/bangumi-tmdb @ e6d78b44](https://github.com/open-ani/bangumi-tmdb/tree/e6d78b44cfc999eacabc28fa0661f9dee743de79)、[上游 NOTICE](https://github.com/open-ani/bangumi-tmdb/blob/e6d78b44cfc999eacabc28fa0661f9dee743de79/NOTICE.md)
- 初始种子：[Rhilip/BangumiExtLinker @ 8f853e6b](https://github.com/Rhilip/BangumiExtLinker/tree/8f853e6bf9d6cb382448091ce3afaf2d9cdb0a3f)，CC BY 4.0。上游保留编号、转换链接、去重并排除冲突。
- 固定 Release：[data-e6d78b44cfc999eacabc28fa0661f9dee743de79](https://github.com/open-ani/bangumi-tmdb/releases/tag/data-e6d78b44cfc999eacabc28fa0661f9dee743de79)。上游使用 Bangumi Archive `2026-11-24`，此 Release 未执行在线逐条核验。
- `manifest.json` SHA-256 `eb8a6c2829ec3d1e9e7aed8837ca24f08f21fd2b5df788bb67b4d863550ece35`。`subjects.json` SHA-256 `d97537c6d78249a3d1c61cead590b1b742f0768e8000cdced1379a8dca2c8e7b`。`episodes.json` SHA-256 `eea690afb41c753c810878c6ddc8c9fef8fc864fa106a99ebe189f003cfbb0d8`。
- Momoka 改动：保留 TV 作品编号和唯一的逐集编号关系。反向索引排除 TMDB 坐标冲突、同一 Bangumi 章节的一对多关系及电影目标，共 8,927 个作品和 205,189 个唯一坐标。通过 `tools/build-bangumi-tmdb-map.py` 排序并重编码为 gzip 内嵌资源，924,422 字节，SHA-256 `abecb5f7c46e98a838f44515de16821456cf8345e4c98c3a2ec29b72848a4874`。
- 内嵌数据只有数字。上游脚本、Bangumi CC BY-SA 资料与测试样本，以及 TMDB 元数据和图像均未随此资源分发。第三方版权与原许可仍由原权利人保留。

### CC BY 4.0 正文

以下为固定提交的 [LICENSE-DATA](https://github.com/open-ani/bangumi-tmdb/blob/e6d78b44cfc999eacabc28fa0661f9dee743de79/LICENSE-DATA)。

```text
Attribution 4.0 International

=======================================================================

Creative Commons Corporation ("Creative Commons") is not a law firm and
does not provide legal services or legal advice. Distribution of
Creative Commons public licenses does not create a lawyer-client or
other relationship. Creative Commons makes its licenses and related
information available on an "as-is" basis. Creative Commons gives no
warranties regarding its licenses, any material licensed under their
terms and conditions, or any related information. Creative Commons
disclaims all liability for damages resulting from their use to the
fullest extent possible.

Using Creative Commons Public Licenses

Creative Commons public licenses provide a standard set of terms and
conditions that creators and other rights holders may use to share
original works of authorship and other material subject to copyright
and certain other rights specified in the public license below. The
following considerations are for informational purposes only, are not
exhaustive, and do not form part of our licenses.

     Considerations for licensors: Our public licenses are
     intended for use by those authorized to give the public
     permission to use material in ways otherwise restricted by
     copyright and certain other rights. Our licenses are
     irrevocable. Licensors should read and understand the terms
     and conditions of the license they choose before applying it.
     Licensors should also secure all rights necessary before
     applying our licenses so that the public can reuse the
     material as expected. Licensors should clearly mark any
     material not subject to the license. This includes other CC-
     licensed material, or material used under an exception or
     limitation to copyright. More considerations for licensors:
     wiki.creativecommons.org/Considerations_for_licensors

     Considerations for the public: By using one of our public
     licenses, a licensor grants the public permission to use the
     licensed material under specified terms and conditions. If
     the licensor's permission is not necessary for any reason--for
     example, because of any applicable exception or limitation to
     copyright--then that use is not regulated by the license. Our
     licenses grant only permissions under copyright and certain
     other rights that a licensor has authority to grant. Use of
     the licensed material may still be restricted for other
     reasons, including because others have copyright or other
     rights in the material. A licensor may make special requests,
     such as asking that all changes be marked or described.
     Although not required by our licenses, you are encouraged to
     respect those requests where reasonable. More considerations
     for the public:
     wiki.creativecommons.org/Considerations_for_licensees

=======================================================================

Creative Commons Attribution 4.0 International Public License

By exercising the Licensed Rights (defined below), You accept and agree
to be bound by the terms and conditions of this Creative Commons
Attribution 4.0 International Public License ("Public License"). To the
extent this Public License may be interpreted as a contract, You are
granted the Licensed Rights in consideration of Your acceptance of
these terms and conditions, and the Licensor grants You such rights in
consideration of benefits the Licensor receives from making the
Licensed Material available under these terms and conditions.


Section 1 -- Definitions.

  a. Adapted Material means material subject to Copyright and Similar
     Rights that is derived from or based upon the Licensed Material
     and in which the Licensed Material is translated, altered,
     arranged, transformed, or otherwise modified in a manner requiring
     permission under the Copyright and Similar Rights held by the
     Licensor. For purposes of this Public License, where the Licensed
     Material is a musical work, performance, or sound recording,
     Adapted Material is always produced where the Licensed Material is
     synched in timed relation with a moving image.

  b. Adapter's License means the license You apply to Your Copyright
     and Similar Rights in Your contributions to Adapted Material in
     accordance with the terms and conditions of this Public License.

  c. Copyright and Similar Rights means copyright and/or similar rights
     closely related to copyright including, without limitation,
     performance, broadcast, sound recording, and Sui Generis Database
     Rights, without regard to how the rights are labeled or
     categorized. For purposes of this Public License, the rights
     specified in Section 2(b)(1)-(2) are not Copyright and Similar
     Rights.

  d. Effective Technological Measures means those measures that, in the
     absence of proper authority, may not be circumvented under laws
     fulfilling obligations under Article 11 of the WIPO Copyright
     Treaty adopted on December 20, 1996, and/or similar international
     agreements.

  e. Exceptions and Limitations means fair use, fair dealing, and/or
     any other exception or limitation to Copyright and Similar Rights
     that applies to Your use of the Licensed Material.

  f. Licensed Material means the artistic or literary work, database,
     or other material to which the Licensor applied this Public
     License.

  g. Licensed Rights means the rights granted to You subject to the
     terms and conditions of this Public License, which are limited to
     all Copyright and Similar Rights that apply to Your use of the
     Licensed Material and that the Licensor has authority to license.

  h. Licensor means the individual(s) or entity(ies) granting rights
     under this Public License.

  i. Share means to provide material to the public by any means or
     process that requires permission under the Licensed Rights, such
     as reproduction, public display, public performance, distribution,
     dissemination, communication, or importation, and to make material
     available to the public including in ways that members of the
     public may access the material from a place and at a time
     individually chosen by them.

  j. Sui Generis Database Rights means rights other than copyright
     resulting from Directive 96/9/EC of the European Parliament and of
     the Council of 11 March 1996 on the legal protection of databases,
     as amended and/or succeeded, as well as other essentially
     equivalent rights anywhere in the world.

  k. You means the individual or entity exercising the Licensed Rights
     under this Public License. Your has a corresponding meaning.


Section 2 -- Scope.

  a. License grant.

       1. Subject to the terms and conditions of this Public License,
          the Licensor hereby grants You a worldwide, royalty-free,
          non-sublicensable, non-exclusive, irrevocable license to
          exercise the Licensed Rights in the Licensed Material to:

            a. reproduce and Share the Licensed Material, in whole or
               in part; and

            b. produce, reproduce, and Share Adapted Material.

       2. Exceptions and Limitations. For the avoidance of doubt, where
          Exceptions and Limitations apply to Your use, this Public
          License does not apply, and You do not need to comply with
          its terms and conditions.

       3. Term. The term of this Public License is specified in Section
          6(a).

       4. Media and formats; technical modifications allowed. The
          Licensor authorizes You to exercise the Licensed Rights in
          all media and formats whether now known or hereafter created,
          and to make technical modifications necessary to do so. The
          Licensor waives and/or agrees not to assert any right or
          authority to forbid You from making technical modifications
          necessary to exercise the Licensed Rights, including
          technical modifications necessary to circumvent Effective
          Technological Measures. For purposes of this Public License,
          simply making modifications authorized by this Section 2(a)
          (4) never produces Adapted Material.

       5. Downstream recipients.

            a. Offer from the Licensor -- Licensed Material. Every
               recipient of the Licensed Material automatically
               receives an offer from the Licensor to exercise the
               Licensed Rights under the terms and conditions of this
               Public License.

            b. No downstream restrictions. You may not offer or impose
               any additional or different terms or conditions on, or
               apply any Effective Technological Measures to, the
               Licensed Material if doing so restricts exercise of the
               Licensed Rights by any recipient of the Licensed
               Material.

       6. No endorsement. Nothing in this Public License constitutes or
          may be construed as permission to assert or imply that You
          are, or that Your use of the Licensed Material is, connected
          with, or sponsored, endorsed, or granted official status by,
          the Licensor or others designated to receive attribution as
          provided in Section 3(a)(1)(A)(i).

  b. Other rights.

       1. Moral rights, such as the right of integrity, are not
          licensed under this Public License, nor are publicity,
          privacy, and/or other similar personality rights; however, to
          the extent possible, the Licensor waives and/or agrees not to
          assert any such rights held by the Licensor to the limited
          extent necessary to allow You to exercise the Licensed
          Rights, but not otherwise.

       2. Patent and trademark rights are not licensed under this
          Public License.

       3. To the extent possible, the Licensor waives any right to
          collect royalties from You for the exercise of the Licensed
          Rights, whether directly or through a collecting society
          under any voluntary or waivable statutory or compulsory
          licensing scheme. In all other cases the Licensor expressly
          reserves any right to collect such royalties.


Section 3 -- License Conditions.

Your exercise of the Licensed Rights is expressly made subject to the
following conditions.

  a. Attribution.

       1. If You Share the Licensed Material (including in modified
          form), You must:

            a. retain the following if it is supplied by the Licensor
               with the Licensed Material:

                 i. identification of the creator(s) of the Licensed
                    Material and any others designated to receive
                    attribution, in any reasonable manner requested by
                    the Licensor (including by pseudonym if
                    designated);

                ii. a copyright notice;

               iii. a notice that refers to this Public License;

                iv. a notice that refers to the disclaimer of
                    warranties;

                 v. a URI or hyperlink to the Licensed Material to the
                    extent reasonably practicable;

            b. indicate if You modified the Licensed Material and
               retain an indication of any previous modifications; and

            c. indicate the Licensed Material is licensed under this
               Public License, and include the text of, or the URI or
               hyperlink to, this Public License.

       2. You may satisfy the conditions in Section 3(a)(1) in any
          reasonable manner based on the medium, means, and context in
          which You Share the Licensed Material. For example, it may be
          reasonable to satisfy the conditions by providing a URI or
          hyperlink to a resource that includes the required
          information.

       3. If requested by the Licensor, You must remove any of the
          information required by Section 3(a)(1)(A) to the extent
          reasonably practicable.

       4. If You Share Adapted Material You produce, the Adapter's
          License You apply must not prevent recipients of the Adapted
          Material from complying with this Public License.


Section 4 -- Sui Generis Database Rights.

Where the Licensed Rights include Sui Generis Database Rights that
apply to Your use of the Licensed Material:

  a. for the avoidance of doubt, Section 2(a)(1) grants You the right
     to extract, reuse, reproduce, and Share all or a substantial
     portion of the contents of the database;

  b. if You include all or a substantial portion of the database
     contents in a database in which You have Sui Generis Database
     Rights, then the database in which You have Sui Generis Database
     Rights (but not its individual contents) is Adapted Material; and

  c. You must comply with the conditions in Section 3(a) if You Share
     all or a substantial portion of the contents of the database.

For the avoidance of doubt, this Section 4 supplements and does not
replace Your obligations under this Public License where the Licensed
Rights include other Copyright and Similar Rights.


Section 5 -- Disclaimer of Warranties and Limitation of Liability.

  a. UNLESS OTHERWISE SEPARATELY UNDERTAKEN BY THE LICENSOR, TO THE
     EXTENT POSSIBLE, THE LICENSOR OFFERS THE LICENSED MATERIAL AS-IS
     AND AS-AVAILABLE, AND MAKES NO REPRESENTATIONS OR WARRANTIES OF
     ANY KIND CONCERNING THE LICENSED MATERIAL, WHETHER EXPRESS,
     IMPLIED, STATUTORY, OR OTHER. THIS INCLUDES, WITHOUT LIMITATION,
     WARRANTIES OF TITLE, MERCHANTABILITY, FITNESS FOR A PARTICULAR
     PURPOSE, NON-INFRINGEMENT, ABSENCE OF LATENT OR OTHER DEFECTS,
     ACCURACY, OR THE PRESENCE OR ABSENCE OF ERRORS, WHETHER OR NOT
     KNOWN OR DISCOVERABLE. WHERE DISCLAIMERS OF WARRANTIES ARE NOT
     ALLOWED IN FULL OR IN PART, THIS DISCLAIMER MAY NOT APPLY TO YOU.

  b. TO THE EXTENT POSSIBLE, IN NO EVENT WILL THE LICENSOR BE LIABLE
     TO YOU ON ANY LEGAL THEORY (INCLUDING, WITHOUT LIMITATION,
     NEGLIGENCE) OR OTHERWISE FOR ANY DIRECT, SPECIAL, INDIRECT,
     INCIDENTAL, CONSEQUENTIAL, PUNITIVE, EXEMPLARY, OR OTHER LOSSES,
     COSTS, EXPENSES, OR DAMAGES ARISING OUT OF THIS PUBLIC LICENSE OR
     USE OF THE LICENSED MATERIAL, EVEN IF THE LICENSOR HAS BEEN
     ADVISED OF THE POSSIBILITY OF SUCH LOSSES, COSTS, EXPENSES, OR
     DAMAGES. WHERE A LIMITATION OF LIABILITY IS NOT ALLOWED IN FULL OR
     IN PART, THIS LIMITATION MAY NOT APPLY TO YOU.

  c. The disclaimer of warranties and limitation of liability provided
     above shall be interpreted in a manner that, to the extent
     possible, most closely approximates an absolute disclaimer and
     waiver of all liability.


Section 6 -- Term and Termination.

  a. This Public License applies for the term of the Copyright and
     Similar Rights licensed here. However, if You fail to comply with
     this Public License, then Your rights under this Public License
     terminate automatically.

  b. Where Your right to use the Licensed Material has terminated under
     Section 6(a), it reinstates:

       1. automatically as of the date the violation is cured, provided
          it is cured within 30 days of Your discovery of the
          violation; or

       2. upon express reinstatement by the Licensor.

     For the avoidance of doubt, this Section 6(b) does not affect any
     right the Licensor may have to seek remedies for Your violations
     of this Public License.

  c. For the avoidance of doubt, the Licensor may also offer the
     Licensed Material under separate terms or conditions or stop
     distributing the Licensed Material at any time; however, doing so
     will not terminate this Public License.

  d. Sections 1, 5, 6, 7, and 8 survive termination of this Public
     License.


Section 7 -- Other Terms and Conditions.

  a. The Licensor shall not be bound by any additional or different
     terms or conditions communicated by You unless expressly agreed.

  b. Any arrangements, understandings, or agreements regarding the
     Licensed Material not stated herein are separate from and
     independent of the terms and conditions of this Public License.


Section 8 -- Interpretation.

  a. For the avoidance of doubt, this Public License does not, and
     shall not be interpreted to, reduce, limit, restrict, or impose
     conditions on any use of the Licensed Material that could lawfully
     be made without permission under this Public License.

  b. To the extent possible, if any provision of this Public License is
     deemed unenforceable, it shall be automatically reformed to the
     minimum extent necessary to make it enforceable. If the provision
     cannot be reformed, it shall be severed from this Public License
     without affecting the enforceability of the remaining terms and
     conditions.

  c. No term or condition of this Public License will be waived and no
     failure to comply consented to unless expressly agreed to by the
     Licensor.

  d. Nothing in this Public License constitutes or may be interpreted
     as a limitation upon, or waiver of, any privileges and immunities
     that apply to the Licensor or You, including from the legal
     processes of any jurisdiction or authority.


=======================================================================

Creative Commons is not a party to its public licenses.
Notwithstanding, Creative Commons may elect to apply one of its public
licenses to material it publishes and in those instances will be
considered the “Licensor.” The text of the Creative Commons public
licenses is dedicated to the public domain under the CC0 Public Domain
Dedication. Except for the limited purpose of indicating that material
is shared under a Creative Commons public license or as otherwise
permitted by the Creative Commons policies published at
creativecommons.org/policies, Creative Commons does not authorize the
use of the trademark "Creative Commons" or any other trademark or logo
of Creative Commons without its prior written consent including,
without limitation, in connection with any unauthorized modifications
to any of its public licenses or any other arrangements,
understandings, or agreements concerning use of licensed material. For
the avoidance of doubt, this paragraph does not form part of the public
licenses.

Creative Commons may be contacted at creativecommons.org.
```
