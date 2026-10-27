# Agent Team Run 20261022-065542-981052

- Goal: 收尾阶段12 AniBT与跨服务整理只读审查：完成三路复核、保存阶段记录与检查并本地提交后暂停，不进入修复或阶段13
- Workspace: C:\Users\89400\Momoka
- Status: completed
- Members: researcher, reviewer
- Capacity: 4 active, depth cap 1
- Created: 2026-10-22T06:55:42.659Z
- Closed: 2026-10-22T07:29:54.504Z

## Tasks

| ID | Role | State | Provider | Session | Stop reason |
|---|---|---|---|---|---|
| T1 | researcher | done | spawn | 6411852b-0921-4204-b86d-e5da3ed7c3f1 | completed |
| T2 | researcher | done | spawn | b0d9b2a6-59ba-4428-a46c-2aa1ea7dc18b | completed |
| T3 | researcher | done | spawn | 67de5f36-42ee-4067-828f-ea5f6265b310 | completed |
| T4 | reviewer | done | spawn | 1d1ed35b-9a3f-4212-8a01-89f9c52cc279 | completed |

## Results

### T1 — AniBT协议与搜索RSS只读审查

- Role: researcher
- State: done
- Attempts: 1
- Stop reason: completed

```text
## T1 收尾结论

### 已确认的候选问题（均未修复）

**R-037 [P2，条件性] 匹配候选缺少可选的内层 `anime.bgmId` 时被丢弃**  
位置：`src/Momoka.Core/AniBt/AniBtClient.cs:228-243`（`ParseCandidate`，关键条件在 233-234）。  
本地规格 `artifacts/code-review-stage12/public/AniBT-openapi-2026-10-22.yaml:4249-4263` 的 `AnimePublic.required` 不含 `bgmId`，即内层 `anime.bgmId` 可省略。当前代码要求内层 ID 必须等于候选级 ID；省略时 `Number(anime,"bgmId")` 得 `null`，整条候选被丢弃，自动候选也可能丢失。实际服务是否省略尚未验证。

**R-038 [P3] `Retry-After` 被解析但未送达界面**  
位置：`AniBtClient.cs:188-200`、`AniBtModels.cs:53-61`、`AniBtSearchViewModel.cs:227-231`、`AniBtSearchPanel.xaml:21-26`。  
429 时客户端保存了 `RetryAfter`，但全仓没有消费者；界面只显示“请稍后重试”，搜索框和搜索命令仍可立即再提交，可能重复触发限流。测试 `AniBtTests.cs:288-301` 只断言属性。

**R-039 [P3] 规格中的多集 `episodeKey=3-4` 被读成“合集”，与筛选值不一致**  
位置：`src/Momoka.Core/AniBt/AniBtReleaseLabels.cs:17-29`；本地规格示例在 `AniBT-openapi-2026-10-22.yaml:873-874`。  
`EpisodeBadge("3-4")` 返回“合集”，`EpisodeChoice("3-4")` 返回“3-4”；同一发布在两个纯函数中集数语义不同，徽标会丢失 3–4 的范围。T2 资源行是否另显集号未复核。

**R-040 [P3，条件性隐私边界] URL 拦截区分大小写，查询串可能被交给 AniBT**  
位置：`AniBtClient.cs:35-46`，关键在 40 行的 `Contains("://", StringComparison.Ordinal)`。  
小写 `https://…` 会被拒绝；大写 `HTTPS://host/path/video.mkv?token=…` 或协议相对 `//host/path?token=…` 不会命中，随后取路径末段时保留 `?token=…` 并作为 `rawTitle` 发送。目录仍被剥离；真实 Emby 是否返回 URL 型路径未验证。测试 `AniBtTests.cs:216-222` 只覆盖小写 URL。

### 排除与待验证

- 搜索 `ok+data`、匹配无包装：与本地规格一致；规格的 `result` 包装属于发布接口，不是当前 T1 接口。部署版本若不同则待验证。
- `page > 1` 返回空页：当前 UI 只请求第 1 页，规格无 page/offset，不列为缺陷。
- 304/404：客户端不发送条件请求，304 不可达；规格未为搜索/匹配定义 404，统一 404 文案暂不列缺陷。
- `bgmId` 10,000,000 上限：搜索 schema 未写最大值，但其他 `bgmId` 参数在规格中出现同值上限，暂按防护处理。
- 连接代次：`AniBtService.SearchAsync/ResourcesAsync` 使用 Stamp+Generation 并复查；搜索 VM 只比较 Stamp，但面板可用性变化会释放并清空，未找到可复现旧结果路径；搜索 VM 无专门代次测试，记为覆盖缺口。
- 季/集父剧补齐：`AniBtSearchQuery.From` 的首选 `SeriesName` 在菜单入口未传 owner；`AniBtEligibility.ResolveAsync` 仅靠 `SeriesId` 补父剧。真实 Emby 是否总回这些字段未验证，暂不定级。
- 搜索行 Bangumi 编号：文档 `docs/MoviePilot订阅.md:11` 说行卡显示编号，代码行属性/模板只显示标题、原名、年份、集数；选定后的头部才显示 Bgm，属低影响文档/展示不一致，未列稳定编号。
- `ImageUrl` 首选 `large` 无效时不会回退 `extraLarge`；仅影响封面，未列缺陷。
- `ResourcesAsync` 按序号配对当前数量一致；未来若过滤/重排才可能错配，未列缺陷。
- T2/T3 线索：资源浏览/下载确认/入口生命周期属 T2；`PrepareAsync`/`SubmitAsync`/MP 重整/Emby 识别提交属 T3，本路未定级。

### 阅读台账

T1 源码全文读：`AniBtClient.cs:1-288`、`AniBtModels.cs:1-61`、`AniBtSearchQuery.cs:1-16`、`AniBtReleaseLabels.cs:1-65`、`AniBtEligibility.cs:1-48`、`AniBtService.cs:1-309`（T1 只用搜索/资源段 47-62，整理提交段仅上下文）、`AniBtSearchViewModel.cs:1-350`、`AniBtSearchPanel.xaml:1-177` 与 `.xaml.cs:1-88`。

测试/探针全文读：`tests/Momoka.Tests/AniBtTests.cs:1-387`、`tests/Momoka.Tests/AniBtIntegrationTests.cs:1-517`（搜索/RSS/匹配/资源配对段用于 T1；整理提交段仅背景）、`src/Momoka.Shell/ShellNavigationProbe.AniBt.cs:1-464`（搜索段 116-304；刮削段 22-115 仅背景）。

规则/规格：`CLAUDE.md:1-162`；四个专项技能全文；`docs/分阶段代码审查与集中修复流程.md:1-218`；`阶段1计划.md:1-130、176-235`；`阶段11审查.md:1-133`；`docs/MoviePilot订阅.md:1-101`；本地规格 `AniBT-openapi-2026-10-22.yaml` 的 142-218、563-785、786-936、2284-2354、2883-2963、3103-3312、4249-4276、4393-4438；`LoadGeneration.cs:1-52`、`PageViewModel.cs:39-220`、`EmbySessionScope.cs:1-80`、`EmbySession.cs:429-464`。

背景阅读（未据此下 T1 结论）：`AniBtScrapeViewModel.cs:1-94`、`AniBtScrapeDialog.xaml`/`.cs`、`AniBtResourceViewModel.cs:1-262`、`AniBtResourceBrowser.xaml`/`.cs`、`MoviePilotWindow.cs:1-283`、`MoviePilotService.AniBt.cs:1-155`、`MoviePilotService.cs:70-209`、MoviePilot Download/Operations/History/Transfer/TransferCollect/TransferHistory/Resource/MediaParser 相关段、`MoviePilotDialog.cs:1-31`、`LibraryPage.xaml.cs:460-560/1090-1235`、`ShellPage.AniBt.cs:1-47`、`ShellPage.xaml.cs` 相关段、`ItemMenu.cs`/`ItemCommands.cs`/`HomePage.xaml.cs`/`MissingEpisodesDialog*`/`MoviePilotSubscriptionPage*`/订阅与遗漏集探针的 grep 段。  
未读：`PROGRESS.md` 全文（只读 1-40 与 grep 段）、`docs/开发与验证.md` 全文（只读第 22 行）、订阅/遗漏集探针全文、`tests/Momoka.Tests/Program.cs` 全文（只核注册行）。

### 验证说明与缺口

- 本轮 **0 次构建、0 次测试、0 次探针、0 次 UI/自检、0 次真实 AniBT/MoviePilot/Emby 调用、0 次网络访问**；只读本地源码、测试、文档和公开规格。未修改、暂存、提交或发布产品文件。
- 本地规格 SHA-256 为 `EFA259707B0BEFB13BA82EDD7BCB3680106B8897B502967D7F0EDB16D752E0A7`，是公开规格，不等于用户部署版本，不能证明真实服务行为。
- 主要缺口：真实服务是否省略内层 `anime.bgmId`、真实 Emby 是否返回 URL 型路径
... (204 characters truncated; see run.json)
```

### T2 — AniBT资源下载与入口生命周期

- Role: researcher
- State: done
- Attempts: 1
- Stop reason: completed

```text
## 结论

基线核对通过：审查树 HEAD 为 `a1cae9f97e9cfe56978e3ce028d8e184d706f3af`，`git diff --stat c34a5e5 -- src tests` 为空；`CLAUDE.md` SHA-256 与任务书一致。本轮为纯只读静态审查，未构建、未测试、未运行探针/UI/真实服务、未联网、未写文件。

阶段 12 就 T2 范围新增 **2 项候选**（R-037、R-038），均**没有数据写入、下载或提交风险**；未发现 P0/P1。资源筛选、下载确认、迟到结果与列表状态未发现新的已确认缺陷。

### R-037 [P3] 遗漏集清单补查元数据期间断线，仍显示可点击的旧搜索入口

- **状态**：已确认（静态），未修复；发现阶段 12；基线同上。
- **文件/成员/行号**：
  - `src/Momoka.Shell/Views/ShellPage.xaml.cs:1215-1281`，`ShowMissingEpisodesAsync`
  - 关键点：`:1224` 捕获 `connection`；`:1227-1250` 读取详情/父剧；`:1251` 只检查会话与页面；`:1254-1258` 构造 `MissingEpisodesDialog` 并以 `connection is not null` 传入 `canSearch`；`:1260-1265` 才订阅 `AvailabilityChanged`；`:1269-1272` 关闭后重新检查并决定是否打开搜索窗口。
  - `src/Momoka.Shell/Views/MissingEpisodesDialog.xaml.cs:39-44,56-82,109-119`
- **触发条件**：打开剧/季的「查看遗漏集」；进入时 MoviePilot 可用；补读详情或父剧期间 MP 断线（`AvailabilityChanged` 已触发）；读取返回后清单仍被构造；用户点击行上的 MP 或 AniBT 搜索。
- **预期行为**：依赖 MP 的搜索入口应隐藏，或至少点击时给出不可用提示。
- **实际行为**：清单以 `_canSearch=true` 打开，行上的 MP/AniBT 按钮可见；点击后 `ChooseSearch` 设置 `SearchEpisode`/`SearchWithAniBt` 并 `Hide()`；随后 `:1269-1270` 因 `!moviePilot.Available` 或 `!IsCurrentConnection(connection)` 直接返回，不打开任何搜索窗口，也没有提示。
- **影响**：可见的无效入口；清单关闭后没有后续动作。**没有证据表明会向失效连接下载或提交**：该对话框没有下载控件，只有搜索入口；两个搜索方法各自还会重查 `Available`（`ShellPage.AniBt.cs:20`、`ShellPage.xaml.cs:1171`）。
- **源码因果链**：`canSearch` 只由构造前的 `connection is not null` 决定；`connection` 在补读前捕获；补读期间没有 `AvailabilityChanged` 订阅；订阅发生在构造之后，因此漏掉已经发生的断线；`ChooseSearch` 只检查 `_canSearch`；关闭后的完整检查阻止搜索窗口，但已来不及阻止按钮显示。
- **已核防护**：`:1251` 检查会话可用和页面身份；`:1260-1265` 覆盖订阅后的断线/换连接；`:1269-1270` 覆盖关闭后的断线与连接代次；两个搜索入口再次检查 `Available`；没有下载/提交路径。
- **证据类型**：源码因果链；未做隔离运行复现。
- **未验证**：真实 WinUI 中 `AvailabilityChanged` 在两次 Emby 读取期间的时序、清单实际消失/按钮实际可见、点击后的视觉反馈。
- **最小修复方向**：在补读完成后、构造清单前重新计算 `canSearch = connection is not null && moviePilot.Available && moviePilot.IsCurrentConnection(connection)`；或者把订阅提前到补读前并记录“已失效”。若点击后连接已不可用，可补一条提示，但不必另开窗口。
- **回归验收（不执行）**：假 MP 可用、延迟详情读取；读取期间置 Offline 并触发 `AvailabilityChanged`；让清单出现，断言搜索按钮不可见（或清单不出现），点击不会打开 MP/AniBT 窗口；恢复 MP 后重新打开，按钮恢复；全程无写请求。
- **附带观察（不单列）**：反向也受同一冻结影响——清单打开时 MP 不可用、之后在设置窗连上，搜索入口不会在已打开的清单元内出现；需关闭重开。属于同一 `canSearch` 只在构造时求值的根因，无数据风险。

### R-038 [P3] 聚合模式非当前来源的「AniBT 识别」入口可见但静默无效

- **状态**：已确认（静态），未修复；发现阶段 12；基线同上。
- **文件/成员/行号**：
  - `src/Momoka.Core/Emby/ItemMenu.cs:299-309`，`MoviePilotBlock`
  - `src/Momoka.Shell/Views/ItemCommands.cs:141-185`（菜单可用性订阅与迟到动画类型插入）、`:272-274`（`Invoke` 只检查 `scope.IsAvailable`）、`:388-390`（派发 `ShowAniBtScrape`）
  - `src/Momoka.Shell/Views/ShellPage.AniBt.cs:33-36`，`ShowAniBtScrapeAsync` 的 `!scope.IsCurrent` 静默返回
  - `src/Momoka.Shell/ViewModels/AniBtScrapeViewModel.cs:42-51,68-80`，加载与提交同样要求 `scope.IsCurrent`
  - 佐证：`src/Momoka.Shell/Views/CardItem.cs:189,226-228`；`src/Momoka.Shell/Views/ShellPage.xaml.cs:508-520`；`src/Momoka.Shell/ShellNavigationProbe.MultiSourceSession.cs:270-277`
- **触发条件**：聚合模式下至少两个账号在线；从次级（非当前默认）来源打开搜索页、媒体库页或详情页；条目为动画（条目或父剧带动画类型）；MoviePilot 可用；菜单出现「AniBT 识别」后点击。
- **预期行为**：无法执行的来源应隐藏该入口；或支持该来源并正常工作。
- **实际行为**：`ItemCommands.Invoke` 只校验来源仍可用，随后 `ShowAniBtScrapeAsync` 在 `!scope.IsCurrent` 处直接返回；没有对话框、没有提示、没有读取。
- **影响**：确定性死菜单项；聚合模式下次级来源无法使用 AniBT 识别，用户会认为功能失效。**没有读取、下载、整理或提交**；`AniBtScrapeViewModel` 的 `SubmitAsync` 另有 `IsCurrent` 守卫，即使入口被放宽也不会误提交。
- **源码因果链**：`CardItem.Scope` 保存条目所属来源（`CardItem.cs:189`）；`ItemMenu` 只按全局 MP 可用与动画类型添加 `AniBtScrape`，不看来源是否当前；`ItemCommands.Invoke` 只检查 `IsAvailable`；外壳与视图模型要求 `IsCurrent`；两者语义不一致，导致入口出现而执行静默失败。
- **已核防护**：`ItemCommands.Invoke:272` 的 `IsAvailable` 阻止已退出来源；`ItemCommands.Build:169` 的迟到类型补查也检查 `IsAvailable`/卡片状态；`AniBtScrapeViewModel` 的加载、提交均有当前来源守卫。现有防护只保证安全，不保证入口显隐一致。
- **证据类型**：源码因果链；多来源探针已证明次级来源卡片真实携带各自 `Scope`，但未运行菜单交互。
- **未验证**：聚合模式真实菜单的渲染与点击；产品是否明确希望 AniBT 识别仅支持当前账号；不同宿主（搜索页/媒体库/详情）是否都走同一 `ItemCommands` 路径（源码上是同一路径，未逐个运行）。
- **最小修复方向**：二选一。若产品限定仅当前账号：把 `scope.IsCurrent` 作为入口能力传入 `ItemMenu`/`ItemCommands`，不添加「AniBT 识别」，包括迟到的动画类型补查分支。若产品要支持聚合次级来源：把外壳和 `AniBtScrapeViewModel` 的 `IsCurrent` 收紧改为 `IsAvailable`，并保留取消、身份和提交守卫；这是较大的产品行为变更。
- **回归验收（不执行）**：两账号聚合夹具，
... (7702 characters truncated; see run.json)
```

### T3 — AniBT识别与跨服务整理

- Role: researcher
- State: done
- Attempts: 1
- Stop reason: completed

```text
## 结论

- 基线核对通过：审查树 HEAD 为 `a1cae9f97e9cfe56978e3ce028d8e184d706f3af`；固定受审产品 `c34a5e5` 对 `src tests` 的 diff 为空；[CLAUDE.md](file:///C:/Users/89400/Momoka/CLAUDE.md) SHA-256 与给定值一致。全程只读，未修改、暂存或提交任何文件。
- 建议新增 **R-037～R-043**：R-037 为上游候选，**P1**；R-038～R-043 为本轮新增，均为 **P2**。全部为静态确认，未用真实 MoviePilot/Emby 复现。
- 上游候选**成立**：无原整理记录时，Emby 远程搜索只要返回非空，[PickIdentifyResult](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L282-L296) 在无标题/年份匹配时无条件返回第一条，随后直接应用元数据与图片；UI 不展示最终 Emby 条目。影响限于元数据、ProviderIds、图片与识别关系，**不包含媒体文件删除**。

## 候选问题

### R-037 [P1] 无原整理记录时 Emby 搜索非空即取首条并自动应用

- **状态**：静态确认，未修复、未复现；现有测试已把该兜底固定为期望。
- **位置**：[AniBtService.cs:225-245](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L225-L245) `PrepareEmbyIdentifyAsync`；[:282-296](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L282-L296) `PickIdentifyResult`；[:298-301](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L298-L301) `ByYear`；[:266-276](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L266-L276) `ApplyEmbyIdentifyAsync`；[:247-264](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L247-L264) `IdentifyTargetAsync`；[EmbyClient.cs:655-683](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/Emby/EmbyClient.cs#L655-L683)。
- **触发**：文件没有 MP 原整理记录；用户选择某个 AniBT 候选；Emby `RemoteSearch` 返回至少一条，但没有任何标题精确/包含匹配，或匹配项年份全部不符。搜索只用 `Title`/`OriginalTitle`，年份传 `null`，仅事后过滤。
- **预期/实际**：预期只应用可核实为同一作品的条目，否则拒绝或让用户选；实际 `PickIdentifyResult` 无匹配时 `return results[0]`，`ByYear` 年份不符时也 `return results[0]`，随后 `ApplyRemoteSearchResultAsync(..., replaceAllImages:true)` 与 `RefreshItemAsync("all", replaceImages:true, ...)` 直接落到目标上。目标是电影/剧集；条目是季/单集时经 `IdentifyTargetAsync` 落到所属**整部剧**。
- **影响**：目标条目的 ProviderIds、标题、简介等元数据和图片被替换；手改元数据/封面可能丢失。对季/集操作会刷新整剧（`Recursive=true`）。没有调用删除媒体文件；不声称媒体文件被删。UI 只显示 AniBT 候选，最终 Emby 条目只在应用完成后出现在逐行结果里。
- **因果链**：`row.History == null` → `identify` → `PrepareEmbyIdentifyAsync` → 搜索非空 → 精确/包含均无 → `results[0]` → `PickIdentifyResult` 非空 → `Claim` → `Apply` → `Refresh` → 结果行。
- **已核防护**：所选候选必须属于该文件的 AniBT 候选（`AnimeId+BgmId`，[:117-119](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L117-L119)）；MoviePilot 连接戳+代次（[:105,127,130,144](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L105-L144)）；`identify.Count` 同番剧限制（[:139-140](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L139-L140)）；`plan.Claim()` 一次性（[:145](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L145)）；无法确定所属剧时拒绝（[:261-262](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L261-L262)）；所有读取/预览先于 Claim。它们都没有建立“Emby 结果就是所选番剧”的证据。
- **证据/未验证**：[AniBtTests.cs:38-47](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/tests/Momoka.Tests/AniBtTests.cs#L38-L47) 明确断言单个不相关结果仍被返回；[AniBtIntegrationTests.cs:257-281](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/tests/Momoka.Tests/AniBtIntegrationTests.cs#L257-L281) 断言 Apply/Refresh 的 `ReplaceAllImages=true`。真实 Emby 的结果排序、跨语言标题匹配、`Apply` 实际影响未核。
- **最小修复方向**：去掉 `results[0]` 与年份不符时的首条兜底；无可靠匹配就拒绝并提示用户走普通“识别”手动选择；把 AniBT 年份传入搜索，并要求精确标题/年份或可核实的 ProviderId 一致。若保留自动选择，应用前必须展示最终条目并让用户确认。
- **回归验收（未执行）**：`[无关, 精确]` 选精确；只有无关结果或年份全不符时 Apply/Refresh 请求数为 0；精确且年份相符仍成功；UI 在应用前显示最终 Emby 条目；真实媒体文件不受影响。

### R-038 [P2] 无原记录识别强制全量替换图片，且缩略图边界并不成立

- **状态**：静态确认，未修复、未复现。
- **位置**：[AniBtService.cs:269-272](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/Ani
... (21723 characters truncated; see run.json)
```

### T4 — 阶段12候选独立复核

- Role: reviewer
- State: done
- Attempts: 1
- Stop reason: completed

```text
**结论摘要**

PASS 8、FAIL 1、PARTIAL 3。基线核对通过：工作树 HEAD 为 `a1cae9f97e9cfe56978e3ce028d8e184d706f3af`，`git diff --stat c34a5e5 -- src tests` 为空，`git status --porcelain src tests` 为空；[CLAUDE.md](file:///C:/Users/89400/Momoka/CLAUDE.md) SHA-256 为 `14ffeb7e…3b86`，与任务书一致。建议不新增 P1；确认 P2 四项（A、D、E、F，其中 F 扩入 R-024）、P3 一项（H）。B 交产品决定；C、G、J 作 P3 建议；K、L 待验证；I 剔除。

**逐项复核**

**A — PASS（成立，建议 P2，P1 过高）**
- 因果链准确：[AniBtService.cs:225](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L225) 在无 MP 原记录时用 AniBT 标题搜 Emby；[PickIdentifyResult](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L282) 在无标题精确/包含匹配时返回 `results[0]`，[ByYear](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L298) 在年份全不符时也回 `results[0]`。测试 [AniBtTests.cs:45](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/tests/Momoka.Tests/AniBtTests.cs#L45) 已把“唯一不相关结果仍返回它”固化为期望，说明是既定行为而非偶然。
- 触发可达：无 MP 整理记录（手动入库/非 MP 导入）+ RemoteSearch 非空但标题不匹配；标题翻译差异、结果缺年份或年份不符都会增加概率。UI 只展示 AniBT 候选（[AniBtScrapeDialog.xaml:17](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Shell/Views/AniBtScrapeDialog.xaml#L17)），不展示最终 Emby 条目；名字只在应用后的结果行出现。
- 影响边界：`ApplyRemoteSearchResultAsync(replaceAllImages:true)` 与 `RefreshItemAsync("all", replaceImages:true)` 会替换 ProviderIds、元数据和图片，不删媒体文件。若入口是单集/季，识别目标是整部剧（[IdentifyTargetAsync](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L247)），影响面是整个 series。
- 缺失防护：没有“必须标题匹配”或“应用前确认”的守卫；没有真实 provider 证据表明 `results[0]` 通常是正确作品。P1 过高，建议 P2；若产品认定“整剧元数据/图片被错误替换”不可接受，可再升 P1。

**B — PASS（事实成立；性质为产品行为，待用户决定）**
- 普通识别默认不换图（[IdentifyDialog.xaml.cs:244](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Shell/Views/IdentifyDialog.xaml.cs#L244)）；AniBT 路径固定 [AniBtService.cs:269](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L269) 传 `replaceAllImages:true`，Refresh 也传 `replaceImages:true`（[EmbyClient.cs:579](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/Emby/EmbyClient.cs#L579)），UI 无选项。
- `replaceThumbnails:false` 与 `ReplaceAllImages=true` 同发；客户端注释明确 ReplaceAllImages 会“先删光再重下”，所以缩略图边界在客户端语义上不成立；服务器是否单独保留 Thumb 未核。
- AniBT 的目的本就是“重新刮削”，强制换图可能是合理的；但它与普通识别明确保护用户封面的做法不一致。建议交用户决定：要么与识别一致（默认关 + 勾选），要么在弹窗明说“会替换图片”。若用户认定是缺陷，P2；当前不作确认缺陷。

**C — PARTIAL（代码缺口成立，影响/可达性被夸大，建议 P3）**
- 准确：[SubmitAsync](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L102) 只在入口查 `IsCurrent`/`ThrowIfNotCurrent`；准备循环、transfer 循环和扫描有 `canContinue`；identify 的 Apply/Refresh（[AniBtService.cs:172](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L172)）不检查。[EmbySession.ExecuteAsync](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/Emby/EmbySession.cs#L452) 只在开始时查 `IsAvailable`，不保证 `IsCurrent`；`SelectSource` 只改当前项，不取消旧来源 lifetime（[EmbySession.cs:91](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/Emby/EmbySession.cs#L91)）。
- 可达性：主窗 ContentDialog 是模态，正常无法点主窗切来源；但已打开的独立设置窗可在聚合模式切换来源，旧来源仍可用。提交中命中窗口存在，尤其是 prepare 网络调用后、Apply 前。
- 影响：写入的是用户最初打开的那个来源，不是“错误来源”；主要后果是用户切走后仍继续写旧库、结果不在当前视图。P2 过高；建议 P3。若项目坚持“身份改变即停写”不变量，可 P2。

**D — PASS（P2）**
- [CompleteReceipt](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/MoviePilot/MoviePilotTransfer.cs#L360) 把 `completed/accepted/skipped` 都算 Success；[TransferMessage](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L215) 只看 `Success && Target`，会说“已重新整理到”；`moved` 按 Success 收集并触发全库扫描（[AniBtService.cs:146](file:///C:/Users/89400/.codex/worktrees/staged-review-continue/Momoka/src/Momoka.Core/AniBt/AniBtService.cs#L
... (15185 characters truncated; see run.json)
```

## Open items

- None.

## Trajectory

- Events: 16 (run/created 1, task/added 4, activation/start 4, activation/settled 4, note 2, run/closed 1)
- Ledger: C:\Users\89400\Momoka\.qoder\team-runs\20261022-065542-981052\trajectory.jsonl

- #7 activation/start T3 @ 2026-10-22T06:57:02.945Z
- #8 activation/settled T2 @ 2026-10-22T07:11:01.417Z
- #9 activation/settled T3 @ 2026-10-22T07:11:01.512Z
- #10 activation/settled T1 @ 2026-10-22T07:18:42.806Z
- #11 task/added T4 @ 2026-10-22T07:19:05.304Z
- #12 activation/start T4 @ 2026-10-22T07:19:12.650Z
- #13 activation/settled T4 @ 2026-10-22T07:26:01.277Z
- #14 note @ 2026-10-22T07:26:01.369Z
- #15 note @ 2026-10-22T07:29:54.406Z
- #16 run/closed @ 2026-10-22T07:29:54.505Z

