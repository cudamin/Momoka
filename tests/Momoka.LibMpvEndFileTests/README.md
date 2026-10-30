# R048：libmpv REDIRECT 结束判定离线回归

独立运行 27 项，只链接本树真实 `LibMpvHandle` / `LibMpvLifetimeGate`、命令 marshal、ABI 声明、节点读取器与 `PlayerViewModel.NextEpisodeToAutoPlay` 方法。构建时的边界提取不唯一就失败；handle 方法正文逐字保留，仅将其命名空间隔离，原生端点改为托管假调用。实际 `ProcessEvent` 在真实 lifetime gate 下接收 marshal 内存，不匹配 `Classify` 字符串、不复制结束算法。

## 入口（PowerShell，在当前工作树根）

```powershell
$root = (Get-Location).Path
$sdk = 'C:\Users\89400\.dotnet\dotnet.exe'
$project = Join-Path $root 'tests/Momoka.LibMpvEndFileTests/Momoka.LibMpvEndFileTests.csproj'
& $sdk restore $project --source 'C:\Users\89400\.nuget\packages' --disable-parallel -m:1 -p:BuildInParallel=false -p:NuGetAudit=false
& $sdk build $project -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
# build 成功后才运行；不接受参数，未知参数退出 2，不回退全 Core/SelfCheck。
& $sdk run --project $project -c Release --no-build --no-restore
& $sdk format whitespace $project --verify-no-changes --no-restore
```

首次 restore 只用既有固定缓存，缺包应失败，不换源/版本；`NuGetAudit=false` 仅当次。项目自行构建当前 Core，无 Shell DLL、libmpv DLL 或其他树输出依赖。此项目不注册全套 SelfCheck，不占其用户级 mutex。

## 有效红绿

冻结同套 C#/csproj 后，修前 27 项：5 过、22 败、0 跳过、退出 1；修后同 27 项全过、0 跳过、退出 0。匿名假 HTTP 分别 12/10 次，不与测试数相加。生产/Core/测试均由本树当前 Release 构建，0 警告/0 错误；输入哈希一致。

## ABI 与覆盖边界

- [mpv 官方 v0.41.0 client.h](https://github.com/mpv-player/mpv/blob/v0.41.0/include/mpv/client.h#L1485-L1546) 与[仓库运行时记录对应的固定提交 270367e8af](https://github.com/mpv-player/mpv/blob/270367e8af/include/mpv/client.h#L1489-L1550)：`reason=5` 是播放列表展开，移除原列表条目并继续其内容；不是 EOF，也不是 HTTP 3xx。[同版本官方事件文档](https://github.com/mpv-player/mpv/blob/v0.41.0/DOCS/man/input.rst#L1718-L1799) 是同一语义。未运行原生载荷来核实其构建来源。
- 同时送官方 x64 32 字节 `end-file` payload 与现行 `LibMpvNative.MpvEventEndFile` marshal payload，核对真实读取的 reason/error 前缀。现行未使用尾字段 `PlaylistInsertId` 是 int，而官方是 int64；本题不改该声明，不宣称尾字段 ABI 已修正或使用插入 ID 跟随。
- REDIRECT 后 Exit/公开等待者 pending，位置不冒满、输出不 detach/clear；随后 start/file-loaded/position/restart 到最后实际 EOF，Stop/Quit/Error/Shutdown、无 REDIRECT 对照、连续/空 expansion、真实 HandOver/Swap 旧事件过滤、取消等待与一次性清理。
- 真实 `PlaybackService` / `EmbySessionScope` / `EmbyClient` 使用仅内存安装的匿名目标 Emby/Jellyfin 及不同默认来源；实际请求送假 handler。检查 redirect 不进入满进度/已看/释放，EOF 才结束并走实际 VM next 决策；timer 继续、error、来源撤销不借默认账号。Jellyfin 假 handler 接受现行协议 `UserPlayedItems`，不修改协议。
- 不启动 `Start`、事件线程、native、App/WinUI、服务器或任何本地/网络媒体；fake `loadfile` 只用于真实 Swap 状态转换，绝不读取其目标。保护器一旦被调用即失败，未登录/restore 账号、读写设置或 DPAPI；HTTP 仅 `.invalid` 假内存目标。
- 只执行 VM 的真实 next **决策**与结果消费等待边界；未执行完整 `StartPlaybackAsync`、UI dispatch/LeavePlayer、窗口或真正下一集加载。Emby/Jellyfin 是离线来源矩阵，不是实际 playlist 产品验收；FnNas/Plex 协议未动态覆盖。未知列表目标的凭据/来源安全前置没有产品级证据，本修不添加许可或跟随策略。

来源独立树 `C:/Users/89400/.codex/worktrees/r048-redirect-eof/Momoka` 的有效配对日志在 `artifacts/r048/red-paired-{build,tests}.log`、`green-paired-{build,tests}.log`；输入源码哈希见 `paired-fixture-hashes.log`。早期编译修整与 26/27 中间结果保留，不当最终成功证据。主整合需在其最终代码上定向重验；此树不发布/推送，桌面快捷方式仍旧版。

主树整合时项目已最小入方案，实际当树27项全部通过／0跳过，10次匿名假HTTP。日志 `artifacts/staged-repair/batch05/r048-integrated-*.log`，不把来源配对结果冒充主树。主树仍有题内R025在途产品改动，阶段单项提交按独立写集，不提前称整个批次交付。


## 2026-10-29 资源收尾与倍速待办回归

本项目保留上面的 27 项 REDIRECT/EOF 回归，另增加 10 项 `NativeResourcePerformanceTests`。
生成目标仍逐字编译本树整个 `LibMpvHandle`、`LibMpvLifetimeGate` 和实际命令 marshalling，只给
`mpv_wait_event` 增加受控的托管回调，不加载 DLL、不调用 `Start`/属性订阅、不播放媒体。

- 真实 `EventLoop` 放在独立托管 Thread，扣住假 wait-event，实际 `DisposeAsync` 三秒 Join 超时后返回；
  再放开线程，等待生产迟到回收任务，确认原生端点调用时线程已 `IsAlive=false`，context、临时配置副本和
  SVP 租约各释放一次，重复 Dispose 不重复释放。配置只用新建 TEMP 下匿名文件，测试结束逐个删除。
- 正常及时退出没有迟到待办，仍必须先终止线程再 Destroy。
- gate 挂起时 128 个 speed 共用一个完成源/worker、仅保留一个最新值；gate 放开后只写最终值一次。
  真实写入中追加再合并，取消等待者互不取消后继，最后值取消不写 native，入口捕获跨代不覆盖新待办，
  HandOver/Swap/Stop/Dispose 与异常重试均覆盖。非 speed 属性与相对命令不纳入合并，原契约保留。

当前 Release 编译与运行命令不变；合计 37 项，匿名 HTTP 的 10 次属于原既有 Service 夹具，不按断言累加。
这是线程/所有权/有界控制流验证，不证明真实 native 卡住的频率、驱动回收耗时、独占窗口或者实际 FPS。


## 2026-10-30 整段原生收尾等待上限

独立反证确认前置native gate/quit等待不受旧三秒Join限制。现在公开Stop/Dispose在锁外关闭新入场，整个公开等待三秒上限；只保留同一后台收尾任务继续等gate/quit/reader/destroy，不超时提前释放资源。长期阻塞时关闭结果及日志明确保留“迟到未完成”，正常及时完成仍实际等待。新增持有native写、quit卡住、Stop卡住三项真实全handle/gate反例；与原37项合计40，不把模拟卡顿当实机频率。公开Dispose任务和内部收尾任务分开，迟到测试等内部任务后才断言一次释放。
