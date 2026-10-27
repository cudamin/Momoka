# R046 实际 VM 自然接续 caller：无窗口回归

独立控制台 fixture，已最小注册到方案构建，仍需单独运行；不加入共享 Program。来源 fixture-only 提交 `58ec7779407cd2416eed9dc50ad312f1f84f67b0`，基点 `82176c7d57c15ddef2a312af2a6232ecb8c8897b`；本机工具核日期 2026-10-24，Asia/Shanghai（UTC+08:00）。主树仅整合测试文件，不拷来源的未提交产品快照。

## 真实与 fake 的边界

- SDK 10.0.400 自带 Roslyn 按完整语法节点逐字提取当前树 `PlayerViewModel.Transport.StartPlaybackAsync`（含全部分支、递归和 finally）、`NextEpisodeToAutoPlay`、Play 包装、Stop、解析单集/父剧/兄弟列表、Enter/Leave，以及完整 `OnNowPlayingChanged` 和原所有权字段。没有手写新状态机，没有以 next 纯 decision 代替真实 caller。
- 实际 Core DLL 提供 `PlaybackContinuation`、`PlaybackResult/Ticket`、`StartIntent`、`EpisodeNavigation`、DTO、实际 `EmbySession.Capture/ExecuteAsync` 与匿名缓存 scope。lease 通过真实内部二参构造函数 `(Func<Task>, Task contextClosed)` 创建，取消通过真实内部 `CancelAsync` 反射调用；release 是计数托管回执。
- `FakePlayback.PlayAsync` 只保留与当前 Core 对齐的六参调用边界：ticket、scope、VM lifetime、preparation、独立 continuation token、expected lease；Validate/Stop/Play 的返回和 admission 由测试输入控制。**它不是实际 PlaybackService、PlayOperation、handle 或 native lease 的实现。** `Consumed` 是显式 fake 回执输入，绝非手抄 service/native 状态机；旧 Dispose 的 caller 等待/令牌传递可验证，实际 service gate 争用和原生换手仍归主线程。
- 元数据走实际 Scope/Client 的只读 GET 调用，但 `HttpMessageHandler` 只允许匿名 `.invalid` 的逐例内存路由；不使用网络传输，不创建真实 HTTP backend、不登录/restore，不读取配置、DPAPI 或凭据。无 SettingsStore，保护器任何调用直接失败；配置只用新建内存对象。图片/着色器/音量写回/WinUI 展示端点 inert；原 VM 的 hold、idle、item/source/reference 判断不替换。
- `ManagedQueue` 是纯托管同步上下文，不是 UI dispatcher、App、WinUI 窗口或桌面输入。无 libmpv/native/media、文件播放、后台真实服务或用户 mutex。

## 可执行覆盖（本次 50 个动态案例）

| 范围 | 断言 |
| --- | --- |
| 自然末尾/不接续 | 末集、兄弟空、当前不在列表、下一季、auto 关闭/EOF 前关闭、非 Episode、UserQuit/Stopped/Error：真实结果 lease 恰一次 release，B 不启动 |
| 异常 | 最终 Noticed、RefreshRequested、自动下一集 Noticed、following Capture、release、Leave 回调异常；B 的 Refresh/Noticed/release 异常递归传播：每层 lease/finally 仍收尾 |
| 后继拒绝 | Validate 失败、不可 play、noSources、metadata 抛错、Series 无单集：B 不进入 Play；A release 完成 |
| 取消/Q | 原 scope 在 Capture 前/metadata 中撤销；lease 在 Refresh/下一集提示/metadata 中取消；准备在封面/metadata 中取消；旧 intent、Stop、VM lifetime：不启动 B，无遗留 hold |
| contextClosed fence | Q 的 closed Task 已完成但 lease cancel 回调尚未执行，真实 VM 仍传 expected lease/独立 token；fake admission 明确拒绝。**不据此声称验证实际 Core fence** |
| 正常继任 | fake 接受 B 后标记旧租约已消费，B 挂起期间显式 old Dispose 立即完成且恰一次；不等待 B 结束、不取消 B、无 Leave。B 结束后实际递归收尾 |
| 顺序/所有权 | C/B/A 依次 release，回执 hold=3/2/1、idle 均 false；最外层 hold=0 才 Leave，Leave 时 idle 仍 false，随后 idle 完成。旧 A 回来时 B 仍播放、hold=1、idle pending、B preparation owner/令牌保留 |
| 原行为 | following/siblings 在 Refresh 前捕获；默认源改 C 不挪走绑定 B，同 ID 跨源不混用；pending adoption 用 ReferenceEquals，同 ID 列表定位用 IdentityKey；换片预停失败只恢复原 DTO 引用；series 解析和多递归 finally 保持原路径 |

Play 的现有异常包装在内部 finally Leave 后仍可能再次 Leave；本 fixture 单独钉住既有两次调用，不在测试边界偷偷修成一次。它不是本题产品修复请求。lease release 抛错时 idle 仍在外层 finally 完成，但 cleanup 成功没有被声称。

## 来源与 fail closed

`SourcePins.json` 记录 11 份主线程未提交快照和 8 份既有辅助源的当前 SHA256；首次 `artifacts/r046-caller/product-snapshots.json` 只是首次快照，不是更新后的事实。**来源验证树的 11 份同步源仍未提交，未暂存、commit、reset、restore 或 checkout，不是主树待合入产品。** 部分只是来源记录，并非本 fixture 执行对象。

构建检查源 hash、C#/XML 语法、namespace/class/member 唯一、完整 Start 签名、唯一 await using、独立取消与 expected lease 传递；每个完整节点保存 offset/length/hash，并确认原文本逐字一致。运行再次核当前源/完整成员/fixture pins，并绑定构建时本树，记录实际 fixture/Core DLL hash。hash 漂移停止，不自动接受新 pins。

本次完整方法（含节点 trivia）SHA256：

- StartPlaybackAsync：`54f3e23d43c611097c39729aaf23b010343b758cd94dc01f6d679457e820dc1b`（11775 字符）
- NextEpisodeToAutoPlay：`16ed18c92208e6b76b8cd3636928c0364e77e1be8ad16e005be54da4da92a9d4`
- OnNowPlayingChanged：`ea305d65872740166bba76fff388897839bb6b10fc9da9c0212594ac9d000053`
- LeavePlayer：`d3a728840f37a46a7064841743cef9ba12d268c9271e26c9bfcb0fcf0031c617`

`Test-SourceExtraction.ps1` 在本树 artifact 下写独立 readonly 输入，不碰产品。缺 Start、重复 Start/Next、非法语法、错 namespace、缺 await using、hash 漂移、外树入口共 **8 个反例均预期 build 退出 1**；之后必须重编译 untouched 当前源，再 no-build 跑实际 caller，不能运行失败提取后的旧产物。

## 复跑

```powershell
$root = (Get-Location).Path
& (Join-Path $root 'tests/Momoka.NaturalContinuationCallerTests/Run-Caller.ps1')
& (Join-Path $root 'tests/Momoka.NaturalContinuationCallerTests/Test-SourceExtraction.ps1')
```

脚本按自身目录确定本树。只读取固定本机包缓存中的现有 3 份 analyzer nupkg，复制到本树 offline feed，缺包停止；所有 CLI home/temp/packages/http cache/logs 留本树。仅当次 `NuGetAudit=false`、`DOTNET_GENERATE_ASPNET_CERTIFICATE=false`；Release `--no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false`。当前 compile 零警告零错误才运行 `--no-build --no-restore`。不升级或新增测试框架包。

后续主线程同步新源时，先逐份核对再定点更新本目录 pins，重跑相关验证；不得改产品来适配 fixture。

## 本树证据与保留的失败

- `artifacts/r046-caller/logs/build-01/02/03.log`：各退出 1、0 警告/1 错误，分别是本 fixture 的 root 等值拒绝、误把 XML 快照当 C#、误叫 PlayReplacingAsync 为 ReplaceAsync；仅修新提取器。
- `build-04.log`：退出 0、2 警告/0 错误，来自未抽取相关 writer 的 inert 字段；在新 boundary 显式 null 初始化/读取，不压制警告，**未运行 no-build**。`build-05/06/07.log` 均 0 警告/0 错误。
- `run-01/results.json`：46 项、45 过/1 败/0 skip/退出 1；fixture 将 Refresh 撤销 lifetime 后的真实递归取消误当正常返回。修正测试预期，未改产品。`run-02` 46 全过，`run-03` 47 全过。
- `verify-01/dynamic/results.json`：补嵌套异常后 50 全过；`extractor-01/counterexamples.json`：8/8 预期拒绝；`extractor-01/current-reverified`：当前源重新 Release 编译零警告错误，50/0/0/退出 0。
- `logs/format-caller-01.log`：第一次 format 未给 Release，缺 Debug extractor 导致加载错误，并发现 3 新 C# 空白问题；`format-caller-02.log` 用 Release 正常加载但仍有空白差异。绝对 include 的 `format-caller-fix.log` 实际选择 0，**不作为格式通过证据**；相对精确 include 的 `format-caller-fix-02.log` 只修 3 新文件。最终 `format-caller-final.log` 0 修改/退出 0/无加载诊断；extractor 格式也 0 修改/退出 0。
- 两个 PS 经现有 script checker 检查 UTF-8 BOM/LF/末尾 LF/语法；本目录其余文本 UTF-8 LF、XML/JSON、source hashes、链接与限定 diff 另验，未放宽基线。

未运行 full Core、SelfCheck、R001/R003/R005/R031、x64 Shell 完整构建、App/WinUI/UI 桌面、真实配置/凭据/DPAPI/服务器/HTTP/network/native/media/playback、发布、升级或 push；快捷方式仍旧版。这是并行 caller 测试交回，不是主线程真实 Service/native 并发覆盖或产品完整交付。

## 主树限定整合证据

19份pins中6份主线程native/所有权与测试边界后续修正已逐份复核显式更新，另候选回退最终Service修改再定点更新1份；实际Start/Next/NowPlaying/Leave节点保持来源逐字一致。主树当前50动态全过／0失败／0跳过／退出0，8提取非法反例各预期退出1、随后默认当前源重编译再跑50绿。来源基点夹具编译错误不搬主树当本轮证据。主树证据 `artifacts/r046-caller/verify-20261024-071614-3335`、`extractor-20261024-071811-2846/current-reverified` 与 `artifacts/staged-repair/batch05/r046/`。当前真实Service/native seam27、完整Core2549及R051/R044/R048分别重验，FakePlayback50不替代它们。

方案已显式映射两个提取器的Release配置，并将caller提取器置独立solution子文件夹，避免formatter把两个同名SourceExtractor视为冲突；原冲突日志保留。最终方案空白0修改／退出0无加载诊断，实际WinUI/窗口/native媒体仍未运行，快捷方式旧版，非完整交付。

## 2026-10-24：R003合法源码变化的单项pin同步

第七组R003修改 `EmbySession.RestoreAttemptAsync` 的最终身份守卫及 `ResolveRestoreConnectionAsync` 对空public ID的保留策略；自然caller只执行实际Capture／ExecuteAsync与匿名缓存scope，不执行恢复／登录／凭据保存。完整Start/Next/NowPlaying/Leave和其余18份pin字节未变，因此逐份复核后**仅更新EmbySession这一个SHA256**，不整份生成新快照、不删除来源核对、不改历史base/date元数据。

首最终方案 `artifacts/staged-repair/batch07/final-current-95d2eab/solution-build.log` 因旧pin失败退出1，随后未运行旧测试。单项同步后当前caller Release零警告错误/50全绿；原提取脚本8个错误源/成员/语法/根目录/缺await-using/hash负例全部按预期退出1，再重编原current/50全绿，证据 `artifacts/staged-repair/batch07/r046-source-pin-sync/`（reviewed-pin-delta/only-one-pin-check/source-guard-positive-negative）和其指向本树新反例目录。不是播放或真实登录验收；本轮不改产品来适配fixture，最终统一方案与51入口另跑。

## 2026-10-25：纯合并基线的旧测试兼容
本轮验证树为 `C:/Users/89400/.codex/worktrees/merged-contract-tests/Momoka`，基点
`4afca26672297aedd7725d675706d7f2b923519c`，产品只来自父树
`mp-translation-interpolation/Momoka` 已 resolved/staged 的 `48c43fc20d5827fe5f5f147eb73e1d1ca7aa2795`
合并差异。导入 patch SHA256：`7ef4a4592f66b219639aa0ca499427da971443a34e9b8fe0f894cc36cffcd680`。
本树 629 份产品源的 Git blob 与父 index 逐份一致；未导入父未暂存的 translation/AppSettings 改动。
产品快照不暂存、不提交，不改共享 Program/Agreements/方案；最终功能再验由父线程完成。

测试 VM 使用本树实际重新编译的标准 x64 Release Shell（不是复制父 DLL）：
`src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.dll`，
2026-10-25 01:36:41 +08:00，4,743,168 字节，SHA256
`b9125a7ff07a46e236603200c97dd0608fd548a84ba588d39431d99fbb5e67f5`。
父首次合并 DLL 只作来源对照：2026-10-25 00:34:42 +08:00，SHA256
`735c3295886cb0dce9c8e4b92aa265739290959e0c8ce5befca5f9dcd1f97f77`；没有将它称为本树输出。
所有日志/输入清单留在本树 ignored `artifacts/verification/merged-contract-tests/`。

19 份 pins 的历史源全部与 `48c43fc` 的实际 blob/hash 逐份吻合。本轮只更新下列 **7** 项，
其余 **12** 项逐份确认字节不变，保留旧 baseCommit/date（提取器仍严格核它），不整份重生成 pins。
逐份完整 diff 存 `artifacts/verification/merged-contract-tests/review-pin-*.diff` 与 `pin-review.json`。

| 更新源 | 本次审查的实际变化及覆盖界限 |
| --- | --- |
| IPlaybackBackend.cs | 只改宿主通信的注释口径：弹幕通道适用于两条内置管线；所有接口/所有权契约未改。 |
| LibMpvBackend.cs | ClientMessage 在弹幕脚本存在时保留，并 load-script 弹幕；caller不初始化实际 native。完整生命周期/handle 提取原样，另由 ShaderCommandGeneration 37 项核控制/事务边界。 |
| PlaybackService.Lifecycle.cs | 主干 `ff31c732ff13876551119011c7270a2ccb3551cd` 按 2026-10-22 用户令取消自动换版，只执行所选 ticket；不是借新hash恢复旧 candidate-loop。自然 native专项的一条旧第二候选预期已作为实际兼容失败单列。 |
| PlayerViewModel.Transport.cs | 完整 Start 中起播失败改为实际 `MediaVersionSwitch.UnplayableNotice`；await using、intent、取消、expected lease、递归与finally不删减。 |
| PlayerViewModel.cs | 增加 DandanPlayClient、DanmakuChanged订退和请求取消；本 caller不构造完整产品VM，提取的原所有权字段仍原样。 |
| PlayerViewModel.Events.cs | 宿主弹幕握手/菜单、完整 NowPlaying 新 ResetDanmaku；跳过改只读 Emby MarkerType，mpv仅刷新章节刻度不改跳过计划。只接受两处实际旁支端点补齐，不删除完整 NowPlaying 或弱化 source guard。 |
| EmbyItem.cs | 新 ChapterInfo.MarkerType，以及 x264/x265 标签只读呈现；不改来源 identity/id、自动接集/lease规则。 |

特别核对 ActivityToken：PageViewModel 相对 `48c43fc` 字节未变，合并中保留旧修复的活动期/取消语义，
SettingsPage与VM依然依真实活动票拒绝迟到结果；没有将新默认票/假模型写入 pins 来掩盖寿命变化。
PlaybackContracts/Continuation/Service.Continuation、ShaderCommand四个边界源、EmbySession/Scope、
EpisodeNavigation、StartIntent和Versions全是本轮未变的 12 项，不借改变来源放宽它们。

完整节点当前 hash：StartPlaybackAsync `7342e327ec6dd358c1066567456b9cb8b5b648ce5a5b7969d8b218e4555eba8b`
（12108 字符）；OnNowPlayingChanged `23e462851607aac5e1665a31c83df34600cd928a93b4a582c42bfb4f5daedc47`
（6434 字符）；NextEpisodeToAutoPlay 与 LeavePlayer hash 未变（见原清单）。
初次更新 pins 后真实构建依然以 0 警告/2 错误拒绝旧 PlayerBoundary 的 ResetDanmaku/RefineChapterMarksAsync
缺口，留 `build-natural-reviewed-pins.log` 与 `build-solution-01.log`，没有运行失败构建的旧 caller。
端点写集获准补齐后必须重编当前完整 source 再跑原 50 项及 8 个非法提取反例；未完成前不报通过。

父之后的字幕功能新增只在最终 feature commit 重新审查来源/重编/复跑，不能把本轮纯合并证据冒充新功能。

## 2026-10-25：第二轮纯合并基线测试兼容
本轮仍在 `C:/Users/89400/.codex/worktrees/merged-contract-tests/Momoka`，产品固定为
`4afca26672297aedd7725d675706d7f2b923519c`＋父 resolved/staged 的
`48c43fc20d5827fe5f5f147eb73e1d1ca7aa2795` 合并快照。629 份产品源均与上一轮清单一致；
未导入父新增 translation/runtime/UI/provider 文件或 `4a15f90` 原子写入修复。
本轮仅测试增量接在第一补丁 `968f2931e4d80346a649c19fd68e10ad91bd36e2` 后，
不提交生产快照，不动共享 `Momoka.Tests/Program.cs`、`Agreements.txt`、`Momoka.sln`。

使用本树实际 x64 Release Shell：
`src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.dll`，
2026-10-25 01:36:41 +08:00，SHA256
`b9125a7ff07a46e236603200c97dd0608fd548a84ba588d39431d99fbb5e67f5`。
本轮按固定用户 SDK 10.0.400 离线 restore，再成功 Release 编译当前 Core/相关项目后才 no-build。
Build flags：`--no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false`。
证据留本树 ignored `artifacts/verification/merged-contract-tests/batch02/`，不借父最终功能构建/桌面结果。

只将旧未调用的 `RefineSkipSectionsAsync` 邻接空端点替换为真实当前
`RefineChapterMarksAsync`，新增无 UI/网络的 `ResetDanmaku` 邻接端点；旧 stub 没有生产调用者，
不继续留一个会让人误认 mpv 章节仍修正 skip-plan 的别名。完整 `OnNowPlayingChanged`
仍原样编译，实际 Core 从 Emby MarkerType 构造跳过计划；不复制 skip、所有权、接续或取消逻辑。
弹幕网络请求与画面刷新不在本 caller 覆盖范围。

原 **50 项全过／0 失败／0 跳过**，未修改 `CallerCases.cs` 或提取器。
19 份 SourcePins 完全不变（含上一轮审查的7项更新），文件 SHA256
`d01f0659f4b80374ddb3673912412b5ac0ae4009591bfcc0081c051eaa5a8b7f`。
Start/Next/NowPlaying/Leave 完整节点 SHA256 仍与上一轮清单完全相同。

复跑使用上文既有脚本，输出参数可明确指定：

```powershell
$root = (Get-Location).Path
& (Join-Path $root 'tests/Momoka.NaturalContinuationCallerTests/Run-Caller.ps1') -OutputDirectory (Join-Path $root 'artifacts/r046-caller/batch02-current')
& (Join-Path $root 'tests/Momoka.NaturalContinuationCallerTests/Test-SourceExtraction.ps1') -OutputDirectory (Join-Path $root 'artifacts/r046-caller/batch02-extractor')
```

指定目录必须尚不存在；再次复跑换新名称，不覆盖旧证据。8项原非法源码/锚点/语法/await-using/hash/外树
反例均预期 build 退出1；之后 `current-reverified` 重新编译实际源0警告0错误并再跑原50项全绿。
这些预期拒绝不计为8个产品失败或额外50项覆盖。

## 2026-10-27：右键菜单快捷键提交的单项 pin 同步

`449d468`（播放页右键菜单显示快捷键）只改 `PlayerViewModel.Events.cs` 的菜单构造：`BuildPictureMenuItems`
增加 `shortcutTexts` 参数并递归传递、`ShortcutTexts(ShortcutBindings)` 与两处 `UoscMenuItem` 的 hint 参数；
fixture 提取的 `OnNowPlayingChanged`、`LeavePlayer` 等成员未变。逐份复核后仅更新该文件 SHA256 为
`49f145e7eb68784bfcedd2bd4302a5c2400a8ac39659e63f405eb4759704fc10`，其余 18 份、baseCommit 与 date 不动。
`Run-Caller.ps1` 50/0/0、`Test-SourceExtraction.ps1` 8/8 预期拒绝后当前源重编译 50/0/0；本树方案构建与空白检查通过。
本机 PowerShell 5.1 默认 GBK 时，脚本对 dotnet 的 UTF-8 输出会误读警告数，需先把控制台设为 UTF-8（`chcp 65001`）再跑。
