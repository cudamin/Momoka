---
name: "momoka-moviepilot"
description: "Develop, review and verify Momoka's MoviePilot integration: availability, media-source identity, searches, torrent results and download confirmation, subscriptions, library playback and reorganization. Use for MoviePilot C# or WinUI changes, upstream source comparisons, dependent-entry visibility and safe live troubleshooting. Review the official GitHub source and synchronize applicable changes with the affected local components and tests. The playback pipeline itself belongs to the playback skill."
---

# Momoka — MoviePilot integration

本技能保存 MoviePilot 接入中容易误判的协议与验证方法。**授权、凭据、协作、构建与交付要求以 [CLAUDE.md](../../../CLAUDE.md) 为准**，不以本技能或历史实测记录替代本轮授权。

## 开始之前

- 当前工作树里的 `src/Momoka.Core/MoviePilot/` 是协议与业务规则；Shell 的 `MoviePilot*ViewModel` / `MoviePilot*` 视图负责界面；服务注册在 `Composition/ShellServices.cs`。
- 先确认实际 MoviePilot 版本。过去某次连接成功不代表服务器仍是该版本。`dashboard/system` 的 `version` 是可靠入口；`openapi.json` 未必可用（在 API 基址下曾返回 404），拿不到就以对应 tag 的官方前后端源码为准，不要猜字段，也不要因此跳过版本确认。
- 不整份输出 OpenAPI、设置或资源回话。按当前接口投影字段；`torrent_info` 可能带站点 Cookie、下载凭据和私有 URL。
- 本仓库 ignored `work/` 的上游快照和诊断宿主只可作为经检查后的本机辅助；干净检出不能依赖它们。业务测试使用匿名夹具与 `StubTransport`；默认传输的登录跳转边界使用下方独立双回环测试。

## Review the upstream source every time

- For every MoviePilot feature review or change, open the relevant official frontend and backend source on GitHub before assessing the local implementation. Use the [source map and comparison procedure](references/upstream-download.md); the download entries are explicit, and other features follow their imports and routes in the same repositories.
- Resolve the live branch and commit, then check the tag or revision applicable to the configured server. Record both revisions and the comparison result in the work record. A saved screenshot, local `work/` snapshot or previous review does not establish the current source.
- If an upstream change affects the feature's visible behavior, field mapping, supported options or request/response contract, update the affected local components and regression tests in the same task. Do not stop at listing the difference or updating this skill. Trace downstream callers so the new choice takes effect in the actual request.
- Apply changes within Momoka's supported feature and server-version scope. Record unrelated changes as such; do not add unsupported music flows, upgrade dependencies, or copy a frontend/backend mismatch merely because upstream contains it. Preserve CLAUDE.md's identity, cancellation, credential and verification boundaries.
- If current source or the applicable server revision cannot be established, state the missing evidence and the exact affected comparison. Do not label the implementation synchronized. This procedure runs during a review; it does not create a background monitor.

## 按问题选择资料

- 搜片、来源选择、跨库编号、同名结果、订阅身份：[来源与搜索](references/search-identity.md)。
- 订阅列表与管理、文件统计（已入库／待入库／缺集）、已入库内容的播放入口：[订阅与入库](references/subscriptions.md)。
- 原记录、路径、预览、重新整理、部分成功：[整理与副作用](references/reorganization.md)。
- Download confirmation, upstream GitHub paths and local component mapping: [Upstream source and download parity](references/upstream-download.md). Review the source even when the request appears to concern layout only.

## 可用性与入口显隐

遵循 [CLAUDE.md 的显隐规则](../../../CLAUDE.md#功能可用性与显隐)，实现与验证分别联读 [Shell 技能](../momoka-winui-shell/SKILL.md#capability-dependent-visibility)和[验证技能](../momoka-verification/SKILL.md#capability-dependent-visibility)。

- 入口判断使用 `MoviePilotService.Available`：开关已开启，且当前配置在本进程成功连接。`Enabled`、历史连接时间和缓存令牌都不能单独作为可用证据；配置变化通过 `AvailabilityChanged` 撤销旧状态。
- 核对搜索面板、主页订阅、侧栏「订阅」那一段（两条入口与那条分区标题同进同退，见 `ShellPage.Sidebar.cs` 的 `SyncSidebar`）、订阅管理、条目菜单及已打开的附属窗口。`ItemMenu.MoviePilotBlock` 在不可用时返回空组；`ShellPage.MoviePilot.cs` 收起窗口和对话框、退出不可用订阅页并清理前进/后退历史。新增入口须接入同一变化链，保留设置中的启用、连接配置和状态反馈。
- 隐藏不删除连接配置，也不解除下载、订阅和整理的防重状态。执行入口仍核对当前连接与结果身份，迟到响应不得重新显示旧面板。
- AniBT scrape is two-stage: prepare the actual sealed preview/final Emby target, show it in the same dialog, then require a separate confirmation before any write. Cancel/change does not preserve the old confirmation token; unknown or attempted writes cannot be unlocked by preparing again. See [confirmation terrain](references/reorganization.md) and [Momoka.AniBtConfirmationTests](../../../tests/Momoka.AniBtConfirmationTests/README.md); no raw mutable request/candidate is exposed to UI.
- AniBT identification retains its current-default-source restriction; independent version searches remain usable on an available secondary source. Pass the actual source capability to `ItemMenu.For`, check it when adding late genre rows, and reject stale direct invokes. An open menu latches source ownership loss at notification time before queueing Hide, so A→B→A cannot revive its old click/read continuations; reopening restores availability. [Momoka.AniBtEntryTests](../../../tests/Momoka.AniBtEntryTests/README.md) exercises actual menu and Home.MenuRead control flow without windows or integration writes.
- 订阅列表页 `MoviePilotSubscriptionsPage.Attach` 把 `request.Key` 写入页面 `Tag`，让 Shell 返回／前进后的 `AdoptContentTag` 恢复当前目的地，并保持同页点击短路。不要只设置初次 `Open` 的 `_current` 而漏掉页面自己。源链接页面契约项目覆盖 Tag／重入／释放；实际 Frame 交互、滚动与前进栈仍另做 UI 验证。
- `ShellNavigationProbe.MoviePilotAvailability.cs` 提供仅开启但未验证、连接成功、断线、恢复和关闭的假服务器场景。按当前断言核实覆盖；普通搜索及独立来源的入口按自身依赖判断，不能只因位于同一面板就一并隐藏。
- Missing-episode metadata reads keep the original connection stamp. Recheck that stamp with `Available` and `IsCurrentConnection` after the detail/parent waits and before constructing the dialog; never adopt a replacement connection merely because it is now available. Preserve the open-dialog availability subscription and final scope/page/search checks. [Momoka.MissingEpisodesAvailabilityTests](../../../tests/Momoka.MissingEpisodesAvailabilityTests/README.md) extracts the real Shell method and links the real dialog/rows, checking visibility values and actual XAML bindings without claiming pixels or real clicks.

## Login redirects and the credential boundary

- `MoviePilotClient` owns its transport independently of Emby/Plex. Its default `SocketsHttpHandler` and directly injected native `SocketsHttpHandler` / `HttpClientHandler` have `AllowAutoRedirect=false`; arbitrary test handlers are response substitutes, not proof of the native transport. Do not loosen a shared boundary or add a project-wide transport abstraction to repair this path.
- Reject every login 300–399 response at the response headers, before buffering or parsing its body. This includes same-origin, relative/absolute, missing-Location and HTTPS-to-HTTP redirects. There is no method conversion, body replay, destination trust decision or redirect chain: the follow limit is zero. The initial configured request may already have reached the server; refusal does not mean it was never submitted.
- Keep the original redirect status and actionable `不自动跳转` / final API origin guidance, but do not attach the redirect body or echo `Location`; either can contain credentials. Users must configure the final API scheme/host/port, without a reverse-proxy path rewrite that requires a redirect. Same-origin canonicalization is deliberately not automatic. API writes and image requests share this transport refusal; do not retry a rejected or uncertain side effect for convenience.
- Header-first login must retain the existing total client timeout and response-buffer limit for direct 200/error bodies. Preserve caller cancellation, internal-timeout classification, direct-token parsing, the dedicated login 401 message and existing 403/status/body semantics. A login rejection is not `MoviePilotTokenExpiredException` and must not enter token-refresh replay.
- `MoviePilotLoginRedirectTests.Register()` is called by `MoviePilotTests.Register()`. A dedicated `moviepilot-login-redirect-fixture` CLI entry should register that suite and run the existing `TestHarness`, without duplicating it or changing the harness. The suite uses the production no-argument client and two atomically reserved loopback ports; assert source and next-hop request, username/password receipt counts, methods and whether a session/result was returned. Cover 301/302/303 method conversion refusal, 307/308 replay refusal, same-origin forms, finite/endless chains, direct success/401/403, cancellation, stalled redirect bodies and bounded direct-body reads.
- The two HTTPS-to-HTTP policy cases inject only a redirect response, explicitly without TLS. They establish response refusal, not a TLS handshake, certificate validation, or real service behavior. Keep this distinction in reports. Anonymous loopback writes and placeholder credentials are not production MoviePilot login evidence.

- The loopback observer counts at TCP acceptance, independently of completed HTTP parsing; pending connections invalidate zero-next-hop claims. An incomplete anonymous request canceled during cleanup remains counted. All loopback receipts are asserted only after stopping and joining both receive tasks, with pre-close pending connections retained. Two observer cases cover half-request cancellation and a late accepted request invalidating an early zero sample. Do not reuse the old 41/42-case input hashes as final 43-case evidence.

## 三种身份不要混为一谈

1. Emby 条目 ID 是库内导航身份，不是 MoviePilot 媒体 ID。MoviePilot 文件统计里的 `itemid` 属于它自己配置的那台媒体服务器，同样不是当前 Emby 的编号——跨服务器播放必须先按主身份或文件路径在当前账号下重新定位。
2. MoviePilot 媒体以 `media_source`、`media_id` 和必要的媒体类型联合定位。相同数字在不同来源、甚至 TMDB 的电影与电视剧中，可能表示完全不同作品。
3. `torrent_info` 是可下载资源；站点名称不是媒体数据源，种子 `page_url` 不是媒体详情页，`enclosure` 不是可安全交给系统的网页地址。

主身份和 IMDb 等交叉编号必须分开显示、解析和回传。不能只在画面上贴“豆瓣”标签，却仍发 TMDB 的编号。缺少可信身份时隐藏相应操作入口，业务层仍拒绝执行，不能按同名作品猜测。

## 资源列表

- `search/media/{id}` 是已选媒体的精确搜索；带 `media_source`、类型 `mtype` 和已指定的 `season`（0 是特别篇，缺失不猜 1）。`search/title` 是关键词搜索，不能借用前一次选择的媒体身份。
- 精确搜索只保留与所选来源、编号和类型一致的完整 `media_info`。即使身份一致，回话显式返回 `media_info_is_target=false`，或解析得到的非空 `match_status` 不是 `exact`，仍须拒绝下载并隐藏下载入口，保留不匹配的状态说明，不能把候选当精确命中。缺少这两个标志或 `match_status` 为空字符串本身不构成否决，仍须核对身份、类型和媒体快照；对应规则在 `MoviePilotMediaParser.ToResource`。
- 带可信媒体信息的下载走 `download/` 的 `media_in`，不能退回 `download/add` 再按种子标题猜片；后者没有影视 `mtype` 参数。
- 搜索结果绑定获取时的连接与账号。订阅、下载和整理的防重状态由服务持有，不随重搜、新行或窗口重建清空；网络结果不明时保留“请先核对”，不开放直接重试。当前防重仅在本进程有效，不是跨进程幂等或服务器最终完成证明。
- 资源字段来自 `torrent_info`：`page_url`、`description`、`labels`、`pubdate`、`downloadvolumefactor`、`uploadvolumefactor`、`freedate`、`hit_and_run`。清晰度通常来自 `meta_info.resource_pix`。
- 体积可能是浮点 JSON 数字，优惠因子可能是字符串；缺失不能默认为“免费”。原始数据保留在内存用于下载，显示模型只取必要字段。
- 过滤当前结果时保持行对象、集合实例与下载状态；显示“符合条件 / 总数”，清除恢复原结果，区分没搜到和被过滤为空。
- 新查询、空查询、来源变化与页面释放均须使旧请求失效。切到资源面板时隐藏底层可交互内容，新媒体搜索时收起旧资源层。
- “种子页面”只接受合法 http(s) 网页地址；不执行本地文件、自定义协议，不把 Cookie 或下载凭据拼进启动参数。外部浏览器登录状态不等于 MoviePilot 的服务会话。

## 订阅管理与入库播放

- 列表、单条、文件统计，以及编辑／暂停／搜索／重置／取消的接口、字段与版本差异见 [订阅与入库](references/subscriptions.md)。搜索与重置的方法在各版本间对不上（公开文档只登记 GET，官方后端同时注册 GET 与 POST），客户端优先 POST、**仅在明确 405 时**改发 GET；超时不改发、不重放。全量列表靠省略 `page` / `count`，别把一批分页结果当全量。
- `total_episode` / `lack_episode` 是订阅目标范围内的下载进度，不是入库进度。已入库只由 `subscribe/files/{id}` 的 `library` 决定；只有 `download` 有文件记录而 `library` 没有时才显示“已下载 · 待入库”，两者都有仍是“已入库”；缺集包含已下载待整理和尚未播出的集。空回话、错误与 `subscribe:null` 都是“统计不可用”，不能显示成 `0 / N` 或全部入库。
- 订阅写操作按有副作用处理，与 HTTP 方法无关：只提交实际改动的字段，提交中锁住再次提交，结果不明时按订阅逐条保留状态、要求用户先核对服务器再显式解除，不自动重发。回执只代表请求已提交，不代表已找到资源或已入库。
- 已入库内容的播放入口必须先在**当前 Emby 账号**下重新定位：按主身份查 `AnyProviderIdEquals=<provider>.<mediaId>`，电视剧另核季号与集号；文件统计里的 `itemid` 属于别的媒体服务器，只能在 `server_type` 与文件路径同时对上时才接受，多候选先按入库文件路径筛。找不到、无权读取、待入库与缺集都隐藏播放入口并拒绝起播，保留条目与状态说明；页面只读取，点播放才起播。

## 验证要回答的问题

- 先用 Core 单测验证来源/编号配对、类型冲突、缺身份、数字或字符串字段、完整/部分失败回执；服务测试断言路径、参数与实际请求次数，而不只判断文案。
- 精确资源覆盖身份相同但 `media_info_is_target=false`、解析得到非空且非 `exact` 的 `match_status` 的场景，两个否决条件分别断言；另覆盖标志缺失或 `match_status` 为空字符串、且其余条件满足时仍允许下载。沿用 `MoviePilotQuerySafetyTests` 的匿名夹具，并断言下载请求体能否构造，而不只检查显示文案。
- 实际只读验证时，白名单限制为本轮需要的读取。某些 POST（如 `storage/list`、`preview:true`）在对应版本中是只读，必须按请求体核对；不能因为都是 POST 就混入提交，也不能因为都是 GET 就放行 `transfer/now` 等有副作用入口。
- GUI 分开验证搜索结果、来源标签、资源过滤、空/错/忙状态，以及下载和订阅的确认/取消。取消不是下载或订阅已成功的证据。
- 订阅按请求方法、请求体与**请求次数**断言：POST 成功、POST 405 才改发 GET、超时不得改发、结果不明锁定后不重发；编辑的请求体只含实际改动的字段。
- 文件统计覆盖电影零号条目、`library` 的字符串与数组两种形态、`subscribe:null`、空 `episodes`、范围外的集被过滤、未播出集被补齐，以及“统计不可用”不显示为全缺或全入库。
- 入库播放覆盖跨服务器 `itemid` 不被直接信任、同名多候选、路径唯一命中与大小写、无权或不存在条目的播放入口隐藏及执行拒绝；断言播放交给的是匹配到的那个 Emby 条目。
- 新控件检查自动化句柄；截图检查受影响尺寸，颜色变化按主规则覆盖主题。不要用属性断言代替实际画面。
- 真实整理只在明确授权的条目范围执行，完成后核对原源仍符合预期、目标文件、整理新记录和 Emby 索引；`accepted` 不等于 `completed`，Emby 收录不等于已播放。
- 子代理遇到暂时性限流且允许重试时，保留任务身份和已完成工作，适当退避再续跑；遵守用户的并行上限。权限拒绝、用户取消、确定性错误或有副作用请求结果不明，都不能盲目自动重放。

## 交付

按 [CLAUDE.md 的改动类型表](../../../CLAUDE.md#四道闸门与日常交付) 选择本轮验证与交付要求，具体命令见 [开发与验证](../../../docs/开发与验证.md)；订阅与入库功能的对外说明在 [MoviePilot订阅](../../../docs/MoviePilot订阅.md)。报告实测、未覆盖与失败项；不把历史失败当成通过，不用改基线消除未经解决的红项。项目规则和入口从源码获取，避免在技能里固化当前版本号、测试数、真实账号或某次媒体路径。
