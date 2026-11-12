# 媒体库第五批：Emby/Jellyfin 普通视频播放列表

实现及源码核对日期：2026-11-12。服务器持久播放列表属于捕获的单个在线账号，和本次播放队列分别持有状态。首轮提供目录、创建、添加、移除成员和排序；没有智能清单、协作授权编辑或跨服务器列表。正式页面及主树交付的最终结果由第五批集成报告记录；本文件只报告协议分支的证据。

## 协议与授权

`EmbyClient.GetServerPlaylistsAsync` 按 `IncludeItemTypes=Playlist` 读取整个账号目录，原始分页索引按服务器返回项数前进。短页在已声明仍有总数时继续读；空缺页、重复列表编号和总数变化拒绝半份结果。`GetEditablePlaylistsAsync` 仅返回当前原生 `CanEditItems=true` 的项。

Emby 目录保留服务端 `CanEditItems`。影视选择器另用 `GetEditableVideoPlaylistsAsync`，仅明确 `MediaType=Video` 且可编辑的清单入选。每次写已有列表前，重新读取该账号的对应 Playlist DTO，要求 ID、类型、MediaType=Video及 `CanEditItems=true` 都匹配；普通账号或管理员都不能以旧条目授权覆盖本次读取。音频/未知种类或缺失授权保留成员只读，不靠管理员身份推断。

Jellyfin 每个列表先读当前 Playlist DTO 的 ID/Type/MediaType=Video，再经 `GET Playlists/{playlistId}/Users/{userId}` 单独核对当前用户的 `CanEdit` 和用户身份。固定 10.10.7 controller：拥有者返回其自身 `CanEdit=true`，其他用户按其 share 返回；管理员、合集管理和媒体删除权限均不能替代该授权。403/404 表示不能编辑；401 沿现有同账号重认证重读。目录浏览本身不需要编辑权，列表仍可打开；没有 share 的公开列表保留只读。客户端不提供 share 写接口。

| 行为 | Emby | Jellyfin |
| --- | --- | --- |
| 账号目录 | `Users/{userId}/Items`，Playlist/Recursive/SortName | 旧版同目录；12.x 经既有 dialect 转到 `Items?UserId=` |
| 成员读取 | 官方 Web `Users/{userId}/Items?ParentId=&SortBy=ListItemOrder` | `Playlists/{playlistId}/Items?UserId=&StartIndex=&Limit=` |
| 创建 | POST `Playlists?Name=&Ids=&UserId=&MediaType=Video` | POST `Playlists`，JSON `Name/Ids/UserId/MediaType=Video/Users=[]/IsPublic=false` |
| 添加 | POST `Playlists/{id}/Items`，JSON `Ids/UserId/SkipDuplicates` | POST `Playlists/{id}/Items?Ids=&UserId=`，无 Emby 去重字段 |
| 移除成员 | POST `Playlists/{id}/Items/Delete?EntryIds=` | DELETE `Playlists/{id}/Items?EntryIds=` |
| 排序 | POST `Playlists/{id}/Items/{entryId}/Move/{newIndex}` | 同结构的已核原生路由，0 基可见成员索引 |

空清单可创建。Jellyfin 10.10.7 `PlaylistManager.CreatePlaylist` 在没有可推断成员/种类时默认 Audio；本批显式 `MediaType=Video` 防止空影视列表变成音频列表。Jellyfin DTO 的 `IsPublic` 默认 true，客户端显式 false；不因“没有分享按钮”就依赖服务端默认值。Emby SDK 也声明可选 `mediaType`，空创建显式 Video。

成员身份为 `EmbyItem.PlaylistItemId`，和媒体 `Id` 分开。同一电影可出现两次，两次成员有不同 entry ID；移除一项及排序只传 entry，不能用媒体 ID 批量删掉两个位置。原始成员读取、排序校验和去重扫描保留成员顺序/重复项，成员缺页、重复页和总数变化拒绝继续写。排序发送前检查 entry 仍存在、目标在读回的可见成员范围内。

Jellyfin 没有原生 `SkipDuplicates`。勾选跳过重复时读取当前完整成员，过滤本次选择及该快照已存在的媒体；全部已在时不发 POST，也不伪造服务器写回执。这是客户端快照语义，另一客户端在读写间添加同一媒体仍可能产生重复。Emby 继续使用服务端 SkipDuplicates。此差异没有扩展到智能过滤或多用户协调。

## 写入与页面归属

四个写 API 在 `CancellationToken` 后接受可选 `Action confirmed` 与 `Action beforeWrite`。已有列表先读本次权限/必要成员，最后调用 `beforeWrite`，父服务检查原账号 owner、当前权限 revision 和页面/操作生命周期，防止读取期间同账号权限换代后继续发送。批量路径亦在每次认证尝试内捕获 revision 并在最后发送前检查。

播放列表专用 HTTP 路径在收到 2xx 头时立即 `confirmed`；创建的 ID 是附属回执，正文空、损坏、读取断线或后续取消不抹掉已确认成功。明确的认证 401 或其他 4xx 保留 HTTP 状态，401 才能按现有 Session 规则安全重试。已发送之后没有确认响应、超时、网络/正文传输失败或 5xx 抛 `PlaylistWriteUncertainException`，上层必须先独立读回再决定下一步，不自动重写。

来源退出后的 Session 发布守卫可能抛取消；捕获 `confirmed` 的调用者仍保留本次成功账本，UI 刷新仅是独立读操作。成功之后目录读取失败不能回滚成功、不能重新执行创建/添加/移除/排序。正文读取和回调异常使用类型日志，不记录凭据或服务器错误正文。

菜单单项加入限可播视频（电影、单集、视频、音乐视频、预告），不把本批扩展到音乐或容器展开。Jellyfin 批量加入首轮同样限视频；已有 Emby 批量容器范围保留其已实现行为。Plex 和飞牛不借用这组 REST 路由，Plex 清单由其独立原生接口处理。

## 固定源码证据

Emby 官方 SDK 固定提交 `f8499bd9d0e356b8a204a27b142765494dc6f450`，4.10.2.0：`Clients/Net.RestSharp/EmbyClient.Dotnet/Api/PlaylistServiceApi.cs`，SHA-256 `d96ac7eac5d2d7bacf64e8669950342e863be6de231eafbb9bbb70c7aa619d49`。读取创建 mediaType、EntryIds 移除及 Move 原生声明；只查合同，没有复制生成物或引入其依赖。GitHub tree API 当轮限流，直接固定 raw 文件读取成功，不用临时错误页作证据。

官方 Emby Web 4.10.1.0 已缓存产物，按字节偏移读可执行逻辑及 helper：

| 文件 | 关键调用/条件 | SHA-256 |
| --- | --- | --- |
| `modules/addtolist/addtolist.js` | `getItemsQuery` 加 Playlist、Recursive、CanEditItems=true | `acb6b86f815e76cbd140e6eacb6b1a826572577d2b9af959cefd5289441b272f` |
| `item/item.js` | byte 59314：playlist trackList.getItems 经当前用户 Items/ParentId/ListItemOrder；byte 99473：原始升序成员及非拒绝权限才 drag reorder | `3673cafe051980efaa4f08ff701d61a4c9ac238ddab51dd3fa4a3c13f1272328` |
| `modules/emby-apiclient/apiclient.js` | byte 100862：Move 读取 `items[0].PlaylistItemId`；byte 101593：Remove 以各 `PlaylistItemId` 拼 EntryIds；createList/addToList 独立请求 | `a73463518f555991606b769dda8051598644215a55cffdab35a7b960c622e667` |
| `modules/common/itemmanager/baseitemcontroller.js` | canEditInternal 处理显式 CanEditItems；createListHelper/addToListHelper 调原生 API 和去重提示 | `331ccf62538cc1faf144d2999bfe712ca254225bd4a80a5eecfdde27a2080262` |

Jellyfin Web v10.10.7 固定 HEAD `f4b8aa0ed4c5b571a3a6cc3bb027bb8ecebe5b68`，实读 `src/components/playlisteditor/playlisteditor.ts` 的 create/add/populatePlaylists/CanEdit、`src/scripts/playlistViewer.js` 的原生成员读取及授权驱动 dragHandle，及其调用的 SDK 生成接口形状。Server v10.10.7 固定 tag 的下列源码完整实读：

| 文件 | 核对点 | SHA-256 |
| --- | --- | --- |
| `Jellyfin.Api/Controllers/PlaylistsController.cs` | Create、GetPlaylistUser、Add、Remove、Move、GetPlaylistItems 的身份/权限/成员 ID | `074b545b0a2399cb3e1f2ec788317b0285bcf55d8a51284fa0780fbd1b22dfa5` |
| `Emby.Server.Implementations/Playlists/PlaylistManager.cs` | 空列表种类/单用户、Add/Remove/Move 实际链路 | `158178d86bdf578f44416ae387b50363936531b86acf32ccee8ee9a91944bb1d` |
| `MediaBrowser.Controller/Playlists/Playlist.cs` | GetManageableItems、公开/owner/share可见条件 | `fbf7da0933015b12bfbfe2fbc8310b907b6c0d2e9cce75b1f848aa7ea6cd3f3c` |
| `MediaBrowser.Controller/Entities/Folder.cs` | line 1564 GetLinkedChildrenInfos 按 LinkedChildren 原序 Select/Where | `87915316d5ba04bc9472493ab7bec059e94c02d368022c992742f01c69285039` |
| `Jellyfin.Api/Models/PlaylistDtos/CreatePlaylistDto.cs` | IsPublic默认true、Ids与Users字段 | `1f9da0b011235c9a6da880adf30e35254b5da9587dfc5fa9523ecf42260b6573` |
| `Emby.Server.Implementations/Dto/DtoService.cs` | line 802 `dto.MediaType=item.MediaType`，Playlist override返回已保存PlaylistMediaType | `3a76b9223ee2cc451d2a6e83b6a243b85f19eb9125798fe8628ff44e07757253` |

既有 Jellyfin 12.2.0 OpenAPI 快照也逐条核了上述 Playlists 路由、JSON 字段及 position 参数；本批追加到队尾省略 position，不依赖该新增参数，不把旧 user-prefixed 目录套到 12.x。12.x 主合同快照未变化，技能的原总表基准继续保留。

## Jellyfin 10.10.7 排序旧算法的限制

固定 10.10.7 `PlaylistManager.cs` lines 286–332：GetManageableItems 得到原 LinkedChildren 次序，筛可见成员；目标0用原首项作 prior，再算 `adjustedNewIndex = priorIndexOnAll + 1`；从原列表 Remove 被移动项后没有补偿，仍 Insert(adjustedNewIndex)。在所有项可见的 `[A,B,C]` 中，B→0 仍 `[A,B,C]`，C→0 变 `[A,C,B]`。这不是把 entry/media ID 混用，成员取回和插入索引的完整链路均已核。

对照 v10.11.1 同文件 lines 288–339，SHA-256 `fb3cc21fef89bd40c8c061c8eae1710664efcded79b49f06fa1295048c86091f`：newPrior 改为 `Math.Max(newIndex-1,0)`，增加 DetermineAdjustedIndex 对目标0处理；本轮再次固定下载 10.10.7 Manager 的 hash 与原读取完全一致。此源代码对照证明具体算法条件，不证明当前实际服务器的排序效果。

Momoka 按原生路由发送后独立读取实际顺序，不能只凭 2xx 就画成本地目标并宣称排序达到指定位置。旧服务器返回不同位置应保留真实读回、提示重新核对；没有用移除后重建清单绕过，避免改变 entry 身份或引入额外写入。

## 验证

当前 Core/Tests Release 编译 0 警告、0 错误后，协议79/79、`item-batch-fixture`72/72，0失败、0跳过。完整主套件4958项4957通过1失败0跳过，唯一失败是Jellyfin菜单完整词条的旧预期缺本批“加入播放列表…”；按已授权范围更新预期，并把现有47项菜单回归纳入`server-playlist-fixture`，重新编译后该专项126/126、0失败0跳过。最终完整主套件由父集成后再跑。产品轮11文本与文档/菜单轮9文本行尾/BOM/末尾换行/空白分别通过，5证据/技能文件本地链接均存在。新增专项直接执行本树真实Client/Http/SessionScope和批量服务，对`.invalid`内存传输检查请求、权限、身份、分页、entry、回执与未知写；不是另写一套模型测试。

追加种类核对：fresh Playlist DTO明确Video才允许已有清单写，影视选择器单独过滤。6项Audio/Unknown/null目标拒写及成员可读回归加入后，当前Release0警告0错误，`server-playlist-fixture`132/132、批处理72/72、0失败0跳过；一次测试构建的未读kind参数警告已删除参数后复编清零。此次读取没有猜新 query参数；DtoService完整源字段赋值已核。

没有读取真实账号凭据、建立测试账号、写入服务器清单或起播。未证明实时服务可用性、DPAPI、WinUI 布局/点击、实际播放链及 Plex 能力。完整主测试、正式 UI 宽窄截图、适用运行检查、最终主树合入及发布由父集成批次完成后另记；本子树 commit 和专项通过不算桌面交付。
