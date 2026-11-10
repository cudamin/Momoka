# 订阅管理、文件统计与入库播放

2026-10-08 定点核对了官方公开文档（`api.movie-pilot.org` 的 `openapi.json`）、官方后端 tag `v3.1.2-1` 的源码，以及一台实际运行 `v3.1.2-1` 的服务器（只读）。当时 18 条订阅中 17 条返回文件统计，入库内容全部匹配到当前 Emby 的条目；写入与播放均为 0。**这是带日期的证据，不是"服务器现在仍是这样"的证明。** 协议随版本变化，动手前按 SKILL.md 重新确认版本并对照对应 tag 的源码。

## 接口与版本差异

| 能力 | 接口（相对于 `/api/v1/`） |
| --- | --- |
| 全量订阅 | `GET subscribe/`，省略 `page` / `count` 保留全量语义 |
| 单条订阅 | `GET subscribe/{id}` |
| 文件统计 | `GET subscribe/files/{id}` |
| 编辑 | `PUT subscribe/`，请求体 `id` 加实际改动的字段 |
| 暂停／恢复 | `PUT subscribe/status/{id}?state=S` / `?state=R` |
| 立即搜索 | `POST subscribe/search/{id}` |
| 重置 | `POST subscribe/reset/{id}` |
| 取消订阅 | `DELETE subscribe/{id}`，不删已入库文件 |

- **搜索与重置的方法在各版本之间对不上。** 官方公开文档只登记 `GET`；官方后端 `v3.1.2-1` 的 `app/api/endpoints/submaintenance.py` 对这两个路径**同时**注册了 `@router.get` 和 `@router.post`，该版本前端也走 POST。客户端优先 POST，只在明确收到 HTTP 405 时改发 GET。超时、断线、5xx 都不是"方法不对"，不触发改发，也不重放。
- 分页靠 `resolve_compatible_pagination`：`page` 与 `count` 都省略才是全量，只给一个或都给了就是分页。集合总数走响应头（`openapi_extra` 声明的那个），不在正文。主页要全量就别带分页参数，也别把一批分页结果当全量。
- 列表字段（该版本实测）：`id`、`name`、`year`、`type`、`keyword`、`media_source`、`media_id`、`season`、`poster`、`backdrop`、`description`、`include`、`exclude`、`quality`、`resolution`、`effect`、`total_episode`、`start_episode`、`lack_episode`、`completed_episode`、`state`、`last_update`、`sites`、`downloader`、`save_path` 等。只投影界面要用的字段。
- 上述只读核对中，该服务器 `GET /api/v1/openapi.json` 返回 **404**，而同一会话里 `GET /api/v1/dashboard/system` 正常返回 `version`。这是该次部署的观测；项目的 `MoviePilotProbe.RunAsync` 在登录后读取 `dashboard/system`、`mediaserver/clients` 和 `download/clients`，没有 OpenAPI 请求。拿不到 schema 时，**契约以对应 tag 的官方源码为准**；不能跳过版本确认，也不能猜字段。

## MP 订阅库与刷新

2026-11-10 核对当前 Momoka：

- 首页媒体库列表的「MP 订阅库」（`mp-subscriptions`）由 `HomeViewModel.MoviePilotLibrary.cs` 创建，仅是本地导航卡；MoviePilot可用时显示，入口汇总电影和电视剧。它没有媒体服务器Source，不请求四后端的库目录或入库WebSocket，也不存进媒体快照。
- 首页两排订阅与 `MoviePilotSubscriptionsPage` 复用 `MoviePilotHomeSubscriptions`／`MoviePilotSubscriptionsViewModel`，读取 `GET subscribe/` 全量后分类；两排共用一次读取，独立页面各有自己的寿命。进入／返回、显式刷新、连接恢复及本页已确认管理操作会更新；没有订阅内容计时器。连接健康且停留原页时，外部新增订阅或入库不会使该列表自动刷新。
- 订阅详情按需读取 `subscribe/{id}`、`subscribe/files/{id}`，并补播出日期与当前媒体服务器目标匹配；没有后台入库通知订阅。列表的订阅进度和详情的文件入库统计不是同一份数据，不能把媒体服务器货架刷新当作MP统计已更新。
- `ShellPage.MoviePilot.cs` 仍每30秒检测 `RefreshAvailabilityAsync`，可用时读 `HistoryCapabilitiesAsync`。健康状态不变不会每次触发订阅回读；恢复可用才通知订阅属主。不能把这个连接检测报成“MP每30秒刷新影片”。
- 本地原一分钟订阅轮询已按用户要求于2026-11-09删除，以解决加载条引起的首页周期跳动；后续MP库复用这套行为。`PROGRESS.md` 保留匿名驻留与手动刷新验证记录，`Momoka.PageContractTests` 和 `MoviePilotSubscriptionPageStateTests` 是页面寿命／状态回归入口。这是当前产品选择，不是上游没有轮询的证明。

同日重新只读核对官方GitHub：前端v3提交 `609ea05eac7efc7c2cb31edcf5364d26c9c592f1`，后端v3提交 `af963b47b350f07b7ee407ab54a5eb9b4091ab35`。本轮没有读取真实部署版本，不能把分支HEAD当成用户服务器版本。

- [SubscribeListView.vue](https://github.com/jxxghp/MoviePilot-Frontend/blob/609ea05eac7efc7c2cb31edcf5364d26c9c592f1/src/views/subscribe/SubscribeListView.vue#L625)：页面活动且浏览器可见时，每2500毫秒查询 `subscribe/execution/batches?limit=10`；批次签名变化，或存在活动卡／读取错误且距全量请求至少15秒，才静默回读 `subscribe/`。挂载、激活及重新可见也触发相应读取。这是条件轮询，不是影片入库推送。
- [subexecution.py](https://github.com/jxxghp/MoviePilot/blob/af963b47b350f07b7ee407ab54a5eb9b4091ab35/app/api/endpoints/subexecution.py#L119) 的批次接口返回当前用户可访问订阅的搜索执行进度；[subscribe.py](https://github.com/jxxghp/MoviePilot/blob/af963b47b350f07b7ee407ab54a5eb9b4091ab35/app/api/endpoints/subscribe.py#L119) 为列表补 `execution_status`。任务进度变化不能直接证明新增影片已入库。
- [订阅日志流](https://github.com/jxxghp/MoviePilot/blob/af963b47b350f07b7ee407ab54a5eb9b4091ab35/app/api/endpoints/subexecution.py#L214) 是 `subscribe/{id}/logs` 的SSE，给日志视图逐行显示；不要将日志流或活动弹窗轮询当作订阅库入库通知。

因此本地订阅列表刷新与官方当前策略存在明确差异。用户随后于2026-11-10明确要求“mp官方怎么弄的你就怎么改”，覆盖此前删除一分钟轮询的选择；目标是MP汇总／电影／电视剧订阅列表采用上述条件批次轮询，首页本地扩展订阅排和详情不套用这套列表规则。该产品修改尚未完成：Core测试生成被内容过滤器阻止，未合入或发布未完成代码。此前无轮询只是当前实现，不再是保留它的用户要求。此次只读源码核对没有证明真实MP入库事件或推送送达。

## 三个进度不是一回事

- `total_episode` / `lack_episode` 是**订阅目标范围内的下载进度**。`total_episode - lack_episode` 不等于已入库集数；界面上叫"订阅进度"，不要叫"下载进度"，更不能当入库进度。
- 订阅文件统计中的已入库**只由** `subscribe/files/{id}` 的 `library` 决定。有 `library` 文件记录就是"已入库"，即使同时有 `download`；只有 `download` 有文件记录而 `library` 没有时才是"已下载 · 待入库"，两者都没有则是"缺集"。这些状态用于有效统计中的目标条目，统计不可用不套用缺集状态。
- 缺集是目标范围内**所有未入库**的集，包括已下载待整理的和尚未播出的。按 `start_episode` / `total_episode` 过滤，不混入范围外的集。
- `state`：`S` 暂停、`R` 订阅中、`N` 或 `P` 待处理。暂停不代表入库事实停止。

## 文件统计回话

`GET subscribe/files/{id}` 返回 `{ subscribe: {...}, episodes: { "1": {...}, "2": {...} } }`：

- 电影用**零号条目** `episodes["0"]`，不是 `episodes["1"]`。
- 每集字段：`title`、`description`、`backdrop`、`download`、`library`。`backdrop` 是该集的横版剧照。
- **每集没有播出日期。** `SubscribeEpisodeInfo` 的 schema 里就只有上面那几个字段，播出日期得另取 TMDB 季集接口 `GET tmdb/{tmdbid}/{season}`（可带 `episode_group`；回话是**裸的集对象数组**，字段 `episode_number` / `air_date`）。只对 `media_source` 为 `themoviedb`、`media_id` 是数字、季号明确的电视剧发这一趟；其他来源（豆瓣／IMDb 等）不发请求，屏上就不显示播出日期，也不判「已播出」。空 `air_date`、缺集号、解析不出来的日期都当「没有日期」，**别拿猜的值去判已播出**。订阅带 `episode_group` 时必须一并带上，否则集号是另一套、对不上屏上那几集。
- `library` / `download` 是数组，元素带 `file_path`、`server` / `server_type`、`itemid`；旧版本可能直接给字符串路径。两种形态都要认。
- 服务端按 `for i in range(start_episode or 1, total_episode + 1)` 补齐目标范围内缺的集。客户端可以照此补出未播出的空条目，但这些条目**既没有文件也没有剧照**，只能作为"未入库"出现在缺集里。
- `subscribe` 为 `null`、`id` 对不上、`episodes` 不是对象、请求失败——全都是"**统计不可用**"，不是"全部未入库"。空响应和错误不能显示成 `0 / N`，也不能显示成已全部入库。该服务器上确实有一条订阅返回 `subscribe:null` 与空 `episodes`。

## 写操作的边界

- 编辑只提交**用户实际改动**的字段，`id` 必带。普通编辑支持 `keyword` / `include` / `exclude` / `quality` / `resolution` / `effect` 与电视剧的 `total_episode` / `start_episode`；季选择另支持显式修改 `best_version` / `best_version_full`。其他字段——站点、下载器、保存路径、其他客户端留下的设置——保持服务器当前值，不回写。清空某项发 `null`，不发空串。
- 所有订阅写操作（PUT / POST / DELETE）都按有副作用处理，与 HTTP 方法无关。提交中锁住再次提交；拿不到明确回执就标记"结果不明"，要求用户先去 MoviePilot 核对再显式解除限制，**不自动重发**。超时不证明服务器没做。
- 回执只说明请求已提交。搜索／重置成功不等于已找到资源，更不等于已入库。
- 按订阅 `id` 单独记账，不把一条的失败盖到整批。
- 连接与账号身份绑定到每批结果：切服、离页、取消之后，旧列表、旧确认与迟到的图片都不应重新生效。

## 入库播放

"MoviePilot 说已入库"和"当前媒体服务器能播"是两件事，中间隔着一次跨服务器匹配；当前定位实现支持 Emby 与 Jellyfin，FnNas／Plex 没有这条主 Provider 定位实现，不能按旧文案推导为 Emby 排他：

- **文件统计里的 `itemid` 属于 MoviePilot 配置的那台媒体服务器，不是当前 Emby 的条目 ID。** 一条订阅可能关联多台服务器；直接拿它当 Emby 条目导航会播错内容。只在 `server_type` 是当前服务器类型、`itemid` 只含字母数字与连字符时才尝试，并且**必须再用文件路径核对**才接受。
- 主路径是按主身份在当前账号下重查：`Users/{userId}/Items?AnyProviderIdEquals=<provider>.<mediaId>&Recursive=true&IncludeItemTypes=<电影|剧集>`。provider 只认 `themoviedb`→`Tmdb`、`douban`→`Douban`、`imdb`→`Imdb`、`tvdb`→`Tvdb`，且编号格式要合法（IMDb 必须 `tt` 开头）；认不出来就不匹配，不按标题猜。
- 默认剧集组仍核原始季号与集号（含 `IndexNumberEnd` 区间），电影要求零号条目对电影类型。非默认 `episode_group` 的组季集不是原始编号，只在主 Provider／ID／Series 身份已核的全剧候选中按唯一入库路径定位，返回原始季的 `Item／Parent／Episodes`，字典键仍是组内集号。
- 默认组多条候选先按入库路径筛，路径唯一命中才用它；否则数字候选唯一才接受。非默认组缺可信路径或多候选命中时不给目标，不凭数字唯一猜测，也不以文件 `itemid` 逃出主媒体核对。路径只做字符串比较，不访问、不启动路径；Windows 驱动器／反斜线 UNC 忽略大小写，Unix 区分大小写。
- 找不到、无权读取、待入库、缺集都不启用播放入口。页面本身只读取，点播放才起播。

## 季选择弹窗的界面对照（2026-10-28）

- **界面事实来源是上游组件，不只是 `subscribe` 接口。** 季选择弹窗读 `jxxghp/MoviePilot-Frontend` 的 `src/components/dialog/SubscribeSeasonDialog.vue`、同目录 `__tests__/SubscribeSeasonDialog.spec.ts`，以及 `src/composables/useMediaSubscribe.ts` 的 `queryDefaultSubscribeConfig`。每次审查这些文件与本地 `MoviePilotSeasonPicker.xaml(.cs)` / `MoviePilotMediaViewModel` 的控件、默认值、禁用条件和显隐规则一起对照；后端只证明请求契约。
- **网页端可见规则。** 没有明确入口季、也没有已有订阅时不预选任何季，主按钮是禁用的「请选择订阅季」；选中季后每季出现开关和普通订阅／分集洗版／全集洗版三档，已订阅季另有标记。新订阅的默认模式：整季已入库用全集洗版，其余用公开的 `DefaultTvSubscribeConfig`；未设置、不可读或旧服务器没有这个入口时回退普通订阅，不阻断季选择。
- **入库状态是提交门禁。** 入库状态还没回来或读取失败时，新选季只显示「入库状态未知」；用户必须亲手点过一次模式（包括已选中的那一档）才允许提交，并提供「重试入库状态」入口。已有同模式订阅不要求重新点。状态失败不能显示成「缺失」或「已全部入库」。
- **本地保留的差异。** WinUI 原生开关与单选替代网页端 VSwitch／VBtnToggle；剧集组草稿按组保留，网页端切组会清空。两处都不改变提交请求，属于外观和本地交互差异。状态色用 `EgDangerBrush` / `EgWarningBrush` / `EgAccentBrush` 表达缺失、部分入库、已入库。
- 本地读取入口是 `MoviePilotService.DefaultTvSubscribeModeAsync`，纯解析在 `MoviePilotSubscriptionPlan.DefaultMode`，按入库状态选默认在 `DefaultSeasonMode`。测试覆盖公开配置的三档、布尔／数字／字符串开关、旧入口 404 回退、已入库季默认全集洗版，以及状态失败时的显式模式门禁。

## 夹具与验证要覆盖

- **方法差异**：POST 成功、POST 405 才改 GET、POST 超时不得改发、PUT / DELETE 结果不明后锁定且不重发。
- **字段差量**：只改一个字段时的请求体、清空发 `null`、未改动的站点／下载器字段不出现。
- **统计语义**：电影零号条目、`library` 的字符串与数组两种形态、只有 `library`／只有 `download`／两者都有／两者都没有的状态判定、`subscribe:null`、空 `episodes`、范围外的集被过滤、未播出集被补齐、"统计不可用"不显示为全缺或全入库。
- **播放匹配**：跨服务器 `itemid` 不被直接信任、同名多候选、路径唯一命中、路径大小写、无权与不存在的条目。
- **播出日期**：空 `air_date`／缺集号／解析不出来的日期都不进表；非 TMDB 来源、非电视剧、季号缺失时**不发请求**（断言请求次数，不只看界面）；带 `episode_group` 的订阅查询要带这个参数。界面按「已播出给搜索键、未播出与无日期不给」两档断言。
- **界面**：进度写在封面内且不与更多按钮重叠、空统计不显示为全入库、离页与切服后旧按钮失效。
- `--probe-shell` 的三个订阅检查只操作假服务器与捕获的假播放动作；真实只读核对另行进行，且不写订阅、不起播放器。

## 媒体详情与按季订阅（v3.1.3）

2026-10-11 用 `dashboard/system` 确认实际服务为 v3.1.3，并核对同版本前后端源码。详情切组只读；季列表用 `media/seasons` 或 `media/group/seasons/{group}`，季号必须保留服务器值。非 TMDB 也按原来源查询季，不自行补季号；TMDB 集查询的首个参数必须是 `?episode_group=`，不能写成路径中的 `&episode_group=`。组内响应可能仍保留原始 season_number，显示与操作用外层所请求的组季号。

新增、取消和模式变更按来源、媒体 ID、类型、剧集组和季定位，取消/修改只用明确订阅 ID。网页端仅按季汇总的缺陷不能照抄。普通模式为 `best_version=0,best_version_full=0`，分集为 `1,0`，全集为 `1,1`；全集指该季目标范围整包。重复 POST 可能返回已有 ID 而不更新模式，新增后需回读核实。失败保留每季已完成结果，不重发整批；明确核对才能解除不明状态。

媒体季集入库状态来自只读 `POST mediaserver/exists_remote`，请求是媒体快照加当前组，错误显示未知，不能复用其他组统计。资源 `search/media` 不接受 episode/group；字幕 `search/subtitle/media` 接受 episode，但仍不接受 group。字幕是独立的扁对象，下载用 `POST download/subtitle` 的 `subtitle_in` 原样对象与 media_source/media_id/save_path；不向日志或系统启动参数输出签名链接与凭据。

新增验证覆盖三模式字段、组/季隔离、部分成功与回执核对、组和库状态迟到、字幕取消保存与站点变化撤销旧结果。完整背景与主题截图来自离线 Shell 探针，不能当真实下载或播放证据。
