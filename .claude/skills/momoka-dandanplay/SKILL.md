---
name: "momoka-dandanplay"
description: "Momoka 的弹弹play（DandanPlay）开放弹幕网络接入：应用签名、标题搜索与唯一精确匹配、弹幕获取与发送、内置凭据端点与 Cloudflare Secret、设置页「弹幕」卡片显隐，以及离线与真实服务验证边界。Use when touching src/Momoka.Core/Danmaku/*, DandanPlayClient*.cs, DandanPlayBuiltIn*.cs, DanmakuSettings.cs, SettingsViewModel.Danmaku.cs, PlayerViewModel.Danmaku*.cs, assets/mpv-ui/scripts/momoka_danmaku.lua, uosc DanmakuInput, or the website credential endpoint. Not for general subtitle rendering or the mpv pipeline itself."
metadata: {agent_created: true}
---

# Momoka — 弹弹play 弹幕接入

规则归 [CLAUDE.md](../../../CLAUDE.md)：凭据、验证、提交与真实服务边界以它为准。功能说明见 [docs/弹幕与快捷键.md](../../../docs/弹幕与快捷键.md)。播放管线归 [momoka-playback](../momoka-playback/SKILL.md)，字幕归 [momoka-subtitles](../momoka-subtitles/SKILL.md)，界面显隐通则归 [momoka-winui-shell](../momoka-winui-shell/SKILL.md)。本技能只负责弹弹play 协议、匹配、凭据、设置与发送链路。

**当前产品边界：** 只用应用签名访问官方 v2 搜索、弹幕读取与本应用私有弹幕写入；没有用户登录/OAuth，也没有写入弹弹play 主弹幕库的入口。不要把 `/app` 的成功写成主库发送，也不要凭接口标题猜一个未实现的登录或写主库流程。

## 先取证据

- 官方入口：[开放弹幕网络](https://doc.dandanplay.com/open/)、[Swagger](https://api.dandanplay.net/swagger)、[开发者中心](https://dev.dandanplay.com)。公开页说明接口形状；**应用额度、审核状态与可用性以开发者中心为准**。
- 当前行为的依据是源码与 `DanmakuTests`，不是历史截图。公开页的接口说明不等于应用后台额度：后台有日/月限额，受限时返回 429。
- 2026-10-25 后台观测的基础层额度：搜索 100/日、获取弹幕 100/日、发送 10/日（各约 2,400/月），所有内置用户共享。2026-10-26 生产端点实测 200。这些都是观测值，重新核对后同步文档与技能。
- 不把真实 AppSecret 写进仓库、聊天、URL、日志或命令行；验证内置端点时只输出应用 ID、长度和有效期。

## 地形

| 位置 | 管什么 |
| --- | --- |
| `src/Momoka.Core/Danmaku/DandanPlayClient.cs` | 签名、GET、重定向、错误映射、30 秒预算、32 MiB 上限、两小时缓存 |
| `src/Momoka.Core/Danmaku/DandanPlayClient.Send.cs` | `POST /app`、发送校验、失败语义、缓存失效 |
| `src/Momoka.Core/Danmaku/DandanPlayMatching.cs` | 标题/集数提取、唯一精确匹配、`IsConfigured`、`CanRender` |
| `src/Momoka.Core/Danmaku/DanmakuComment.cs` | `p`/`m` 解析、模式/颜色/文本限制、屏蔽与去重 |
| `src/Momoka.Core/Danmaku/DandanPlayBuiltIn.cs` | 端点、AES 密钥、响应上限、提前换新间隔 |
| `src/Momoka.Core/Danmaku/DandanPlayBuiltInCredentialSource.cs` | 取密文、解密、DPAPI 缓存、过期与失败回退 |
| `src/Momoka.Core/Configuration/DanmakuSettings.cs` | 偏好默认值、`Auto/BuiltIn/Custom` 归一化 |
| `src/Momoka.Core/Configuration/DandanPlayCredentials.cs` | 自定义 AppSecret 的 DPAPI 存取 |
| `src/Momoka.Shell/ViewModels/SettingsViewModel.Danmaku.cs` | 「弹幕」卡片、内置/自定义显隐、重载 |
| `src/Momoka.Shell/ViewModels/PlayerViewModel.Danmaku.cs` + `.DanmakuSend.cs` | 自动匹配、加载、分块推送、发送状态 |
| `src/Momoka.Shell/Views/PlayerPage.Danmaku.cs`、`Views/DanmakuPanel.xaml(.cs)` | 集成页与独占面板的入口 |
| `src/Momoka.Core/Mpv/MpvDanmaku.cs`、`assets/mpv-ui/scripts/momoka_danmaku.lua` | 两条内置管线共用的脚本消息契约与独立 OSD 图层 |
| `assets/mpv-ui/scripts/uosc/elements/DanmakuInput.lua`、`uosc/main.lua` | 独占模式输入栏；升级 uosc 时最容易掉的补丁 |
| `tests/Momoka.Tests/DanmakuTests.cs` | 协议、缓存、重定向、发送、内置凭据的离线回归 |
| `src/Momoka.Shell/ShellNavigationProbe.Danmaku*.cs`、`tests/Momoka.Tests/Lua/uosc-interaction-tests.lua` | 设置/发送/面板与 uosc 的隔离检查 |

端点部署在独立仓库 `cudamin/Mmoka-web-cloudflare-`（站点 `yuanshenqidon.dpdns.org`），不在本仓库。`website/` 若出现在本树，是临时镜像，不能当作干净检出的依赖。

## 协议要点

细节、报文与错误表见 [协议参考](references/protocol.md)。

- **签名**：`base64(SHA256(appId + timestamp + path + appSecret))`，路径只取 `RequestUri.AbsolutePath`，**不含查询串**。请求头是 `X-AppId`、`X-Timestamp`（Unix 秒）、`X-Signature`。时间偏差会得到 `Invalid Timestamp`。
- **搜索**：`GET /api/v2/search/episodes?anime=<标题>&v2=true[&episode=N]`。标题 2–120 字符；集数留空搜全部，最多 6 位正整数。响应是 `animes[].episodes[]`，客户端平展成 `episodeId` 列表；本地最多 1000 条，`hasMore` 或本地截断都不能当作唯一结果。
- **取弹幕**：`GET /api/v2/comment/{episodeId}?withRelated=<bool>&chConvert=0`。`p` 至少三段：`time,mode,color,...`，`m` 是文本。只接受模式 1/4/5、时间 0–86400 秒、颜色 0–0xFFFFFF；控制字符剔除，文本截到 300，最多 40,000 条。
- **发送**：`POST /api/v2/comment/{episodeId}/app`，JSON 为 `{time, mode: 1, color: 0xFFFFFF, comment, userName: "Momoka"}`。文本 1–100 个 UTF-16 字符、无控制字符；时间先减去当前弹幕延迟，范围 0–86400。必须同时拿到 `success=true`、`errorCode=0`、`cid>0` 才算成功。**只进本应用私有库。**
- **重定向**：取弹幕允许 302，但只接受 `dandanplay.net` 的 HTTPS 443 子域、无 URL 用户信息；同源跳转按新路径重签，跨来源不带鉴权头。搜索不跟随跳转。发送不跟随、不自动重试。
- **错误**：401/403 只映射 `X-Error-Message` 的已知值；未知响应头和正文一律不回显（可能夹带凭据）。429 是额度/频率受限。所有 GET 共用 30 秒排队+读取预算，响应上限 32 MiB。
- **缓存**：成功结果按「应用 + 凭据身份 + 路径」隔离，两小时有效、最多 16 份；失败不缓存。发送一旦发出，成功或结果不明都删掉该库的两种关联缓存，不额外消耗一次读取额度。
- **内置凭据的 401/403**：GET 会丢掉缓存、换一份再试一次；POST 不自动重试，避免重复写入。自定义凭据不访问内置端点。

## 凭据模型

- 默认 `CredentialSource = BuiltIn`：首次使用向 Momoka 端点取 AES-GCM 密文，解密后用 DPAPI 缓存。`Auto` 只用于旧配置迁移：本机有可保护的 AppSecret 就落 Custom，否则 BuiltIn。
- `Custom` 必须同时有 AppId 和可解密的 AppSecret；输入成功后不回填明文，备份不导出凭据，恢复偏好不覆盖这四项。
- 端点信封是 `{v:1, kid, exp, data}`；`data` = 12 字节 nonce + 密文 + 16 字节 tag，AAD 为 `momoka-danmaku-credential-v1|kid|exp`。客户端校验 `v`、`kid`、`exp`、载荷内 `exp` 与解密结果，有效期必须在 1 分钟到 30 天之间。缓存到期前一天换新；端点暂时失败时，未过期缓存继续用。
- 生产端四个 Secret：`DANDANPLAY_APP_ID`、`DANDANPLAY_APP_SECRET`、`DANDANPLAY_CREDENTIAL_KEY`、`DANDANPLAY_CREDENTIAL_KID`。`DANDANPLAY_CREDENTIAL_KEY` 必须等于 `DandanPlayBuiltIn.Key`；换 key 必须发客户端新版，不能只改服务器。
- 秘钥轮换：换 AppId/AppSecret 后，旧缓存会在下一次 401/403 被客户端丢弃并重取；换 `kid` 只改缓存隔离与诊断；换加密 key 需要同步改客户端常量并发版。
- **这不是保密方案，也不是服务端代理。** 加密 key 随客户端分发，能提取；它只把 AppSecret 移出公开仓库并允许服务端轮换。所有内置用户共享额度，额度耗尽时提示改用自定义凭据。
- 端点只看到时间、IP 和 `Momoka/<version>` User-Agent；作品名、集数、媒体服务器信息与弹幕内容仍直接发往 `api.dandanplay.net`。

部署、密钥表、轮换矩阵与排查见 [内置凭据端点](references/credential-endpoint.md)。

## 匹配与显隐

- 自动匹配只接受**唯一且标题精确**的候选：`SeriesName` 优先，Unicode 归一化后只比较字母数字并忽略大小写。`hasMore`、多个结果、续季（`ParentIndexNumber` 不是 1）或特别篇都不猜，留给播放页手动搜索。
- `IsConfigured` = 服务开启 + 内置 libmpv +（内置来源或自定义凭据齐全）。外部 mpv 后端不接入弹幕：设置卡片、分类入口、播放器按钮和快捷键行一起隐藏，但偏好保留，同一视频的草稿不因隐藏清空；切回内置后可继续。
- 设置分类名 `弹幕` 是跨文件契约：`SettingsViewModel.CardCategories`、`SettingsCategoryGlyphConverter`、`PlayerSettingsLinks`（令牌仍是 `danmaku`，菜单行文案仍写产品名 `弹弹play`）、探针断言和 `MpvUiTests` 的固定表都要一起改。只改一处会出现「点设置入口落在别的卡」或自检红项。侧栏与卡片标题只写「弹幕」；产品名、开关文案和错误提示里的「弹弹play」指服务本身，不是分类名。
- 分类下是**四张卡**（2026-10-27 拆卡，`SettingsViewModel.Danmaku.cs` 的 `DanmakuSections`）：`弹幕`（启用、自动匹配、包含关联）、`显示与外观`（显示开关、输入栏、五个外观滑块）、`类型与屏蔽`（滚动/顶部/底部/彩色/合并、屏蔽词）、`应用凭据`（使用自己的应用凭据＋AppId／AppSecret／开发者中心）。行数归零的卡由 `SettingSection.SectionVisibility` 整张收起；四张卡的实例一直保留，`RefreshDanmakuPresentation` 按标题对号原地 `UpdateRows`。**不要再按 `Single(Category == "弹幕")` 找这张卡**，探针用的是 `DanmakuCards`／`DanmakuRows`。
- 服务关闭时只剩「启用弹弹play」与「使用自己的应用凭据」两行（分属第一张和最后一张卡）；自定义开关打开后才出现 AppId、AppSecret 与开发者中心。服务关闭或不可用时收起从属控件，但不重置偏好。

## 渲染与发送边界

- C# 负责过滤、排序与限流，按 `begin → chunk(128 条) → commit` 发给 `momoka_danmaku`；Lua 只做时间轴布局与 ASS OSD 叠加，不联网、不读媒体、不碰凭据，并用文件序号/传输序号丢弃旧片迟到的块。
- 发送校验、网络请求和“结果未确认”的判断都在宿主；uosc 的 `DanmakuInput` 只编辑草稿并把请求原样回传，不能让它直接发 HTTP。
- 输入栏在集成与独占模式共用同一套宿主逻辑；外部 mpv 不显示。输入时播放快捷键让路，失焦/换片/换库会隔离旧回执。
- 升级 uosc 后先查 `uosc/main.lua` 是否仍实例化 `DanmakuInput`，再跑 `uosc-interaction-tests.lua`；Lua 测试只证明状态与布局，不证明真实播放窗口。

## 验证

先按 CLAUDE.md 编译测试项目，再 `--no-build` 运行；SDK 用 `$HOME/.dotnet/dotnet.exe`。

```bash
$HOME/.dotnet/dotnet.exe build ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
$HOME/.dotnet/dotnet.exe run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build

py tools/test-danmaku.py --output work/danmaku-lua-new
py tools/test-uosc.py --output work/uosc-new

./artifacts/publish/win-x64/Momoka.exe --probe-shell --probe-section settings,playback --screen 1
```

- 端点仓库的测试在 `tests/danmaku-credential.test.mjs`；本树的 `website/worker` 不是干净检出的一部分。改动信封、AAD 或密钥后，Worker 测试与 `DanmakuTests` 的跨语言固定向量必须一起更新。
- 内置端点在线检查：GET 应返回 200，再用与客户端相同的 key 解密，核对 `appId`、密钥非空、`exp` 未过期；只输出长度，不打印密钥。503 表示 Secret 未配齐或部署未生效，404 表示路由未部署。
- 离线测试不证明真实服务、真实 UI、真实播放或独占 uosc。真实发送会写入一条真实弹幕并消耗共享额度，必须按 CLAUDE.md 先取得授权，且不做自动重试或批量试探。
- 设置/入口/分类改名属于 UI 契约变更，收尾时跑 `settings,playback` 探针并检查截图；只跑单测不足以证明入口没有落错卡。

## 改动清单

1. 协议改动先核对 Swagger 与当前源码，再改 `DandanPlayClient*`，补 `DanmakuTests`。
2. 设置显隐或分类改动同步 `SettingsViewModel.Danmaku.cs`、`PlayerSettingsLinks`、字形转换器、探针与 `MpvUiTests`。
3. 内置凭据改动同步 Worker（独立仓库）、`DandanPlayBuiltIn`、客户端解密和跨语言测试。
4. 同步 [docs/弹幕与快捷键.md](../../../docs/弹幕与快捷键.md)、PROGRESS 与本技能；只提交本次任务的文件，不夹带其他未提交改动。
5. 提交遵循 CLAUDE.md；推送仍需明确授权。

## 资源

- [协议参考](references/protocol.md)：接口、参数、报文、错误映射、额度与未覆盖项。
- [内置凭据端点](references/credential-endpoint.md)：Cloudflare 部署、Secret、AES-GCM 信封、轮换与排查。
