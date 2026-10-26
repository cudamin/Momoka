# R-035 MoviePilot 非默认剧集组入库定位回归

`MoviePilotEpisodeGroupPlaybackTests` 已在普通 `Program` 注册；显式入口只运行本项，不运行全 Core、SelfCheck、Shell 或用户 mutex：

```powershell
$tree = (Get-Location).Path
$sdk = 'C:\Users\89400\.dotnet\dotnet.exe'
$project = Join-Path $tree 'tests\Momoka.Tests\Momoka.Tests.csproj'
& $sdk restore $project --source 'C:\Users\89400\.nuget\packages' --disable-parallel -m:1 -p:BuildInParallel=false -p:NuGetAudit=false
& $sdk build $project -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
# 仅在当前编译成功后执行：
& $sdk run --project $project -c Release --no-build --no-restore -- moviepilot-episode-group-playback-fixture
```

必须从当前工作树根执行。首次 restore 只用既有固定缓存，缺包应失败，不改仓库源／版本；`NuGetAudit=false` 仅该次完全离线还原，不是长期安全设置。当前 Core 和测试均从本树源码编译，不借其他树 DLL 或 native。

## 行为与证据

- 非默认组只接受当前 Emby／Jellyfin 下主 Provider／媒体 ID／Series 类型已核候选中的唯一路径。MoviePilot 组 S01E01 可映射到原始 Emby S02E01／S02E07，返回字典键仍为组内 E01；`Item`、`Parent` 与 `Episodes` 保留匹配系列及原始季上下文。
- 对所有同主身份系列的全剧后代查路径，再判唯一；数字唯一但路径不符、跨季／跨系列同路径、多路径命中不同目标、无 `LibraryEntries`／空路径均无目标，不用文件 `itemid` 逃出主媒体核对。重复入库记录只命中一个条目时不额外制造歧义。
- 路径仅字符串比较，无文件／目录／网络盘 IO：Windows 驱动器与反斜线 UNC 可规范斜线并忽略大小写；Unix 保留大小写。支持 `Item.Path`、`MediaSources.Path` 和旧字符串 `library` 的真实解析。
- 默认组（缺失、空串、空白）独立保留默认季集数字唯一、合集区间、路径优先多候选及旧 itemid＋数字＋路径兜底。电影零号及电影候选语义不受剧集组影响。非默认组不扩张未知／非法 Provider 的兜底。
- 产品允许已有主 Provider 定位实现的 Emby／Jellyfin，拒绝尚无该实现的 FnNas／Plex；未入库、预取消不发请求；在 provider／后代／默认组 itemid 回话取消时传播取消并停止后续请求；后代权限拒绝不降级成文件目标。
- 每个 HTTP 场景调用实际 `MoviePilotLibraryPlayback.FindAsync`，使用共享 `StubTransport`、真实 `EmbyHttp`／`EmbyClient` 解析匿名 JSON，断言全部请求数量、精确 GET 端点／顺序、空正文、当前夹具用户／Detail 参数，以及最终目标三项；不是只测 `MatchesEpisode`。

本树基点 `7f18f128fdefb7e4a9f94728be68701914b92052` 首轮 50 项：17 过／33 败／0 跳过，实际假 HTTP 87 次。修后同 50 项全过／0 跳过，实际假 HTTP 77 次。日志在本树 `artifacts/r035/{restore,red-build,red-tests,green-build,green-tests}.log`，只记匿名夹具。

## 主复核补修回归（2026-10-24）

删除首轮“Jellyfin 零请求”错误断言，增加9项 Jellyfin实际 Core 回归，当前总数为 `50 - 1 + 9 = 58`；不把首轮50项与补修58项相加计覆盖。

- 真实 `FindByProviderAsync` 的两页 `GetItemsAsync`（200＋1）在本地排除同数字错 Provider／错主ID，Core再排除错媒体类型；随后只取两部同主身份系列后代，唯一路径选中第二部系列原始 S02E01，精确断言 `Item / Parent / Episodes`。
- Jellyfin 默认组保留数字唯一和默认季上下文；电影两页查主 Provider 后按电影零号与唯一路径选中，不发 Episodes。
- 同数字错路径、同主身份多系列同路径歧义、无 LibraryEntries、无主身份候选均不走 itemid；分页／后代回话取消传播，停止后续请求。
- 回归按当前本地实现生成匿名 JSON，使用无 `/emby/` 前缀的 `https://jellyfin.invalid/`，走现有默认 Jellyfin 12 route。断言每个 GET 的实际数量／顺序、`Items`／`Shows/.../Episodes`、UserId、过滤后 Detail 字段、分页偏移0／200、Limit=200及未发送 `AnyProviderIdEquals`；没有查询网络接口或接触真实账号／header。

补修红测（首轮错误产品门控）58项：49过／9败／0跳过／退出1／实际77次假 HTTP；补修后同58项全部通过／0跳过／退出0／实际97次，其中新增 Jellyfin9项实际20次请求。新增证据独立保存到本树 `artifacts/r035/jellyfin-correction/{red-build,red-tests,green-build,green-tests,format-core,format-tests,checks,status}.log`；首轮证据哈希另存 `original-evidence-hashes.log`，原日志未覆盖。当前 Release Core／Tests 均0警告／0错误，补修复用本树已成功固定缓存 restore，不改依赖。

## Scope 守卫及未覆盖边界

主复核纠正（2026-10-24）：首轮提交 `3c4ee85531dce915f4eed24f167d3b3499396dcc` 的 Emby-only 门控误禁已有 Jellyfin 能力。文档旧“Emby”文案不是删除能力的授权，首轮以此推导“局部排他前提”的说法不成立。当前源码依据是 `src/Momoka.Core/Emby/EmbyClient.MoviePilot.cs` 明确通过 `GetItemsAsync` 以200条分页、本地按 Provider／ID 精确过滤；`EmbyClient.cs` 的 `GetEpisodesAsync` 支持 Jellyfin；`EmbyClient.Protocol.cs` 依当前连接重写 Jellyfin 用户 route 与字段；`ShellPage.OpenSubscription` 捕获任意当前已签入 scope。因此补修只把门控纠正为允许 Emby／Jellyfin、拒绝尚无该查主 Provider 实现的 FnNas／Plex，不改协议／登录／auth header／共享能力／Shell。旧提交及日志保留，不把首轮50项通过当成 Jellyfin 能力正确的证据。

只读核对实际 `MoviePilotSubscriptionsViewModel.LoadPlaybackAsync`：请求前 `_playbackScope.IsCurrent`，经捕获 `scope.ExecuteAsync` 传递令牌，回填前检查 `IsCurrent(load)`／`Live`／`scope.IsCurrent`；行可执行条件含当前连接与 scope，`PlayEpisodeAsync` 检查行归属、生命周期令牌和当前 scope；`Cancel` 取消生命周期、清空 scope／回调并退役行。这些调用者未修改。

`EmbySessionScope.ExecuteAsync` 绑定捕获账号、核对仍可用并链接来源生命周期与调用者取消，在完成前再次检查取消。它不单独保证该账号仍是当前默认账号，也不认识页面加载代次；因此不能删除上面的 VM 当前 scope／离页守卫。本夹具直接测试 Core，不创建真实或模拟登录流程，不覆盖 Scope 401 重登、页面实际导航或播放后生命周期。

所有媒体、用户、地址和占位令牌仅内存匿名夹具，无真实账号／配置／凭据／DPAPI、认证链、服务器、native、App、播放、UI、发布或推送。未验证真实跨服务器路径映射／路径元数据完整性、Emby 超过现有 provider 查询100条上限的候选／其它 Jellyfin 版本 route 的运行行为、真实 WinUI 导航和后端播放；不声称产品运行通过。主线程须只整合题内文件并在当树重新编译定向测试，不复制本进度或旧输出。
