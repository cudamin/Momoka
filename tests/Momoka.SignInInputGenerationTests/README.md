# R-008 登录身份输入代次专项（无窗口）

只验证登录等待期间的 **VM 用户输入、请求取消及完成通知归属**。整个实际
`SignInViewModel`、`PageViewModel`、`LoadGeneration` 及纯输入／地址函数通过 source-link
编译，`CommunityToolkit.Mvvm 8.4.2` 使用与 Shell 相同的真实属性／命令生成器。

没有 Core／Shell 程序集引用，没有实际 `EmbySession`、Gateway、认证、restore、re-auth、
HTTP、凭据读写、DPAPI、配置保存、真实用户互斥、原生库、媒体或 WinUI 应用运行。
`Boundaries.cs` 只提供枚举／DTO、内存设置／假密码值、公开列表值端点和窄 fake Session。
`FakeSignInAttempt` 原样记录 VM 提交的 A／B 参数快照与 CancellationToken；手动释放确认后
只观察该 token，不比较 VM 输入、不另造账号／身份／代次模型。fake Adopt 是收据列表增加一项，
不是实际会话采用／保存。地址均为 `.invalid`，不记录任何真实凭据；断言仅使用合成占位值。

## 范围与取舍

- 每次登录票捕获选中行的不可变 ID 值；真正选中非空行时与该提交 ID 比较，不使用事件
  `oldValue`。因此 A→null→B 的同名不同 ID 也同步撤销 A。null 自身不取消，same ID 复制免取消，
  `_filling` 不误取消也不把在飞票的提交 ID 迁移到默认行。非等价用户名编辑沿用原取消逻辑。
  取消不跳回地址步骤、不清 B 新草稿，原选 B 的假保存密码／Remember／焦点填充继续。
- 登录命令启用 `AllowConcurrentExecutions`，因为旧 endpoint 可以在已取消后仍等待迟到确认；
  实际 `Busy` 方法／CanExecute 门控继续拒绝未取消身份的重复提交。不是放开并行认证授权。
- 新票拥有自己的 Busy／成功／失败／finally。旧 A finally 只清自己的 token／捕获 ID，不结束或
  清除 B 的当前票；因此第三次身份编辑仍能取消 B。
- 不改 Session、Gateway、vault、协议／token／hash／持久化／profile，也不修 R-001／003／005／031。
  不改 shared 测试 Program、方案、技能、增量文档或主树。正式项目单独运行；主线程决定整合。

## 执行（PowerShell 7）

在本项目所属工作树执行，脚本用 Git 顶层核对路径。SDK 固定为
`C:\Users\89400\.dotnet\dotnet.exe 10.0.400`；不改 PATH／SDK／包版本。

```powershell
$root = 'C:\Users\89400\.codex\worktrees\r008-login-input-generation\Momoka'
$run = Join-Path $root 'tests\Momoka.SignInInputGenerationTests\Run-Fixture.ps1'

# 首次只从本机固定 nupkg 快照还原：缺包立即停，不用外网补包。
# 初次有效未修 red 已在本树保留，不需要重新覆盖产品源码。
& $run -RunName red -Baseline -Restore

# 对保留的首修 cdfb832 复现同名不同ID经null改选的补修red。
& $run -RunName null-red -Baseline -BaselineCommit 'cdfb83212694953b8a39d1c2ad9d7cab57f81197'

# 首次当前变体还原；Shell仅编译，不运行应用。
& $run -RunName current -Restore -BuildShell -Format

# 同一固定缓存已经还原后的最终验证。
& $run -RunName final -BuildShell -Format
```

`-Baseline` 只将精确 Git blob
`1b37ca14bbbdf5699ac686aac891a7b2e14c9bdf:src/Momoka.Shell/ViewModels/SignInViewModel.cs`
写入本次证据目录，以 `git hash-object --no-filters` 核对；**不覆盖工作区产品文件**。
`-BaselineCommit` 只接受原基点或首修 `cdfb83212694953b8a39d1c2ad9d7cab57f81197`；两者均精确导出并核 blob 字节。基点与当前 fixture 的 bin／obj 分开，同一 Program／fake／真实生成器分别验证 red 和 green，原提交和历史有效 red 不覆盖。

脚本将 CLI home、temp、NuGet packages／HTTP／plugin cache、固定本地 feed 和全部日志放在
本树 `artifacts/r008-signin-input-generation` 及 `work/r008`。
`DOTNET_GENERATE_ASPNET_CERTIFICATE=false`；`NuGetAudit=false` 仅传给离线 restore 调用。
固定快照首次失败即停，既不刷新快照也不重试外网。所有 build 使用 Release、`--no-restore`、
`-m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false`；
只有当前变体刚成功编译且零警告／错误后才运行 `--no-build --no-restore`。

格式检查仅题内 VM／fixture，读取真实 x64 Shell 项目，不写工作区；随后当前成功 Shell build
为实际 `SignInPage.g.cs` 和程序集生成带源码 hash 的回执。`Test-GeneratedBindings.ps1` 只接受
本树回执／当前 g.cs，不读取 backup／g.i／其它树：17 项核对控件动态绑定、即时写回、
命令、通知和真实 Toolkit 选中行／用户名回调；8 个只在内存破坏的反例必须被同一验证器拒绝。

## 用例与计数边界

当前专项 **77 项**，包含原 44 项和补修 33 项。相同最终 fixture 对首修 `cdfb832` 为
**59 通过／18 失败／0 跳过，退出 1**，当前 VM 为 **77 通过／0 失败／0 跳过，退出 0**。
原精确基点的44项23过／21败、首修44项全过，以及补修初次73项57过／16败的有效日志均保留，
不重复统计、不以历史44绿证明经null场景通过。主要场景：

- A 迟到确认／迟到失败 → 实际 Username B 或 SelectedUser B → A 无 Adopt／SignedIn，B 草稿保留。
- B 有密码新提交、B 无密码自动新票，A→B 和 B→A 两种结果顺序；A 后续 finally 不结束 B。
- A 先收尾后再改第三份草稿仍能撤销 B；B 当前失败不被 A 旧成功覆盖；输入 A→B→A 不复活旧票。
- 同名不同ID直接／经null × 有／无密码 × A先／B先；B票继任后的第三次取消及same ID复制、
  _filling切默认行后仍按提交ID判定、提交DTO ID被修改、提交未选ID再选非空同名行。
- 等价名字／地址、同 ID DTO 复制、公开 connect 期间用户名编辑及 _filling；真实地址改变、
  返回／Cancel／Detach／Dispose；重复命令、当前失败／重提、Busy 文案／命令通知及集合身份稳定。
- 实际 XAML 和页面接线作源码契约核对；当前生成绑定 17 项、验证器反例 8 项单列，
  **不与 77 项或完整 Core 计数累加**。

## 未验证（必须保留）

真实 Session 的 `SignInAttemptAsync` 提交锁／Adopt／凭据保存、生产协议、服务器响应与状态均
**没有运行验证**；静态可见该提交边界已检查调用方 token，但此 fixture 只证明真实 VM 已同步
取消并传给窄 fake 确认端点，不等于真实 Session commit／服务器验证。真实 WinUI 输入、
列表／焦点／键盘／页面渲染也没有运行验证，生成绑定是编译证据而不是桌面截图。
没有 full Core、SelfCheck、用户 mutex、实际应用、native、播放、服务器／外网、配置凭据、
发布／推送；桌面快捷方式仍是旧版。此提交供主线程整合重验，不宣称整批产品已发布交付。

## 主树限定整合（2026-10-24）

从最终 `afd448469fdbd7db947668254739ee57b693bb0a` 的精确Gitblob复制登录VM与正式专项9文件（含首修cdfb832），不搬来源进度/DLL/日志；原VM基点与主树无冲突，产品与来源最终字节一致。主树自行加入方案并明确Release映射，`run-main-integrated-20261024-092417-6068`固定缓存restore、当前真实fixture与x64Shell Release零警告错误，77/77通过、0跳过，题内format零修改无加载诊断；format后freshShell产生actual当前g.cs，再17生成绑定与8输入反例通过，未读backup。主线程补文档/源码编码/PS语法/来源哈希、独立复核后才计完成。各专项独立不相加，不据此冒生产认证链或WinUI输入绿；本批剩R011/R067收束后按用户指示暂停。

## 主线程同步入口补修（2026-10-24 恢复后）

独立复核使用完整原样VM和真实Toolkit发现：Busy同步通知改身份时，旧票/ID尚未发布，旧请求仍可发送。主线程在通知前捕获地址、用户名、选中行字段、密码归属和Remember，`BeginLoad`窄回调先发布票/ID、每次同步通知后核当前票；保留旧无参入口。取消/旧finally不写后继状态，`EndLoad`的Busy通知开启B后也不把B报成已完成；Dispose在原退订方法中结束请求与Busy，并守未来调用。未改生产Session/认证/持久化。

同一最终112项（原77＋35同步边界）对**固定afd448的SignInVM和PageVM两个Gitblob**：93过19败/0跳过/退出1；当前112全过/0跳过/退出0。对照包括Busy/IsReady/NoticeOpen/Caption内编辑身份、无密码B自动继任、同ID/等价编辑/null/默认填充、Cancel/Detach/Dispose、输入快照、手动新票和EndLoad继任。真实页面双向绑定只静态和actual生成输出检查，不冒真实WinUI重入场景或服务器认证验收。

两阶段错误保留：初baseline在重定位obj时编入正常obj/generated，20个重复特性/成员错误，未运行测试；项目显式排普通obj/bin后重新编译。首111绿前107过4Dispose败和修时一次重复Dispose编译错误均留盘，合到既有退订路径后绿，不删除输出或放宽断言。最终runner同步固定旧PageVM/SignInVM，防单改VM而配新base误当精确基点；两个blob记录各自独立文件。

证据：`artifacts/r008-signin-input-generation/run-resume-final-paired-red-20261024-095259-4829`和`run-resume-final-paired-green-20261024-095343-3510`，最后actualShell绑定在`run-resume-sync-final-20261024-095007-7371`（17＋8），正常方案项目和共享base专项重验在`artifacts/staged-repair/batch05/r008/resume-*`。当轮R01039/R02768/Subtitles36/PageContract98/Resource21/AniBt53/飞牛Defaults36/SettingsLifetime45全部过，各套不相加；脚本/源码哈希与最终一致性单独记录。

## 取消与失败反馈尾部补修（最终128项）

原同步入口112项之外，14项真实取消/失败尾部反例先跑 **112过14败**：Busy=false通知里填B草稿或提交B后，旧Username/SelectedUser hook仍清空或覆盖草稿；Severity/Title/Message/Open/Caption各同步通知启动B后，A错误尾部仍显示或抢密码焦点。修后输入编辑/新票使用本VM的`_inputVersion`，选中行自动填充只接受自己的单次setter版本推进，任何嵌套编辑/新票撤销旧填充；登录失败逐属性通知后核原token，并在聚焦前再核。随后新增same-ID但非等价用户名有/无密码两项保原取消规则（126过2败→128全过），点击快照保选中ID和标准用户名，不因自己填充回调抑制误保旧票。

最终128项配**完整afd448两个旧VM**为 **95过33败/0跳过/退出1**，当前 **128过0败0跳过/退出0**；35启动/EndLoad＋14尾部＋2姓名护栏是新增用例，原77未削减。最终实际格式0修改、fresh x64Shell零警告错误后生成17项和8非法输入拒绝绿；正常方案目录128也绿，重新运行当前SettingsLifetime45绿。其它sharedBase回归39/68/36/98/21/53/36均当前重新编译过，测试不累加。

最终红目录 `artifacts/r008-signin-input-generation/run-resume-final128-red-20261024-101017-5456`，最终绿目录 `run-resume-final128-green-20261024-101057-4276`；正常项目/相关结果及最终源码日志哈希见`artifacts/staged-repair/batch05/r008`。这些只证明完整VM/真实生成器/窄回执的取消与草稿契约；真实WinUI触发性、Session提交锁、真实凭据保存与服务器未验保持。
