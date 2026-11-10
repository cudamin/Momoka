---
name: "momoka-danmaku"
description: "Use when developing, reviewing or troubleshooting Momoka danmaku (弹幕) from DandanPlay (弹弹play), self-hosted huangxd-/danmu_api, or both: source selection, authentication, matching, aggregation, settings, rendering and sending. Covers Core/Danmaku, DandanPlayClient*, DanmuApiEndpoint, DanmakuSourceRules, DanmakuMatchRules, DanmakuSettings, SettingsViewModel.Danmaku, PlayerViewModel.Danmaku*, momoka_danmaku.lua, uosc DanmakuInput and website/worker credentials. General subtitle styling belongs to momoka-subtitles; playback pipeline and backend lifetimes belong to momoka-playback."
metadata: {agent_created: true}
---

# Momoka — 弹幕来源与播放

规则归 [CLAUDE.md](../../../CLAUDE.md)，命令以 [开发与验证](../../../docs/开发与验证.md) 为准。功能说明见 [弹幕与快捷键](../../../docs/弹幕与快捷键.md)。本技能统一负责弹弹play、用户自部署的 `danmu_api`、客户端聚合、弹幕设置、独立 OSD 与发送链路；播放后端和管线生命周期归 [momoka-playback](../momoka-playback/SKILL.md)，字幕归 [momoka-subtitles](../momoka-subtitles/SKILL.md)，界面显隐通则归 [momoka-winui-shell](../momoka-winui-shell/SKILL.md)。

## 先确定来源和证据

1. 先查 `DanmakuSettings.Source`、`DanmakuSourceRules` 和本次操作的具体来源。`Both` 是选择模式，每次 HTTP 请求仍只属于一个 `DanmakuSource`；两源的 `episodeId` 可以重号。
2. 按问题读取 [弹弹play 协议](references/protocol.md)、[内置凭据端点](references/credential-endpoint.md) 或 [danmu_api 协议与上游核对](references/danmu-api.md)。后者记录已读上游提交和真实路由，不把 README 的兼容宣称当作 Momoka 已实现的能力。
3. 以当前源码和测试确认本地行为。上游更新时核对受影响路由、鉴权、响应与缓存；服务在线、共享额度和部署参数必须分别核实，源码不证明真实服务可用。

| 边界 | 弹弹play | danmu_api |
| --- | --- | --- |
| 服务地址 | 固定官方 `api.dandanplay.net` | 用户部署的根地址，可含反向代理前缀；Momoka 不提供公共服务 |
| 鉴权 | Momoka 内置应用凭据，三个签名头 | 可选访问令牌，转义后放入 URL 路径；不签名，不取内置凭据 |
| 自动匹配 | 标题与集号搜索，再判断唯一精确标题 | `POST /api/v2/match`，正文只有 `fileName` |
| 手动搜索 | 一次选择这个来源 | 一次选择这个来源 |
| 弹幕读取 | v2 JSON；可包含关联弹幕 | 兼容的 v2 JSON；不启用弹弹play 的关联选项 |
| 发送 | 只写本应用的 `/app` 私有库 | 当前产品没有发送路径 |
| 重定向 | 搜索和发送拒绝；读取仅允许官方 HTTPS 子域 | 搜索、读取、match 全部拒绝 |

`DandanPlayClient` 这个现有类名承载两源传输。技能改名不要求改类名，也不意味着增加用户 OAuth、主弹幕库写入、文件哈希识别、上游管理或本地弹幕上传能力。

## 代码入口

| 位置 | 职责 |
| --- | --- |
| `src/Momoka.Core/Danmaku/DandanPlayClient.cs` | 按来源连接、搜索与读取、鉴权、重定向、限长、缓存和关闭时排空请求 |
| `src/Momoka.Core/Danmaku/DandanPlayClient.Match.cs` / `DandanPlayClient.Send.cs` | danmu_api 自动匹配 / 弹弹play私有库发送 |
| `src/Momoka.Core/Danmaku/DanmuApiEndpoint.cs`、`src/Momoka.Core/Configuration/DanmuApiToken.cs` | 自部署地址、路径令牌与 DPAPI |
| `src/Momoka.Core/Danmaku/DanmakuSourceRules.cs` / `DanmakuSource.cs` | 模式、来源身份、本地可用性、手动来源与发送能力 |
| `src/Momoka.Core/Danmaku/DandanPlayMatching.cs` / `DanmakuMatchRules.cs` | 官方唯一精确匹配 / 自部署文件名与返回集号校验 |
| `src/Momoka.Core/Danmaku/DanmakuComment.cs` | 两源共用解析、排序合并、屏蔽与去重 |
| `src/Momoka.Core/Danmaku/DandanPlayBuiltIn.cs` / `DandanPlayBuiltInCredentialSource.cs` / `IDandanPlayCredentialSource.cs` | 内置端点参数、解密、刷新、缓存与离线凭据替换点 |
| `src/Momoka.Core/Configuration/DanmakuSettings.cs` / `SettingsPreferences.cs` | 当前默认值、归一化、外观恢复与连接备份边界 |
| `src/Momoka.Shell/ViewModels/SettingsViewModel.Danmaku.cs` | 四张卡、原地更新与显隐恢复 |
| `src/Momoka.Shell/ViewModels/PlayerViewModel.Danmaku.cs` / `PlayerViewModel.DanmakuSend.cs` | 匹配、加载、聚合、播放身份、脚本传输与发送状态 |
| `src/Momoka.Shell/Views/PlayerPage.Danmaku.cs`、`src/Momoka.Shell/Views/DanmakuPanel.xaml` | 集成页和独占面板的入口 |
| `src/Momoka.Core/Mpv/MpvDanmaku.cs`、`assets/mpv-ui/scripts/momoka_danmaku.lua` | 内置两条管线共用消息契约与独立 OSD |
| `assets/mpv-ui/scripts/uosc/elements/DanmakuInput.lua`、`assets/mpv-ui/scripts/uosc/main.lua` | 独占输入栏与注册；升级 uosc 时保留补丁 |
| `tests/Momoka.Tests/DanmakuTests.cs`、`src/Momoka.Shell/ShellNavigationProbe.Danmaku.cs` | 离线协议、设置与聚合测试；Shell 设置/面板/发送探针 |
| `website/worker/credential.js`、`website/worker/index.js` | 本仓已跟踪的凭据加密与 Worker 路由；线上部署另行核对 |

## 匹配、加载与聚合

- 自动入口共用 `DanmakuMatchRules.CanAutoMatch`：电影，或第一季且集号为正的单集。续季、特别篇和其他类型不发自动请求，保留手动选择。
- 弹弹play按元数据标题搜索；`SeriesName` 优先，Unicode 归一化后只比较字母数字、忽略大小写。`UniqueMatch` 只收唯一精确结果；`HasMore`、本地截断和多个候选都不能证明唯一。
- danmu_api 使用 `DanmakuMatchRules.MatchFileName` 拼出 `标题 (年份) S01E02`，电影省略季集号。单集优先用所属剧首播年，取不到才退回本集年份。`DanmakuSeriesYearAsync` 可能只读媒体服务器取得剧年份；不会下载视频来算哈希。`MatchAsync` 只取有效的 `matches[0]`，再由 `Accepts` 拒绝明确集号冲突；结果未写集号时不据此拒绝，也不能宣称已验证集号一致。
- `Both` 自动流程依次匹配两源，再由 `LoadSourcesAsync` 依次读取成功匹配的选择。任一源有匹配就继续；另一源的匹配错误目前不会带入加载后的状态。读取阶段一源失败时保留成功来源，并显示读取失败警告。
- 成功返回空 `comments` 也算已加载，记录为零条；全部读取失败才保持未加载。弹弹play成功但零条仍可建立发送目标。
- **手动一次只搜索和加载一源。** `AvailableSources` 列出模式包含且本地配置有效的来源；`LoadDanmakuAsync` 只传一个选择并替换原加载结果。不能把手动选择描述成逐次累加两源。
- `DanmakuComments.Combine` 按时间排序，先取最终最多 40,000 条；它不去重。随后 `Filter` 根据类型、颜色、屏蔽词和 `Deduplicate` 过滤；开启去重时相同文本忽略大小写、距上次保留项严格小于 10 秒才丢弃。
- 保留来源身份贯穿请求、缓存、选择、数量与发送目标。不要用一个裸 `episodeId` 代替两源选择，也不要把 danmu_api 服务端自己的多平台聚合等同于 Momoka 的 `Both`。

## 连接、凭据与缓存

- `IsSourceReady` / `IsAvailable` 是本地前置条件：总开关开启、使用内置 libmpv；danmu_api 还要求地址合法。它们不验证服务在线、令牌可解密或凭据有效。`Both` 缺 danmu_api 地址仍可使用弹弹play；实际请求在 `ResolveAsync` 才解密并校验令牌。
- 弹弹play只使用内置凭据。已退役的用户 AppId/AppSecret 不恢复为额度耗尽时的兜底；danmu_api 的访问令牌是另一套凭据。内置密文下发和 DPAPI 缓存不提供应用密钥的保密保证，额度由所有内置用户共享。
- danmu_api 地址与令牌分开填写；令牌保存后不回填明文。`DanmuApiEndpoint` 保留地址路径前缀、补末尾斜杠、追加转义令牌段，再接 `api/v2/...`。地址不含账号、查询或片段；诊断时隐藏完整令牌路径。
- 两源 GET 共用每次 30 秒排队与读取预算、32 MiB 响应上限、两小时缓存和最多 16 份 JSON。缓存文件名只用哈希：官方按应用与凭据身份隔离，自部署按地址与令牌摘要隔离。失败不缓存；`MatchAsync` 的 POST 不走 GET 缓存。
- 只有官方 GET 的 401/403 会使内置凭据失效并换一份重试一次。danmu_api 鉴权失败直接提示检查令牌；发送不自动重试。错误正文和未知鉴权头不回显。
- 修改客户端关闭流程时，保持 `ShutdownAsync` 停止接收、取消并等待所有活动操作退出后再释放 HTTP 和请求闸门，包含缓存解析和已被新请求替代的旧操作。

## 设置、渲染与发送

- 分类 `弹幕` 下是四张卡：`弹幕`、`来源与聚合`、`显示与外观`、`类型与屏蔽`。以当前 `DanmakuSettings` 读取默认值；本次核对值为 `Enabled=false`、`Source=Both`、`ShowInput=false`，不要沿用早期“默认单源、输入栏开启”的说明。
- 内置后端关闭服务时保留启用行；danmu_api 地址无效时保留来源配置恢复入口。外部 mpv 隐藏整个弹幕分类、播放入口和快捷键，切回恢复偏好。稳定卡片按标题 `UpdateRows`，空卡完整收起；使用 `DanmakuCards` / `DanmakuRows`，不要 `Single(Category == "弹幕")`。
- `WithRelated` 及其设置只属于弹弹play，Shell 向 danmu_api 传 `false`。纯 danmu_api 隐藏输入栏和发送相关设置；`CanSend(mode)` 仅表示模式包含弹弹play。真正发送还要求正在播放、显示和输入栏开启、读取已成功，且 `_danmakuEpisode` 指向已加载的弹弹play库。Both 仅加载 danmu_api 时不能发送。
- 发送提示在 Both 下写“发到弹弹play”；只写 `/api/v2/comment/{episodeId}/app` 本应用私有库。必须同时有 `success=true`、`errorCode=0`、`cid>0` 才确认成功；未确认结果可能已经送达。请求派发后清两种关联 GET 缓存，成功只追加本地评论，不额外读取。
- `Enabled`、`Source`、自部署地址和受保护令牌属于本机连接：不导出，不被偏好恢复覆盖。`RestoreAppearance` 保留连接、匹配、显示开关、类型与屏蔽列表。
- 分类名和设置跳转是跨文件契约：修改时一起核对 `CardCategories`、`SettingsCategoryGlyphConverter`、`PlayerSettingsLinks`、探针与 `MpvUiTests`；跳转令牌保持 `danmaku`，服务名“弹弹play”不等于分类名。
- 两源都解析 `p/m`：模式只接受 1/4/5，时间 0–86400 秒且有限，颜色 24 位，去控制字符、文本最多 300 字符。宿主过滤后按 `begin → chunk(128 条) → commit` 传给 Lua；文件序号与传输序号拒绝迟到数据。
- Lua 只做时间轴、轨道和 ASS OSD，不联网、不读媒体或凭据；不占用主/次字幕轨。外观共用 `MpvDanmaku.RenderOptions`；智能防挡指本地防重叠，屏蔽词是本机列表，不承诺人像遮罩、平台账号同步或高级脚本弹幕。
- 宿主负责发送校验、HTTP 与回执；`DanmakuInput` 只编辑草稿并回传消息。输入时播放快捷键让路，换片、换库及输入上下文变化隔离旧回执。临时隐藏保留恢复入口与偏好。升级 uosc 后检查 `main.lua` 仍实例化 `DanmakuInput`。

## 验证与维护

按 CLAUDE.md 选择改动对应检查；技能文字维护只检查内容、链接、命令和差异。产品逻辑改动先编译当前 Release 测试项目，再运行正式测试或 `danmaku-fixture` 参数；该参数属于测试程序，不是 Shell 开关。

```powershell
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build -- danmaku-fixture
py tools/test-danmaku.py --output work/danmaku-lua-new
py tools/test-uosc.py --output work/uosc-new
node --test website/worker/credential.test.mjs
```

- Lua 检查使用尚不存在的输出目录。协议测试用假 HTTP、固定凭据和临时缓存；真实发送会写入真实弹幕并消耗共享额度，按 CLAUDE.md 的授权边界执行。
- 设置/入口/发送 UI 改动用正式 Shell 的 `--probe-shell --probe-section settings,playback --screen 1`，检查报告和截图。它与 Lua 测试都不证明真实服务、独占真实播放或外部 mpv 覆盖。
- 改凭据信封时同步 `website/worker/credential.js`、客户端解析、Worker 测试和 `DanmakuTests` 跨语言向量；本仓文件与生产部署分别核对。
- 更新功能文档、PROGRESS、本技能和受影响参考，按 CLAUDE.md 提交。技能改名同步规则索引与 `tools/check-review-docs.py`；历史报告中的旧路径作为当时证据保留，用户级技能安装另行处理。
- 使用 [技能复核场景](references/validation.md) 检查两源边界和触发范围；人工走读不等于测得自动触发率。
