# 第三批 Plex 已看接口与多选验收

本批 Plex 只复用电影、节目、季和单集的已看／未看单项接口。收藏和合集写入仍未接入；缺少单项协议与权限依据时不为多选新增路由。这里的结论来自固定官方客户端源码和匿名 HTTP 回归，没有真实 PMS、真实账号或真实写入结果。

## 一手源码

2026-11-11 直接读取官方 Plex Web **4.163.0 / 8246fd2** 的发布 JavaScript；该版本是固定依据，不是最新版结论。URL：`https://app.plex.tv/desktop/js/main-8792-0fadf59092c873ab638b-plex-4.163.0-8246fd2.js`。文件 3,508,034 字节，SHA256 `bdb3e4ce5d49b5eb6be48e11a4aea0f33a357561f8bf1e1303d31255f701cd97`。下面位置是原文件从零开始的字符索引。

| 实现位置 | 行为与本批边界 |
| --- | --- |
| ServerModel，字符 241008 | `isFullServer` 排除 cloud、mobile 和 embedded；`isFullOwnedServer` 还要求非 shared。拥有服务器和可更新个人已看状态是不同条件。 |
| 模块 9500，字符 538739–539765 | 本地库 provider 在 `isFullServer` 时添加 Scrobble action，key=`/:/scrobble`、reverseKey=`/:/unscrobble`；只有 Manage 和 download 使用 `isFullOwnedServer`。不能把 Emby 管理员权限作为已看门控。 |
| 模块 34041，字符 447511 | 库 identifier 为 `com.plexapp.plugins.library`。 |
| 模块 34314，字符 1701751 | 从 provider 的 Actions 中按 action id 取动作；对 Scrobble 没有另造账户管理员条件。 |
| 模块 69249，字符 1642300 | 已看显隐要求 Scrobble key，并排除 extra、collection、playlist 和 LiveTV；剧／季使用 leafCount/viewedLeafCount，电影／单集使用 viewCount/viewOffset。共享 `TracksUserState` 不能替代这条 Plex 边界。 |
| 模块 69249，字符 1640502 | 加入合集要求 provider 同时具备 Collection 和 Manage、条目有 librarySectionID，且不是不适用的额外内容／embedded。此条件仅证明官方客户端门控，不能据此为 Momoka 猜写入接口。 |
| 模块 61100，`Lh` 从字符 3132050 起 | 按 played 选 key 或 reverseKey，再 GET，query 使用 provider.identifier 与条目 ratingKey。成功后发单项 succeeded，失败发单项 failed。 |
| 模块 68615，字符 2009878 | 多选按所选 metadata 逐项发同一个 playedStatusUpdateRequested，并不引入另一条批量已看协议。Momoka 采用串行执行以便取消剩余和记录逐项结果。 |

同时直接读取官方 `plexinc/plex-for-kodi` 固定提交 `97191f248a20223bb93296789c010ff2204a9c76`：

- `lib/_included_packages/plexnet/video.py:95–103` 的 `markWatched`／`markUnwatched` 使用同一 scrobble/unscrobble 路由、ratingKey 和库 identifier。其 SHA256 为 `b2ea00590c6b01cf69b5c09aef729691556c0b31eefff855386d20e7c1f89f82`。
- `lib/_included_packages/plexnet/plexserver.py:169–182` 的 `query` 默认 `session.get`，状态失败抛异常。Kodi 把 token 放 URL 的实现不复制到 Momoka；Momoka 保持 X-Plex-Token 请求头。
- `lib/windows/preplay.py:197–200,231–238` 根据已看与进度选择用户状态动作，然后调用上面的单项方法。该调用没有 owner-only 门控；它不能证明当前 PMS 分享、权限或写入结果。

官方 PMS 服务端闭源。本轮未向 PMS 或 plex.tv 发送任何请求；不能把官方客户端调用证明扩大成真实服务器支持、账号权限确认或实际落库。

## 本地执行边界

`PlexClient.SetWatchedAsync` 已使用上面两个 GET 接口，`PlexMediaAdapter.SetWatchedAsync` 写成功返回 null，不伪造完整 UserData。必要补读经 `GetPlexUserDataAsync` 独立执行，补读 401 只重读。多选不可把全部写循环放进单个会话认证重试委托，否则后项 401 会重放前项成功写入。

批处理须固定来源和账号身份，Plex 只接受 Movie／Series／Season／Episode。收藏和合集在菜单与执行层都拒绝；不能拿个人评分、watchlist 或标签充当 Emby 收藏，也不借 `/Collections` 路由。取消剩余只在下一项前检查，当前已发 HTTP 保留自己的成功／失败确认。

## 运行验收

`ShellNavigationProbe.LibraryBatch.cs` 注册 `InspectLibraryBatch` 和 `library` 分区。它使用正式 LibraryPage、ItemsView、ItemContainer、确认表和结果 InfoBar，数据来自匿名假 HTTP。场景包括：

- Emby／Jellyfin／Plex 在 1100 与 760 DIP 下的实际选择容器、UIA SelectionItem 已选读数、选择计数和工具栏边界；选择期间隐藏播放与单项动作。
- 真实批菜单打开确认后经遮罩取消，验证零写；预取消 token 也零写。
- Emby／Jellyfin 合集权限拒绝与恢复，收藏保持可用；Plex 不露出未接入的收藏或合集。真实批菜单通过既有合集选择表加入三项，再从当前合集移出全部三项；空成员页仍显示三项成功结果，媒体详情和观看／收藏状态保留。
- 第 1 项成功、第 2 项在飞失败、取消后第 3 项未尝试，核对只发送前两项以及正式卡片／结果三态。
- Plex 写成功但 metadata 补读在飞时先确认卡片位；补读失败后成功位保留，三项各只写一次，结果继续反馈刷新失败。

图片与 `library-batch-receipt.json` 由父交付会话在最终整合树串行运行生成。独立探针开发树只编译与执行离线测试，不创建窗口；编译成功不算原生 UI、像素或确认交互通过。本树已看单项基线协议夹具是 fresh Release **300/300**、失败 0、跳过 0；最终多选与主树整合结果须另记本次实际读数。
