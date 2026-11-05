# Momoka 全项目分区代码审查计划

> 执行说明：这是审查计划，不是功能实现计划。执行时使用 `momoka-code-review`，按分区加载项目技能；静态阅读可由子代理并行，主代理统一复核证据。清单使用复选框跟踪，产品修复另行形成任务，不将审查自动扩展成重构或发布。

**目标：** 覆盖当前项目全部自有源码、入库第三方源码、测试、构建与运维脚本、网页及配置，形成可追踪的缺陷清单和验证缺口。

**方法：** 按业务调用链划分 18 个分区，每个文件只有一个主责分区；共享文件按成员补充跨区检查。每区从调用入口读到状态、外部交互、失败处理和测试，再做跨分区场景复核。

**技术范围：** C# / WinUI 3 / XAML、PowerShell、Python、JavaScript / HTML / CSS、Lua / uosc、GLSL / HOOK、MSBuild 与部署配置。

**需求依据：** 用户要求“制定计划分区审查项目所有代码”；[CLAUDE.md](../../../CLAUDE.md)、[开发与验证](../../开发与验证.md)、[审查技能](../../../.claude/skills/momoka-code-review/SKILL.md)。本轮仅制定计划；以下审查与验证步骤均待执行。

## 1. 范围基线与已知前置条件

- 盘点日期：2026-11-05，Asia/Shanghai。
- 当前工作树：`C:/Users/89400/.codex/worktrees/cd73/Momoka`。
- 盘点提交：`08a8e30218c53bf0cf7978aaa020d738bd6ecfcb`；盘点时当前树无未提交修改，主树 HEAD 相同，两个位置的 `CLAUDE.md` 哈希一致。这是计划依据，不保证执行时仍相同。
- 执行开始重新读取项目规则、记录 HEAD 与 dirty 状态。后续新增或修改的文件进入增量清单，不静默沿用本次数字。

| 范围 | Git 跟踪文件数 | 本次确认的组成 |
| --- | ---: | --- |
| `src/Momoka.Core` | 346 | 345 个 C#、1 个项目文件 |
| `src/Momoka.Shell` | 473 | 397 个 C#、73 个 XAML、3 个其他文件 |
| `tests` | 688 | 486 个 C#；另有脚本、夹具、说明和 66 个 csproj |
| `tools` | 54 | 维护、质量、测试、发布、签名脚本及分析器 |
| `assets` | 86 | 32 个 Lua、18 个 shader、字体、原生载荷、许可等 |
| `website` | 20 | 网站源码、dist 副本、Worker 和部署配置 |
| `.claude` | 39 | 项目技能、参考、4 个 Python 脚本等 |
| `docs` | 125 | 当前说明、计划、基线与历史证据 |
| `.qoder` 与入库 `work` | 27 | 历史任务记录与证据 |
| 其余根文件 | 17 | 方案、共享构建配置、规则、说明、`smoke.ps1` 等 |
| **合计** | **1,875** | 数量来自 Git 清单，不等于已审查数量 |

当前没有入库的 `.github` CI 目录；不要创建虚构的 CI 审查任务。Shell 没有独立的 `Controls` / `Diagnostics` 目录：控件多在 `Views`，诊断与探针大量位于 Shell 根目录。Core 的 `Emby` 目录也包含浏览、布局、WebView 和播放上报，不能按目录名称全部划进协议区。

**验证入口已有静态确认的差异，先登记，再决定处理：**

1. `tests` 下有 60 个顶层测试项目、6 个嵌套支持项目；`tools/regression-suites.json` 只有 59 项，缺 `Momoka.LibraryHeaderTests`。`tools/run-regressions.ps1` 在 `-List` / `-Suite` / `-All` 前比较顶层项目与清单；按当前源码会先拒绝。此处仅比较文件和读取控制流，尚未运行或修复。
2. 方案没有纳入 6 个顶层专项：`Momoka.MoviePilotSubscribeSettingsTests`、`Momoka.ServerSettingsTabs.Tests`、`Momoka.SettingsPriorityMotionTests`、`Momoka.SettingsRowsMotionTests`、`Momoka.SettingsSearchNavigationTests`、`Momoka.SiteDialogFlowTests`。构建方案不能替代逐项目覆盖核对。
3. `Momoka.ServerSettingsTabs.Tests` 在统一入口中属于 external；选择 `-All` 会记录 skipped 并返回非零。完整 DOM wrapper 另有 Node、Playwright 和浏览器前置，不能拿 `contracts` 子集当完整通过。
4. 主测试 `Program.cs` 还有显式参数入口，例如 `settings-applicability-fixture`、`home-performance-fixture`、`player-volume-gesture-fixture`；无参运行不覆盖它们。嵌套辅助项目与重叠用例不重复累加为独立覆盖。

## 2. 全局约束与审查标准

- 本计划的执行默认只审查、运行适用检查和记录结果；不隐含修改产品、升级依赖、推送、发布、安装或签名授权。修复另按用户要求进入开发与交付流程。
- 自有代码与入库第三方文本源码全部纳入，不以“大文件”“已有测试”“上轮看过”跳过。二进制没有可读源码时，记录来源、固定版本/哈希、批准清单、加载与分发边界，明确不能声称完成内部源码审查。
- 与生成内容逐字节一致的部署副本可通过对应关系完成核对；不一致时审查差异、生成流程和实际发布入口。不可用“生成代码”标签排除网站 dist 或 MSBuild 生成/提取逻辑。
- 不扫描用户真实明文凭据；脱敏检查使用占位值。报告不包含服务器私有地址、令牌、密码、私钥或用户媒体数据。
- Emby、Jellyfin、飞牛、Plex 分别核对实际协议和支持能力；MoviePilot/AniBT 共享业务只审一套，并检查各服务器接入处。
- 正式涉及上游行为判断时，先按对应技能读取官方实现，记录仓库、版本/提交、文件与条件；本轮盘点未联网，上游一致性尚未核实。源码不可得时单列缺口。
- 静态阅读可并行；同树写入与构建串行，同用户互斥锁、桌面/UI、鼠标/GPU 与发布目录均由一个协调者安排。只读子代理不创建临时工作树；需要修改时才按项目规则隔离。
- 普通自检或导航可能登录已保存账号、读取真实库。默认使用匿名夹具和隔离目录；真实播放、删除、整理、字幕/元数据/账号写入按项目规则另获具体授权。
- 发现必须包含：实际位置、可达触发、预期与实际行为、具体后果、证据和最小完整修正方向。允许零发现，不按数量交差，不因文件长或个人风格要求新增抽象。
- 缺陷、重要待核实风险与可选改进分开；默认不列可选重构。复审只看原发现及受影响链路，新范围必须有新证据，不循环寻找凑数问题。
- 状态分开记录：源码审查、离线验证、真实 UI、真实服务器、修复/交付；任一项完成不能代替其他项。

## 3. 第 0 批：建立可核对的范围与验证清单

- [ ] 记录当前 Git 根目录、HEAD、分支、工作树状态及规则版本；列出基点后变化。只有发生内容漂移的文件重新审查。
- [ ] 用 `git -c core.quotePath=false ls-files` 建立逐文件台账；加入当前未跟踪的自有代码，单列忽略目录内实际被构建或发布引用的资源。
- [ ] 每行填写 `path / blob或内容hash / 内容类型 / 主责R编号 / 联审分区 / 阅读状态 / 测试入口 / 证据 / 例外说明`。所有文件必须有主责；共享文件按方法或成员登记联审范围，不能仅由匹配到的前缀自动判定完成。
- [ ] 按下面 18 区分配全部文件；`AppSettings.cs`、`EmbyClient.cs`、`ShellServices.cs`、`SettingsViewModel.cs`、`MpvOutputOptions.cs` 等共享入口拆成明确的成员清单。未归属数量必须为 0。
- [ ] 对比全部 csproj、方案与回归清单；登记上述差异。仅审查阶段将修复建议入报告；若已获修复授权，补清单及对应拒绝反例后再使用统一回归入口，不删除守卫绕过差异。
- [ ] 建立“项目 + 模式/参数 + 被测产品文件 + Shell/原生/浏览器前置 + 输出目录”的验证矩阵；包括 wrapper、源码提取器、Lua、Worker、技能脚本以及主测试显式入口。
- [ ] 为后续运行分配全新证据目录，例如本树 `artifacts/review/<基点短SHA>/<批次>/<运行ID>`；保留失败、跳过和环境原因，不覆盖旧报告。

**本批产物：** 文件台账、验证矩阵、待核对清单。可使用 JSON 或 Markdown，不要求新增审查框架或产品依赖。台账初始必须为“未审查”，盘点数量不能自动变成完成数。

## 4. 18 个审查分区

以下路径均相对仓库根目录；前缀或通配符代表同类文件，执行时必须展开到逐文件台账。各区列出的测试是切入点，完整范围由第 0 批核对，不把示例列表当全部测试。

### R01 构建、依赖、更新与交付工具

**范围：** `Momoka.sln`、`global.json`、`Directory.Build.props`、`nuget.config`、项目/props/targets、`src/Momoka.Core/AppIdentity.cs`、`src/Momoka.Core/Updates`、Shell manifest、`tools` 中构建/发布/安装/签名/备份/分析器工具、`assets/mpv-runtime`、根 `smoke.ps1`。其他业务资源的算法交所属区。

**检查：** 固定 SDK 与真实包依赖；分析器加载条件与哈希；本机/外部路径、清理边界和文件锁；原生依赖、许可和批准载荷；升级版本判断；签名、证书、安装与系统设置修改的触发边界。

**测试入口：** `Momoka.ArchitectureTests`、主测试 Update/Agreements、`tools/test-mpv-*.ps1`、`test-shader-publish.ps1`、`test-release-maintenance.ps1`、`test-smoke.ps1`。合成载荷检查与真实发布验证分别记录；审查不运行发布/签名命令。

- [ ] 审完全部构建和系统修改路径；标出 ignored 的根 `libmpv-2.dll` 等实际依赖与来源证据。

### R02 设置持久化、迁移与凭据

**范围：** `src/Momoka.Core/Configuration`、`Services/SettingsService.cs`、`ISettingsPersistence.cs`、`Infrastructure/AtomicFile.cs`、`AppPaths.cs`、Shell `Security/DpapiSecretProtector.cs`、`ShellPrefs.cs`。具体音视频/字幕/弹幕字段与 R12–R14 联审。

**检查：** 迁移兼容；快照与活对象隔离；原子写、延迟保存、失败重试与关闭排空；导入/重置/导出边界；DPAPI 与多服务凭据隔离；设置隐藏后保留和恢复原值。

**测试入口：** `Momoka.SettingsPersistenceTests`、`Momoka.SettingsDeferredSaveTests`、`Momoka.AtomicFileWriteTests`、主测试 Settings/SettingsSaveSnapshot。

- [ ] 核对加载→编辑→保存→重新加载与失败恢复完整链路，检查支持项目的旧 schema 夹具是否真实对应迁移输入。

### R03 启动、会话、权限与导航生命周期

**范围：** Shell `Program.cs`、`App.xaml*`、`ActivationListener.cs`、`Composition/ShellServices.cs`、`Platform`、`ShellPage` 主体与页面接口、SignIn/Servers VM 与页面；Core `EmbySession*`、`EmbySignInIdentity.cs`、`ServerAccess.cs`、`Services/ServerCapabilities.cs`、启动参数与入口规则。

**检查：** DI/Attach/Detach 与事件退订；单实例和退出；登录输入代次、自动恢复和权限变化；聚合来源及导航栈；旧请求不得覆盖新身份。区分 `IsCurrent` 和 `IsAvailable`，保留有意绑定原来源的操作。

**测试入口：** `Momoka.SignInInputGenerationTests`、`Momoka.CurrentPermissionSnapshotTests`、主测试 Session/Identity/Activation/Startup。

- [ ] 给身份创建、替换、撤销和导航返回分别画出状态归属，交给 R04–R15 作为联审依据。

### R04 Emby / Jellyfin 协议与实时连接

**范围：** Core `EmbyHttp*`、`EmbyUrl.cs`、`EmbyServerAddress.cs`、`DeviceIdentity.cs`、`EmbyApiException.cs`、`EmbyServerGateway.cs`、`EmbyServerDiscovery.cs`、`EmbyClient.Protocol.cs`、共享门面的协议分发、`EmbyRealtime*`、相关模型序列化。

**检查：** 反向代理基址和路径；鉴权头与重定向；产品探测；错误、超时、取消、下载清理和脱敏；各版本路由/字段差异；实时握手、重连与旧会话停止。

**测试入口：** 主测试 HttpRedirect/HttpDeadline/HttpDownloadError、JellyfinProtocol、EmbyRealtime；上游核对用项目 Emby/Jellyfin 技能。

- [ ] 分别完成两个产品的请求与失败矩阵，不能以一种产品的成功推定另一种兼容。

### R05 飞牛 / Plex 协议与能力映射

**范围：** `src/Momoka.Core/FnNas`、`src/Momoka.Core/Plex`，以及 `EmbyClient.cs` / `EmbyServerGateway.cs` 中的适配入口。飞牛和 Plex 分两个子批阅读。

**检查：** 签名、登录/刷新和请求身份；返回模型、分页与播放映射；权限未知态；无正文写入回执与读回；Plex 收藏/筛选/管理等能力差异；当前实现与上游版本的证据边界。

**测试入口：** `Momoka.FnNasErrorClassificationTests`、`Momoka.FnNasDefaultPermissionTests`、`Momoka.PlexAffordanceTests`、主测试 FnNasProtocol/FnNasMedia/PlexProtocol/PlexMedia/PlexV2Login。

- [ ] 对两个产品分别提交接口、映射和不支持能力清单，联审浏览、写操作和播放调用者。

### R06 首页、媒体库、搜索、收藏与状态同步

**范围：** Core `EmbyClient.LibraryBrowse.cs`、`Library*`、`Home*`、`ItemQuery.cs`、`AggregateQueryCursor.cs`、`BrowseSnapshotStore.cs`、来源/收藏/续播模型；Shell Home/Library/Favorites/Filter VM、相关页面、Shelf/Card 数据模型与请求对象。

**检查：** 分页与重复/缺项；筛选排序、聚合来源、总数；取消与快速换查询；返回快照、选择和滚动；已看/删除后的刷新；长剧、特别篇、无图/空库；各服务器自己的组件与数据分支。

**测试入口：** `Momoka.HomeDirectoryTests`、`Momoka.HomeFoldCompletionTests`、`Momoka.LibraryHeaderTests`、`Momoka.SearchLifetimeTests`、主测试 `library-resume-paging-fixture`、`library-favorite-source-fixture`、`library-deep-return-fixture`。

- [ ] 展开 Home、Library、Detail 的数据交接表；搜索四来源入口始终显示的例外单独核对。

### R07 详情、条目写入、元数据与删除

**范围：** Core ItemDetail/ItemMetadata/EmbyMetadata/ItemUserStateOrder/RemovedMedia 与 `EmbyClient.Metadata.cs`；Shell Detail VM/页面、`ItemCommands*.cs`、元数据/识别/刷新/合集/封面等编辑对话框。字幕专属逻辑由 R13 主责。

**检查：** 显隐与执行权限一致；确认前后账号、条目、版本身份；删除范围和计划单次消费；收藏/已看并发回执；失败与已保存但读回失败的反馈；写后详情、卡片、缓存与播放目标失效。

**测试入口：** `Momoka.ItemUserStateOrderTests`、`Momoka.DetailDeleteCommandTests` 的原 wrapper、主测试 Metadata/RemovedMedia/DetailSubjectRemoval。

- [ ] 使用匿名传输或源码链证明写入边界，禁止通过真实删除制造证据；与 R06/R10 联审旧卡片和旧播放目标。

### R08 MoviePilot 搜索、资源、下载与订阅

**范围：** Core MoviePilot 客户端/服务的鉴权、搜索、资源、订阅、入库播放与版本定位；Shell 对应 Search/Resource/Download/Subscription/Versions VM、页面与弹窗，`ShellPage.MoviePilot.cs` 相关接线。

**检查：** JWT/配置身份与跳转；连接失效、搜索进度、站点选择和页面返回；资源筛选/排序；订阅剧季集身份、默认值、编辑保存与重试；下载提交目标；库内播放映射；搜索来源例外与从属入口显隐。

**测试入口：** `Momoka.MoviePilotJourneyTests`、`Momoka.ResourceBrowserTests`、`Momoka.SubscriptionEditorTests`、`Momoka.SubscriptionIdentityTests`、`Momoka.MoviePilotSubscribeSettingsTests`、`Momoka.SiteDialogFlowTests`。

- [ ] 按 MoviePilot 技能核对官方前后端源码，记录提交；仅凭截图或旧文档不能判定本地错误。

### R09 AniBT 与 MoviePilot 整理、历史和媒体管理

**范围：** `src/Momoka.Core/AniBt`、MoviePilot AniBt/Transfer/History/MediaManagement 相关文件；Shell AniBt 全链、系列整理、手动整理和媒体管理及 `ShellPage.MoviePilotMediaManagement*`。

**检查：** 动画/季/集/编号身份；准备并发失败与半份计划；配置 A→B→A、选择变化后计划失效；一次确认、重复提交与不确定结果；源/目标/历史删除范围；完成后的列表和通知更新。

**测试入口：** `Momoka.AniBtEntryTests`、`Momoka.AniBtConfirmationTests`、`Momoka.AniBtSourceContractTests`、`Momoka.TransferSubmissionTests`、主测试 AniBtPrepareConcurrency/MoviePilotTransfer。

- [ ] 从“预览/准备→确认→提交→结果”连读，不执行真实整理或删除；高后果候选由第二名审查者复核。

### R10 播放意图、队列、版本与生命周期

**范围：** Core `PlaybackService*.cs` 的公共生命周期、PlaybackPlanner/Continuation/Contracts、EpisodeNavigation、StartIntent、版本选择与 UserPlaybackQueue、`Emby/PlaybackReport.cs`；Shell Player VM 的 Transport/Events/Versions 及页面退出、切换路径。

**检查：** 连续起播、取消、停止和退出竞争；播放来源撤销；自然下一集与用户队列；后端结束事件与停止上报；续播点和已看；同窗切片、失败回退与旧回调失效。

**测试入口：** `Momoka.NaturalEpisodeContinuationTests`、`Momoka.NaturalContinuationCallerTests` 原 wrapper、`Momoka.PlayerExitHandoffTests`、`Momoka.PlaybackSourceRevocationTests`、`Momoka.LibMpvEndFileTests`。

- [ ] 给每个异步回调标出所属播放会话和资源释放者；与 R11–R14 联审换片后的选项和任务清理。

### R11 播放后端、原生互操作、窗口与 uosc

**范围：** Core LibMpvBackend/MpvProcessBackend、PlaybackBackendFactory、LibMpvNative/Nodes/Frame、NativeModuleBinding、IPC/CommandLine/ArgumentBuilder、MpvUi/InlineSwitch/菜单与交互规则；Shell Interop/Windowing 与 Player 输入、菜单、时间轴；`assets/mpv-ui` 中 uosc、stats、字体和本地交互补丁。

**检查：** 原生句柄/回调、IPC 断线和子进程退出；凭据不进命令行；C#↔Lua 消息契约；菜单握手和同窗换片；输入焦点、DPI、全屏、鼠标与退出恢复；本地补丁与对应上游源码。

**测试入口：** 主测试 BackendLifecycle/MpvProcess/MpvUi/InlineSwitch、`tools/test-uosc.py`、`tools/test-uosc-review.py`、对应 Lua 夹具；隔离本地播放探针按项目规则安排。

- [ ] 分开记录 libmpv / 外部进程与集成 / 独占两个维度；三条集成管线探针不能替代独占、uosc 或外部后端证据。

### R12 视频、HDR、着色器、补帧与音频输出

**范围：** Core MpvOutputOptions/MpvProfiles/HdrOptions、Shader/FrameInterpolation/MpvSvp、AudioDeviceCatalogue/VolumeMemory/VolumeScale、ShaderStaging；相关配置成员、Settings/Player 设置接线、`assets/shaders`。

**子批：** A 视频/HDR/profile；B 着色器/补帧与原生前置；C 音频设备/声道/直通/延迟/响度。

**检查：** UI→持久化→实际 mpv 选项→读回；条件显隐与原值恢复；profile/着色器残留、门限、链顺序与失败回滚；设备回退和音量记忆；不支持组合与性能资源预算。

**测试入口：** `Momoka.FrameInterpolationTests`、`Momoka.ShaderCommandGenerationTests`、`Momoka.ShaderMonitorPolicyTests`、主测试 AudioOutput/VideoOutput/ShaderTransaction、音频技能无声原生探针。

- [ ] 全部 18 个 shader 逐个核 `HOOK/WHEN/BIND`、来源和宿主设置；无声或无媒体探针不声称证明听感、画质或真实播放。

### R13 字幕选择、管理与翻译

**范围：** Core TrackSelection/TrackLanguagePriority/PlaybackTrackState、SubtitleFile/SubtitlePreviewPlan/ServerSubtitles、Translation、MpvSubtitleTranslation、PlaybackService.Translation；Shell SubtitleDialog、SubtitleTranslationController 与相关设置/播放器路径；`momoka_subtitle_translation.lua`。

**检查：** 语言优先、强制/主次字幕、轨道编号与外部文件；ASS/SRT/PGS 样式和预览；拖入/删除时来源与窗口归属；翻译凭据、请求预算和文本解析；字幕/播放切换后取消、缓存与迟到结果。

**测试入口：** `Momoka.SubtitleDialogTests`、`Momoka.SubtitleDropOwnershipTests`、`Momoka.SubtitleTranslationTests`、`Momoka.SubtitleTranslationRuntimeTests`、`tools/test-subtitle-translation.py` 与主测试字幕夹具。

- [ ] 完整检查主次字幕、关闭字幕、同窗换片与翻译开关的恢复路径；真实字幕写入单列授权和验证缺口。

### R14 弹幕获取、匹配、发送与凭据端点

**范围：** Core Danmaku、MpvDanmaku 与弹幕配置/令牌；PlayerViewModel.Danmaku/Send、DanmakuPanel、相关设置；`momoka_danmaku.lua`、uosc `DanmakuInput.lua`；`website/worker` 凭据协议、Worker 路由与对应部署配置。

**检查：** 提供方与缓存隔离；唯一匹配及剧季集身份；获取/显示/发送生命周期；文本校验、速率/队列和重复提交；内置凭据加密封装、失效/轮换、错误与日志脱敏；客户端与 Worker 协议一致。

**测试入口：** `Momoka.DanmakuSourceBoundaryTests`、`Momoka.DanmakuPerformanceTests`、主测试 Danmaku、`tools/test-danmaku.py`、`website/worker/credential.test.mjs`。

- [ ] 用占位凭据核协议，不读取或打印真实 Secret，不执行真实发送或部署；与 R11 联审输入协议和退出取消。

### R15 设置体验、通知与服务器管理界面

**范围：** Shell Settings/SettingRow/SettingsWindow、通知页面与 VM、Dashboard、全部飞牛原生管理页面与 VM；Core ServerSettingsMenu/JellyfinServerSettingsMenu、`EmbyClient.ServerSettings.cs`、`EmbyWebConsole*.cs`。设置业务字段分别交 R02/R12–R14 联审。

**检查：** 搜索定位、导入/重置、未保存提示和关闭；完整区域显隐与恢复入口；通知提交状态；服务器权限与原生页差异；WebView 受信任来源注入、profile 清理、整页/页内导航代次、JS/CSS 功能完整性。

**测试入口：** `Momoka.SettingsLifetimeTests`、SettingsSearchNavigation/RowsMotion/PriorityMotion、`Momoka.DashboardNavigation.Tests`、`Momoka.ServerSettingsTabs.Tests` 完整 DOM wrapper、`Momoka.ServerSettingsReviewTests`、`Momoka.FnNasAccountEditTests`。

- [ ] 设置显示与真实生效分别验证；匿名 Dashboard 探针、DOM 夹具和真实 WebView 结果分开报告。

### R16 共享 UI、主题、图片缓存与性能

**范围：** Core Theming、图片/布局/字体/颜色规则、BudgetCache/LruCache/SharedWork/LoadGeneration；Shell Theme、Media 图片加载与缓存、通用控件、DialogDismiss、Focus/Hover、布局和材质。页面专属规则由各业务区主责。

**检查：** 主题角色、存活主题、高对比度回退；文字缩放、UIA/键盘/焦点；弹窗点外只取消与在飞写入保护；卡片回收串图、解码失败、缓存取消和预算；频繁滚动/缩放/布局事件是否造成有证据的卡顿或泄漏。

**测试入口：** `Momoka.DialogDismissTests`、`Momoka.AppearanceOpacityTests`、`Momoka.GaussianBackdropTests`、`Momoka.ImagePerformanceTests`、`Momoka.ResourceBudgetTests`、`Momoka.PageContractTests`、自动化句柄扫描。

- [ ] 公共机制只报告一个根因，逐消费页面核影响；源码契约不能替代真实窗口布局、透明效果和可访问性检查。

### R17 测试、诊断、自检与验证工具的可信度

**范围：** 全部测试框架/夹具/源码提取器/wrapper/断言基础设施；Core Diagnostics、SelfCheckRun、ShellProbeSections；Shell 根目录和页面/VM 的 Probe/SelfCheck 文件、Diagnostics 页；质量/回归/自检/截图/输入工具与基线。业务测试同时由所属区核对断言语义。

**检查：** 被测源码是否为当前完整路径；正反例是否有效；清单/分区/参数入口完整性；构建失败不得运行旧 DLL；超时/取消/报告 IO 失败的退出码和 finally；截图与指针证据真实性；隔离目录、匿名传输和用户互斥锁。

**测试入口：** `Momoka.ProbeFinalizationTests`、SettingsProbeContract/HomeOrderProbe/PlayerMotionProbe/PerfListDiagnostics、`tools/test-run-regressions.ps1`、`test-quality-tools.ps1`、`test-quality-dependencies.ps1`、`test-selfcheck-diff.ps1`。

- [ ] 60 个顶层项目及其参数、6 个辅助项目的调用者均有归属；明确跳过、不完整、环境失败与产品失败，不降低断言或基线来结束审查。

### R18 官网、文档、技能、配置与历史入库内容

**范围：** website 静态页面、`dist`、nginx/Docker/Wrangler 配置的部署部分；README/PRIVACY/许可/根规则、docs、.claude 技能及其脚本、.qoder 和入库 work 历史。弹幕 Worker 行为交 R14，字体/shader/原生许可与 R01/R11/R12 联审。

**检查：** 源码与部署副本一致性；下载/外链和静态内容脚本；配置/文档命令的实际副作用；规则、技能、帮助、基线与源码一致；当前引用有效性；历史材料的敏感信息与过期结论。

**测试入口：** 技能 `check-skill.py` / `test-check-skill.py`、`tools/check-server-support-docs.py`、`tests/test_server_support_docs.py`、`tools/check-main-tree-policy.py`、文档链接/文本差异检查。

- [ ] 全部剩余 tracked 文件均有处置；纯叙述、图片和旧证据注明内容类型，不冒充当前可执行代码或通过证据。

## 5. 批次顺序与并行安排

由主代理维护唯一台账，最多同时安排 3 名只读审查者；主代理负责验证候选、跨区交接和汇报。不把某一批当一次完成的超大任务：通常按完整业务子链拆为 20–40 个自有文件的阅读单元，大型 partial 类按成员拆，实际规模以能完整追踪行为为准，不按工时强制截断。

| 批次 | 并行阅读安排 | 进入下一批的条件 |
| --- | --- | --- |
| 0 范围 | 主代理建台账；子代理核文件/测试/资源入口 | 所有文件有归属，测试入口差异已列明 |
| 1 基础与身份 | R01、R02、R03；R17 先看 runner/隔离/报告框架 | 有明确的构建、设置、身份和证据契约 |
| 2 协议与副作用 | R04、R05、R07；随后 R09 | 高后果身份/写入候选已有证据或明确缺口 |
| 3 业务流程 | R06、R08、R15 | 浏览/订阅/设置/管理调用链与协议区完成交接 |
| 4 播放 | R10、R11、R12；先共享生命周期和选项契约 | 后端×管线矩阵、资源归属和选项落地有记录 |
| 5 文本与共用界面 | R13、R14、R16 | 换片、取消、恢复和控件消费路径已联审 |
| 6 验证与遗漏复核 | R17 剩余测试、R18、主代理跨区检查 | 台账无漏项，报告和验证矩阵可核对 |

无需等全项目读完才提交发现：每个阅读单元结束报告已确认问题和证据缺口。出现账号串用、凭据暴露、数据误删、不可恢复设置丢失或持续崩溃的可靠证据时及时汇报；不以并行速度牺牲证据核实。

## 6. 必须联合检查的跨区场景

| 条件 | 预期与联审分区 |
| --- | --- |
| 请求进行中切账号、注销、权限撤销，或配置 A→B→A | 旧响应不覆盖新页面；写入仍绑定允许的来源；R02–R09、R15 |
| 预览/确认弹窗打开后修改选择、目标或关闭弹窗 | 旧计划失效、关闭只取消、不会重复执行；R07–R09、R13、R16 |
| 写入成功但补充读取失败，或超时导致结果不明 | 明确反馈状态，不无条件重试有副作用的操作；R04–R09、R15 |
| 连续起播、自然换集、切版本、退出或后端崩溃 | 资源与回调按会话清理，字幕/弹幕/翻译/选项无串台；R10–R14 |
| 设置导入、重置、关闭上级开关、再恢复 | 可见性与实际选项一致，偏好保留、恢复入口可达；R02、R12–R16 |
| 同名/同 ID 跨来源条目与图片，删除后迟到刷新 | key 与状态归属含正确来源，不复活旧目标或串图；R03、R06、R07、R16 |
| 快速搜索、分页、返回、窗口缩放和卡片回收 | 无旧页覆盖、重复项、选择/滚动丢失和无限布局循环；R06、R08、R15、R16 |
| 构建/报告/截图/子进程失败 | 不运行旧产物、不发布成功、不掩盖跳过，保留证据；R01、R17 |

## 7. 验证如何执行与记录

### 7.1 不重复全量运行

先审读取路径与测试断言，再选择能证实本区结论的最小现有测试。稳定基点上同一检查通过后不因换分区重复跑；相关代码、环境或候选问题变化时才重跑。完整源码审查不能由静态工具或测试代替，测试通过也不代表真实 UI 或服务器行为已证实。

当前可用命令形式如下；执行前仍须核对对应脚本/README。这里列的是待执行步骤，不是本轮通过记录。

```powershell
# 仓库根目录；先确认 SDK 与路径，再用当前 Release 输出。
& "$HOME/.dotnet/dotnet.exe" restore ./Momoka.sln --disable-parallel -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./Momoka.sln -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false

# 仅在回归清单与顶层项目一致后使用；runner 自行逐项目 restore/build/run。
& ./tools/run-regressions.ps1 -List
& ./tools/run-regressions.ps1 -Suite @('Momoka.SettingsLifetimeTests', 'Momoka.SettingsPersistenceTests')

# 需要完整回归时使用；保留 external/skipped 的非零状态，并单独完成其前置与 wrapper。
& ./tools/run-regressions.ps1 -All

# 静态文本与脚本；不存在产品/UI通过含义。
& ./tools/check-diff.ps1 -All
& ./tools/check-scripts.ps1

# Worker 匿名测试，不部署、不读取真实 Secret。
node --test website/worker/credential.test.mjs
```

- 全量回归清单校准前，可按各项目 README 单独还原/编译/运行，保留差异为未修问题；不能删掉项目或清单守卫求通过。
- `run-regressions.ps1 -All` 后另核主测试显式参数入口、两个 wrapper、DOM、Lua、原生测试。单独 DOM wrapper 的必填参数是 `-Node`、`-NodeModules`、`-Browser`；可选 `-Dotnet`、`-EmbyManager`、`-OutputRoot`，从实际环境解析路径，不在计划填猜测值。
- Lua 入口为 `py -X utf8 tools/test-uosc.py --output <全新目录>`，弹幕与字幕翻译脚本同样需要 `--output`；它们依赖当前树真实 libmpv，不能用不存在的 DLL 路径或错误版本证明通过。
- `quality.ps1 -Tests` 会串接全部专项；重复检查使用 `npx`，可能联网。不要为本次审查升级分析器、安装新的门禁或把它称作纯离线检查。
- 运行前阅读适用的验证技能。Shell 分区选择以当前 `ShellProbeSections.All` 与实际 Case 注册为准，手动 `search-preview` 不算自动全量覆盖。完整 Shell 探针也不包括所有独立探针。
- UI 验证按受影响页面、宽窄窗口、各存活主题、服务器与恢复状态取真实截图；记录匿名/真实数据边界。源码链接夹具、DOM 与真实 WinUI/WebView 分别标注。
- 当前三条集成本地探针不覆盖独占、uosc 或外部 mpv；缺少条件时保留具体矩阵缺口。没有真实播放授权就不通过播放补齐。
- 纯审查不会刷新交付目录或执行签名/安装。以后若获授权修复，按最终代码完成对应测试、主树整合后验证与发布；不把本次审查报告当交付凭据。

### 7.2 单区报告格式

每份报告只需包含：

1. 基点/实际变更、主责文件和已读调用链。
2. 已确认缺陷，按 P0–P3 排序：位置、触发、后果、证据、最小修正及建议回归场景。P0 为已证实的严重数据/凭据危险；P1 为应优先处理的重大功能/可靠性问题；P2 为普通可达功能缺陷；P3 为影响较小但确需修正的问题。
3. 重要未决风险：缺少的条件、最小核对方式、阻塞什么结论。不得直接升级成确定缺陷。
4. 实际运行的命令、提交/文件 hash、退出码、通过/失败/跳过和证据目录；未运行项明列。
5. 与其他分区的交接、去重根因和处置状态。零发现时写“在本轮范围内未发现需要处理的缺陷”，并说明验证限制。

所有必要候选由主代理重新读源码或证据后进入总表；严重写入/身份/凭据问题另由第二位审查者独立核对。相同根因合并为一条，保留受影响入口。

## 8. 完成与停止条件

- [ ] 每个跟踪文件及被实际引用的本地依赖已分区；全部可审源码已读，非代码/生成/二进制的处理方式有据可查。
- [ ] 18 区报告齐全，跨区场景全部有结论或明确未决条件；无“待分配”“忘记查看”的文件。
- [ ] 确认缺陷有优先级、证据、最小修正方向和建议回归；待核实事项没有被包装成缺陷，也没有被隐藏。
- [ ] 测试入口矩阵覆盖项目、参数、wrapper、提取器、Lua、Worker、DOM、原生与 UI；每项有实际状态，跳过不算通过。
- [ ] 第 0 批与结束时对比文件/提交差异；新改动只复核其影响范围，旧结论未被无依据复用。
- [ ] 交付一份总报告：已审范围、必要缺陷、验证结果、尚缺条件和建议处理顺序。已确认缺陷可以处于“待修复”，但必须如实列出。

**审查完成**表示约定范围已检查并形成完整、诚实的报告，允许存在明确列出的待修缺陷和运行缺口；它不表示代码没有缺陷。**修复完成、所有验证通过、主树交付完成**是另外三个状态，不能用“18 区已审完”替代。可选的风格或抽象建议不延长本次审查。

## 9. 本计划文档的检查记录

本轮只读盘点与计划编写，不开展 18 区正式审查。交付前核对：统计总和、分区编号、目录/入口存在性、测试登记差异、命令参数与文档链接，以及文件编码/行尾/空白。本轮不构建、不运行应用测试或 UI、不访问真实服务、不发布。检查结果在本次提交和 PROGRESS 记录中说明。
