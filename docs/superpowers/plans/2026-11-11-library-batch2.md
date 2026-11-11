# 媒体库第二批：把媒体找得到

依据用户指定的 2026-11-10 路线图实施第二批。沿用正式 WinUI 页面，按已授权的直接实现偏好执行。当前基点 d649eba；本批不升级版本、不上传远端、不执行真实服务器写入或播放。

## 设计与全局约束

采用服务器独立的协议及条件模型，复用现有详情、人物作品和搜索结果页面。统一套用 Emby 条件会丢失 Plex 的 section 身份；增加独立全屏浏览器则重复现有分页和导航，因此不采用这两种方式。

- Plex 人物和题材只读取本地 PMS 的指定库，明确保留 section、媒体类型、字段和原生值。不使用 Plex 云人物页作为 PMS 接口，不用人名制造可导航身份。
- Plex 筛选与排序只展示服务器目录实际声明且本批支持的条件。多条件组合为普通并列条件，不提供嵌套编辑器；旧条件失效时不得默默扩大查询。
- Emby/Jellyfin 扩展人物、合集和播放列表搜索；人物不是容器，播放列表成员须按各自真实接口读取。Plex/飞牛不获得未经实现的搜索类型。
- 会话、取消、返回栈、分页、缓存和来源身份沿用当前生命周期；异步旧响应不能回填新页面。
- 每个子任务在独立工作树写入；最终整合、UI 验证、主树合并与桌面发布由主代理串行负责。先有失败回归，再实现并编译定向测试。无真实 PMS，结论保留离线协议证据边界。

## Task 1：Plex 人物与题材

读取官方 Web 4.163.0 / 8246fd2 原始产物，记录模块和字符位置。新增 Core/Plex/PlexBrowseRelation.cs，类型保存 SectionId、媒体类型、Field、Value，字段白名单 actor/director/writer/genre，拒绝空值、非法路径及跨 section 使用。EmbyItem/EmbyPerson 只增加运行期关系信息，PlexMediaAdapter.Models 映射 Role/Director/Writer 与 Genre，包括单对象和数组两种形状、string/number id、role 和 thumb。

演员、导演、编剧均可读；没有稳定 id 的人物可显示但无导航动作。有关系的卡片经 LibraryRequest/ItemQuery 带关系进入同库作品页，题材同理；详情旧按钮、离页和来源切换不能借当前其他账号执行。保留 Emby/Jellyfin/飞牛人物行为。

负责文件：PlexBrowseRelation、PlexMediaAdapter 人物 partial/Models、EmbyItem/ItemQuery 相关字段、ItemDetail.Cast、LibraryRequest、DetailViewModel/Page、Shell 人物导航及所需小范围 CardItem 修改。LibraryViewModel 仅关系查询分支，避免修改筛选分支。测试注册在现有 PlexMediaTests.Register 内，新增独立 partial；同步 PlexAffordance 受影响契约。

回归：同名异 id 不合并；跨库同 id 不串；三类字段不同；无 id 不请求；映射单对象/数组；关系查询保留 section/type/native id、分页头；非法关系不发 HTTP；人物头像遵循当前 HTTP 同源规则；旧来源不能导航。编译当前 Core/Tests 后跑 plex-protocol-fixture 与受影响 Shell 契约，提交中文正文及项目要求 trailer。

## Task 2：Plex 筛选、排序和字母跳转

新增独立 PlexLibraryCatalog/类型/条件/排序记录和 PlexFilterSelection，不复用 Emby ItemFilters。PlexClient/PlexMediaAdapter/EmbyClient 各用独立 partial 实现目录、可选值与 firstCharacter 读取，保留 Plex 分页请求头。按官方源码核实 includeDetails/includeAdvanced、Type/Filter/Sort 及回退目录的使用条件。

首轮支持目录提供的媒体类型、genre、year、unwatched/unwatchedLeaves、resolution 及其合法类型前缀。保留服务器原生值与显示标签，查询逐值编码；排序严格用已提供 key/descKey。首字母索引使用同一 section/type/filters 的 firstCharacter 返回顺序和 size 累加，不能沿用 Emby NameStartsWithOrGreater。

Shell 主体放 LibraryViewModel.Plex.cs，使用独立筛选行或面板接现有页面布局。目录失败可重试，空目录收起；设置按来源账号与库隔离，恢复时校验目录；变更条件重置页码及字母索引，旧响应拒绝回填。更新 BrowseSnapshotKeys、窗口查询复制等所有必要消费者。人物关系可与 typed query 兼容，不改其语义。

回归：不同库/类型目录隔离；目录缺失/失败/恢复；选项编码；组合条件；排序两方向；alpha size 累加及空索引；条件变化清失效缓存；旧来源响应丢弃；旧筛选失效提示；Emby/飞牛选择不污染。使用现有 PlexMediaTests 注册独立 partial；Core/Tests 与 Shell 编译后跑定向协议及实际 VM 夹具。记录固定来源、命令和结果并提交。

## Task 3：Emby/Jellyfin 扩展搜索及打开

LibrarySearchCategory/LibrarySearchGroups 追加 People、Collections、Playlists，既有枚举数值保持。普通媒体与新类型按服务器能力选择，搜索结果空组不显示，类型标签及近期搜索导航保留。

Emby 使用已核对的 Items 搜索。Jellyfin 人物使用 /Persons 并传 UserId；根据实际服务端源码处理旧版不支持 startIndex 的情况，分页不能重复。热门结果与分类列表保持稳定顺序和明确总数；不为了人物分页扫描全媒体库。合集导航现有容器，播放列表按官方成员接口保留顺序及分页；人物导航 ForPerson。新增条目不能被误当成可播放媒体。

负责 LibrarySearch/Groups、EmbyClient 搜索 partial、LibraryViewModel.Search 与搜索必要主文件分支、LibraryPage 搜索分类、容器读取与导航的最小修正、相关测试和文档。不改 PlexBrowseRelation 或筛选代码。

回归：两家人物/合集/列表 query；空词零请求；人物与媒体独立分页、边界和取消；播放列表成员按序、空列表及失败；结果按捕获来源打开；跨源同 id 不串；旧近期记录和已有分类数值兼容；纯 Plex/飞牛不展示无实现分类。编译当前 Core/Tests、Shell及必要契约项目，记录测试先失败再通过，提交对应更改。

## Task 4：整合、复核与交付

主代理逐项审阅提交并 cherry-pick 到当前树，解决共享类型/导航/VM 的交叉修改。补本批匿名原生探针：Plex 详情三类人员、关系入口和筛选/排序/alpha，Emby/Jellyfin 搜索分类及结果打开，覆盖宽窗和窄窗、失效恢复和零真实播放。每个改动探针当轮实际运行其路径及反例。

最终编译 Release Core/Tests/Shell，主测试全部通过；受影响独立契约、自动化句柄和差异检查通过。独立审查最终差异与来源边界。核对 master 与其他会话工作后合入主树，对最终整合代码重新执行必需测试和 UI 运行检查，发布实际桌面目录并核快捷方式。若目标程序有用户实例持锁，保留可审阅结果并说明具体阻塞，不擅自杀进程。未获上传授权，不 push。

## Review Focus

身份丢失、未经目录确认的条件、Jellyfin 旧版人物重复分页、播放列表被当作普通文件夹、旧异步读取覆盖新来源，是本批重点回归风险。各任务必须在实际生产调用链上覆盖；源码契约、假 HTTP、原生 UI、真实服务器四类证据分开报告。
