# libmpv 的运行时依赖

这里放一个 dll：`vulkan-1.dll`。它是 `libmpv-2.dll` **唯一一个不属于 Windows 系统的依赖**，
由 `src/Momoka.Shell/Momoka.Shell.csproj` 当普通内容文件拷到 exe 旁边，普通构建和发布都拷。

2026-10-14 起这里还放许可正文，跟着一起拷到 exe 根目录：`LICENSE.Apache-2.0.txt`
（`vulkan-1.dll` 的许可）、`LICENSE.LGPL-2.1.txt` / `LICENSE.LGPL-3.0.txt` / `LICENSE.GPL-3.0.txt`
（`libmpv-2.dll` 的许可），以及 `NOTICE.txt`（两个 dll 的出处与归属，并记录「这份 libmpv 为什么是
LGPL 构建」的判定依据）。项目自身的许可正文是仓库根的 `LICENSE`（MIT）。
在这之前发布包里**一份许可文本都没有** —— `libmpv-2.dll` 就在 exe 旁边，许可正文却不在，
这是发行上的缺口。

那三份 LGPL/GPL 文本与 `assets/shaders` 下同名的那几份**逐字节相同**（着色器源文件指向它们，
2026-10-04 入库）。放两份是因为发布时它们要落在两个目录：exe 根（`libmpv-2.dll` 旁边）与
`shaders\`（着色器旁边）。`tools/verify-publish.ps1` 逐字节比对这两处，防止改了一处忘了另一处。

2026-09-04 入库。**从前它不在仓库里** —— 发布时从 `C:\mpv_config-2026.08.12`（用户自己那套便携版 mpv）
的根目录现拷，也就是说这个程序能不能发布、发出来能不能起播，取决于另一个软件的安装目录还在不在。
着色器文件 2026-09-03 因为同一个理由入库（见 `assets/shaders/README.md`），这是同一件事的最后一块。

| 项 | 值 |
|---|---|
| 出处 | `C:\mpv_config-2026.08.12\vulkan-1.dll`，一字节不差 |
| 版本 | 1.4.359.0（`1.4.359.Dev Build`，Vulkan Runtime 的 loader） |
| 大小 | 581,120 字节 |
| SHA256 | `13dd448a97495763b5cce84b8ade84cd2fa13c71f7e871f3c7d509eca845ad0f` |
| 许可 | Apache-2.0（Khronos Vulkan-Loader） |

## 播放内核本身：仓库根的 `libmpv-2.dll`

它不在这个目录里，但许可与出处记在这里一起看。2026-10-14 换过一次：从 GPL 构建换成 LGPL 构建，
为的是让项目自身不必再被 GPL 传染（来龙去脉见 `docs/第三方组件许可合规.md`）。

| 项 | 值 |
|---|---|
| 出处 | zhongfly/mpv-winbuild 的 release `2026-10-14-270367e8af`，资产 `mpv-dev-lgpl-x86_64-20261014-git-270367e8af.7z` |
| 版本 | `mpv v0.41.0-1163-g270367e8a`（提交 `270367e8af…`），构建于 2026-10-14 03:08:54 |
| 归档 SHA256 | `1488b4458279561a4a0134ddf837989dcc8045308ac521657a98a284bcd8529d`（与发布页 `sha256.txt` 一致） |
| 文件 SHA256 | `fe583af31036bf3f3adacf6659e1bd448b568091b0334675573a3e303ec89d17` |
| 大小 | 101,242,880 字节（96.6 MiB） |
| 许可 | LGPL-2.1-or-later，静态链 LGPL-3.0 的 FFmpeg |

换之前那份（GPL 构建，`mpv v0.41.0-923-g7b8915bc1`，SHA256 `401d0a1e30233aecb5b5c1f1522c1f6e0f4d32e735ab18ea03e6094a9e07e824`）
不再随包。`libmpv-2.dll` 在 `.gitignore` 里，git 不兜底 —— 换内核时先在 `work/` 留一份旧文件再动，
否则没有退路。

## 为什么它必须跟着发

两条，各自都足够：

- **它是 `libmpv-2.dll` 的静态导入**（这份 dll 里一张延迟加载表都没有），所以找不到它的机器上
  libmpv 连加载都失败，一帧都放不出来 —— 不是「切到 vulkan 才用得上」。
- **图形接口的出厂默认就是 `vulkan`**（v8 起，见 `MpvRenderCheck.PreferredApi`）。

**不能指望系统里那一份**：`C:\Windows\System32\vulkan-1.dll` 是显卡驱动装的，没有 Vulkan 驱动的机器上
就没有这个文件；而且它常常更旧 —— 这台机器上系统那份是 1.3.250，箱子里这份是 1.4.359。

## 为什么没有 `lua51.dll`

从前发布时是把那个 mpv 目录根下**所有** dll 拷过来，也就是 `lua51.dll` 和 `vulkan-1.dll` 两个。
前者是白拷的 734 KB：**`libmpv-2.dll` 根本不加载它** —— 导入表里没有，延迟加载表不存在，
整个二进制里连 `lua51.dll` 这个字符串都不出现（LuaJIT 2.1 是静态编译进去的，
`--version` 的功能表里那个 `luajit` 指的就是它）。所以 2026-09-04 起不再装箱。

## 升级 `libmpv-2.dll` 之后要做的一件事

**重新读一遍它的导入表**，看有没有出现新的、不属于系统的 dll；有就照上面的样子一起放进这个目录。
办法是直接解析 PE 的导入目录（DOS 头 `e_lfanew` → PE 头 → 数据目录第 1 项是导入、第 13 项是延迟加载，
RVA 用节表换成文件偏移，逐个描述符读 dll 名）。

2026-10-14 换成 LGPL 构建后重读过一次：**47 个静态导入，除 `vulkan-1.dll` 之外全是 Windows 自带的**
（`KERNEL32`、`USER32`、`d2d1`、`SHCORE`、`IPHLPAPI`、`WLDAP32`、`AVICAP32`、`Normaliz` 那一批），
延迟加载表为空 —— 非系统导入的集合与换之前那份完全相同。也就是说换内核没有带来新的随包 dll。

## 换内核之后还要看的另一件事

`libmpv-2.dll` 的**文件版本资源**是「关于」那张卡上显示的那一行（`AboutFacts.PlaybackCore`）。
换完从 `v0.41.0-923-g7b8915bc1` 变成 `v0.41.0-1163-g270367e8a`，是预期内的可见变化。
