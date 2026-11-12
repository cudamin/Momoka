# 媒体库第五批：Plex 普通清单

2026-11-12，独立实现树 `library-batch5-plex`，基点 `a072a8b`。Core、探针及补充权限提交依次为 `3b185a2`、`1a8b997`、`a6decb1`、`fc7a4fb`，主控在 `37ba/Momoka` 按内容整合并继续修复。本文件区分固定上游、给定假响应的离线证据与主控待收尾的运行验证；子树不发布应用，真实 PMS 登录、读取、写入和播放均未执行。

## 读取的上游实现

固定官方 Plex Web **4.163.0 / 8246fd2** 发布产物：`https://app.plex.tv/desktop/js/main-8792-0fadf59092c873ab638b-plex-4.163.0-8246fd2.js`，3,508,034 字节，SHA256 `bdb3e4ce5d49b5eb6be48e11a4aea0f33a357561f8bf1e1303d31255f701cd97`。读取主树既有匿名存档并复核哈希；此版本是固定依据，不是最新版结论。位置是原 JS 从零开始的字符索引。

| 实现 | 源码位置和条件 |
| --- | --- |
| 视频播放列表目录 | 模块 53310，约 235000：`/playlists/all`、`type=15`、`playlistType`，`onlyDumb` 发 `smart=0`；模块 19093，1927664 同样排除智能清单。 |
| 合集目录 | 模块 85759，1927012：Content feature key 加 `/{section}/collections`，`subtype` 来自元数据种类转数值，`smart=0`。电影 1、剧集 2 分别由模块 38176 / 31070 证实。 |
| 原生能力与管理权限 | 模块 34094 / 92959，210863 起：`/media/providers` 返回 `MediaProvider`、`Feature`，字段包括 `type`、`key`、`requires`。模块 46144 按 feature type 找能力；动作判断约 1640390 / 1640470：播放列表须有 playlist feature 和满足账户要求，合集须 collection + manage，并且媒体条目有 librarySectionID。模块 32642，572920：空 requires 允许，`nonAnonymousAccount` 要求非匿名账号；模块 22701，452360 声明该字面量，其他要求查账号 services 和订阅。 |
| 播放列表可编辑 | 动作判断约 1643013 / 2904555：isPlaylist 按 `!readOnly` 判编辑，普通媒体按 manage feature；smart 清单改规则不在本轮范围。 |
| 成员身份及顺序 | 模块 24563，203993：播放列表成员 `playlistItemID` 独立于媒体 `ratingKey`。模块 53310，233856：移除 `DELETE /playlists/{id}/items/{playlistItemID}`；移动 `PUT .../{playlistItemID}/move?after={previousEntry}`，无 after 移首。新式页面约 3314961 / 3315293 同样使用成员 ID。 |
| 创建及添加 | 模块 19518，309362：新建 POST、已有清单添加 PUT；模块 61100，3168180：播放列表 type=video，合集 type=1/2、sectionId，smart=0，uri 来自选中媒体。 |
| 媒体 URI | 模块 17095，299643：同来源的多个 ratingKey 组成 `/library/metadata/{ids}`；模块 52336 / 29943 生成 `server://{machineIdentifier}/com.plexapp.plugins.library` 前缀。本客户端只发送当前服务器 URI。 |
| 合集成员 | 元数据 `key` 由 `extractChildrenKey`（200880 起）保留 `childrenKey`；合集类型路径约 3117082 为 `/library/collections/{ratingKey}`。读取该合集 children，移除由 3169190 的 `containerKey + /{ratingKey}`，不调用媒体删除。 |

完整产物还由 REA 作惰性 AST 分析，未运行脚本、浏览器或 PMS。静态图达到所选 8,000 节点 / 3,000 unknown 上限且父图结果预算受限，不能说完整关系图已核对；实际方法判断使用完整源码的对应模块和引用链。导出证据在本树 ignored `work/plex-container-evidence/rea-bundle.json`，主记录 `ev_bf28ac5dcf6794c5ecbfc69576be9668577e14bb102b1595e355350a601e347e`。官方 developer 页面网络尝试遇 TLS EOF，无额外接口证据来自该失败请求。

## 本地边界与取舍

- 普通视频播放列表和电影／电视剧合集分别读取。音乐仍使用已有统一音乐界面，`EmbyItem.PlexPlaylistType` 留原生媒体家族，视频清单由主控页面接入。
- `PlexContainer` 持有清单种类、所属媒体库、成员类型以及 CanEdit / CanOrder。播放列表只在明确 `smart=false`、`readOnly=false` 及 playlist feature 无未知账户要求时允许写；空要求允许，官方 `nonAnonymousAccount` 额外通过固定 `GET https://plex.tv/api/v2/user` 核对严格数值 id 等于连接 AccountId，且明确 `anonymous=false`。AccountId 来自登录成功的云账户 id，恢复路径只保存它，不能仅凭非空 id 推断非匿名。其他订阅要求没有账户服务证据则隐藏。缺字段仍可读，但保持只读。上游 `!readOnly` 对缺值较宽松，本地有意使用明确授权，未把缺省解释成服务端拒绝。
- 合集要求 collection 和 manage feature，创建／添加重新 GET 每个媒体的元数据确认同库、同 movie/show 类型，不创建跨库持久清单。共享账号与拥有者不靠名字或默认管理员猜；能力来自当前请求的 provider，写前再读能力和清单。
- 正式 `ServerListsService` 在目录和容器读取结束后复读当前权限，并把旧容器 CanEdit／CanOrder 与当前能力相交。账号权限查询在末次读取中降级时，相应编辑操作收起；能力恢复不能把原来 readOnly 的清单反向授权。页面持有单一来源和权限代次，写前 beforeWrite 再核同来源、同代次；权限缓存使用该在线身份所有者的弱表，不跨退出后新身份复用。
- 每次移除／排序先核对对应成员仍存在。重复媒体保留不同 playlistItemID，不按影片 ID 去重或删除文件。当前普通合集支持创建、添加、移除；CanOrder 固定 false，手工重排合集及其 custom sort 设置不纳入首轮。每次写入另提供发送前 beforeWrite 回调，页面所属来源／权限代次在全部异步预读后再次守卫。
- 写前 `RequireMemberAsync` 校验另加重复页和声明总数守卫：即使未知总数下服务器忽略起点并重复满页，第二个相同成员页也会停止并报错；已确认的声明总数变化或消失同样拒绝。该错误发生在关系写入之前，不调用 confirmed，也不发送 POST／PUT／DELETE。
- 新建要求至少一个初始媒体条目，沿官方媒体选择创建流程；没有空 URI 或空清单创建的可执行依据，不猜服务器接受它。播放列表允许来自当前服务器的不同库；合集明确限制单库。
- 目录分页返回 Consumed 与 HasMore：按原始服务器条目推进，包括本地排除的智能条目；成员分页按返回成员数推进。DeclaredTotal 单独保留服务器有效非负整数 totalSize，缺总数时只以满页继续／短页结束，不把 `TotalRecordCount` 的本地累计回退值当作已知总数。
- 主控追加了已知总数下的空页守卫：读取位置尚未达到声明总数，却收到空目录页或空成员页时抛读取错误，不能把丢失响应当作“没有清单／成员”。目录 PageIdentity 由当前原始页的 ratingKey 顺序构成，包含本地过滤的条目；正式服务拒绝重复目录页或重复 `媒体ID:成员ID` 页，并在已确认总数之后拒绝跨页总数变化或消失。后页失败会丢弃本次读取结果，保留上一完整界面及恢复入口，不把部分清单称为完整读取。
- 写入统一走 Plex 原生 POST / PUT / DELETE。HTTP 2xx 响应头是确认点，confirmed 回调当场记录；可选响应正文损坏、断流或后续取消保留确认。创建结果缺 ID 时 `PlexContainerCreationResult.Container=null`，显示已创建后独立刷新，不重发创建。
- 请求发送后失去 HTTP 回执、取消、无法跟随的重定向，或写请求收到 5xx，均抛 `PlexWriteUncertainException`。5xx 只能证明服务器返回错误，不能证明关系写入尚未执行；本地保留未知状态，要求先独立刷新确认，不能自动重发。401/403 等确定拒绝保留实际状态。后续刷新不得复用写委托，scope 末尾撤销不能清除已记录的 confirmed 位；未知写回执和确认成功后的刷新失败都会暂停相关编辑，刷新完成后才恢复。
- 账号权限查询由专用固定 authority/path 方法发起，不允许调用者传入地址。令牌只放请求头，跨来源重定向拒绝；账号 profile 形状不明、id 不符或暂不可达时 `VerificationError` 解释未确认原因，相关清单保持只读但 PMS 浏览仍可继续。PMS provider 本身的读取失败不被降级掩盖，account 401 沿既有同身份重新认证处理。

## 验证

子树在 `fc7a4fb` 对应代码上完成 Release Core 与测试项目编译，0 警告、0 错误；`plex-container-fixture` **20/20**、既有 `plex-protocol-fixture` **320/320**，0 失败、0 跳过。覆盖独立目录、分页、数值字符串、缺权限、重复成员、成员移除／after 移动、写前权限复读和最终代次守卫、跨库拒绝、同服务器 URI、占位头部令牌、预取消与身份撤销、明确拒绝、未知写回执、2xx 后正文失败和跨来源写重定向，以及 nonAnonymousAccount 当前账户 profile 核对、缺／错匿名形状、异账户和读取失败的只读回退。该组也注册进既有 PlexProtocolTests。这组数字是子树当时的运行结果，不替代随后父树代码的复编复验。

本次复核直接读取父树最终 `PlexHttp`、`PlexClient.Containers`、`PlexContainers`、`ServerListsService`、VM 和探针。父树新增了 5xx 未知回执、DeclaredTotal／PageIdentity、声明未完却空页，以及末次权限相交；对应测试源码新增了 503 结果未知和缺失成员页反例。这些父侧函数的存在不证明本次测试已运行，最终结果由主控记录。

复核发现写前查找成员的内部循环尚无重复页保护，另获主控授权作最小修复。为使用父树已经新增的 DeclaredTotal，而不覆盖其他在途代码，在本树 ignored `work/plex-member-guard` 复制父 Core 并仅注入最终 `RequireMemberAsync`，434 个输入文件哈希保存在 `source-pins.json`；链接本树实际 PlexContainerTests 和 TestHarness。当前 Release 编译 **0 警告、0 错误**，其中 **22/22** 离线用例通过，0 失败、0 跳过；新增两个反例要求缺总数重复满页／声明总数变化最多读取两成员页，且关系写入为 0。这组是父 Core 快照加单函数修复的隔离测试，不是父最终整方案、Plex完整兼容组或真实页面验收。主控应用该函数和测试差异后须当前复编、重跑本批测试；子树未改父工作区，也未运行桌面或全套测试。

匿名内存 HTTP 只证明本客户端请求和给定响应的处理；不证明真实 PMS 支持、权限、实际写入或 WinUI 页面。父代理须整合页面后重新编译、测试和运行截图，完成第五批交付；子树不更新共同发布目录、不改快捷方式、不推远端。

追加独立原生探针 `ShellNavigationProbe.PlexLists.cs` / `PlexServerListsAsync`：使用真实 ServerListsPage / Service / ViewModel、1280／760／360 宽，内存 handler 拒绝账号交换、媒体流及清单关系以外的写入；仅本夹具额外允许固定 `https://plex.tv/api/v2/user` 到假响应，仍不发送真实网络。覆盖普通视频目录排除 smart/audio、两影视库、重复成员及原生移首、只读隐藏和恢复、初始媒体创建经 PrimaryButton、添加表 Hide 取消、单库电影／电视剧合集目录、合集媒体搜索与排序隐藏。合集搜索复用实际共享搜索路由，并按结果 PlexSectionId 核对当前库；不把构造出的 section/all 查询猜作运行请求。

子树检查源码边界和控件名字，并用本机 WinUI 2.3.9 引用和父模型占位进行探针类型编译，0 警告、0 错误；这不是完整 Shell 或实际页面运行。最终 Shell 编译、父树追加测试和三宽原生探针由主控串行执行，结果以当轮日志和截图为准。截图、假HTTP操作或固定源码均不能推出真实 PMS 编辑权限、成功写入、实际播放或观看记录上报；本轮没有新增真实服务验证授权。
