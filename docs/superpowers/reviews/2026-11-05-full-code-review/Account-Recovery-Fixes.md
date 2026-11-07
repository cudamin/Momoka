# R03-003 与 R03-005 账号恢复修复记录

2026-11-07，隔离树 `C:/Users/89400/.codex/worktrees/review-account-lifetime/Momoka`，基点 `08324e33b355a118112d849ae3e30c8f549b0770`。主树整合、最终验证和发布由批次交付会话执行；本树不写主树、不连接真实服务器、不读取真实配置或凭据、不播放、不推送、不发布或修改快捷方式。

## R03-003 同服产品纠正与并发恢复

原来的四处恢复守卫比较旧API基址。同服第一个账号确认并写回合法Emby→Jellyfin纠正后，另一个已确认账号被误认为地址已编辑。

修复在原生命周期锁下一次性捕获原连接和原输入地址，匿名确认后固定预期连接。当前档案的产品、规范化API基址、可空ServerId必须精确匹配原绑定或本次确认绑定，才允许四处恢复守卫继续。原取消／代次守卫和密码回退的最终账号／服务器身份核对保留；名称、版本不是绑定身份，不把同源当同服务器。

新增29项：24组令牌／401密码回退／仅密码×两种完成顺序×两种反代前缀×原ID有／无，以及URL／ID／产品编辑拒绝、取消、原Jellyfin对照5项。使用真实Core Session、Gateway、Vault与SettingsStore，HTTP只接受匿名 `.invalid` 来源，配置写入独立临时目录。核对双来源、默认B、保存重读和显式重试不重复来源。通过既有 `RestoreIdentityR003Tests.Register()` 接入全量及 `restore-identity-fixture`，不修改共享主测试Program。

变更文件：

- `src/Momoka.Core/Emby/EmbySession.cs`
- `tests/Momoka.Tests/RestoreIdentityR003Tests.cs`
- `tests/Momoka.Tests/RestoreParallelCorrectionTests.cs`
- `tests/Momoka.Tests/README.RestoreIdentity.md`
- 本记录。

有效RED在产品源码尚未修改时执行：本机缓存restore、当前Release Core／Tests编译均退出0、0警告／0错误；全量正式runner共3972项，3960通过、12失败、0跳过、退出1。12个失败全是 `/media/` 前缀的同服纠正矩阵；`/media/emby/`、用户编辑／取消和原Jellyfin对照通过。每组失败明确得到期望2、实际1，不是测试接线或编译失败。

原始证据相对本树路径：

- `artifacts/review/account-recovery/red-restore-core.log`
- `artifacts/review/account-recovery/red-build-core.log`
- `artifacts/review/account-recovery/red-core-full.log`
- `artifacts/review/account-recovery/red-assembly-hashes.json`（保留原测试和Core程序集哈希）。

GREEN使用当前产品源码重新编译Release Core／Tests，退出0、0警告／0错误；`restore-identity-fixture` 共115项、115通过、0失败、0跳过、退出0，包含原86项及新29项。日志为 `artifacts/review/account-recovery/green-build-core.log` 与 `green-restore-identity.log`。新增测试登记只移入既有恢复入口，全量仍通过SessionTests登记；中间不重复运行完整Core，最终全量由主树交付会话执行。

静态差异复核及 `git diff --check` 通过；不新增通用身份抽象、不改普通HasAddress或resolver许可规则。主树最终完整测试、实际账号恢复界面、真实Emby／Jellyfin部署、DPAPI和播放不在本树运行范围。

独立产品提交：`0faa517411dd7b08fc0e21416ae454153ddc7cc8`。

## R03-005 关闭期间登录与排队通知

首个关闭await前先设置永久closing状态，退订SignIn.SignedIn并调用现成真实SignInViewModel.Dispose；只Detach／Cancel仍允许未来命令，不能终止关闭期间重新提交。进入Shell、显示登录、Start、Switch、目录重试、聚合模式及已排队退出／来源通知均使用同一关闭守卫。FinishChangingSources继续递减计数、清pending；不提前SignOut或Dispose Session，已捕获来源的合法收尾保留。

关闭测试链接完整真实SignInVM／PageVM及Shell同版Toolkit生成器，原样提取当前Shell实际关闭、入口和通知控制流。真实Core Session／Gateway／SettingsStore使用独立临时设置和合成占位凭据，只接受close.invalid；Frame／DispatcherQueue／翻译和播放器停止是可控边界，不重写认证采用或关闭算法。新增16项，原目录15项全部保留，共31项。最终另补充迟到来源通知不更新SignInText及pending清理断言；所有关闭等待先释放并完成再报告断言失败，避免留存后台收尾。

变更文件：

- `src/Momoka.Shell/Views/ShellPage.xaml.cs`
- `tests/Momoka.HomeDirectoryTests/Momoka.HomeDirectoryTests.csproj`
- `tests/Momoka.HomeDirectoryTests/PageBoundaries.cs`
- `tests/Momoka.HomeDirectoryTests/AccountLifetimeBoundaries.cs`
- `tests/Momoka.HomeDirectoryTests/AccountShutdownTests.cs`
- `tests/Momoka.HomeDirectoryTests/Program.cs`
- `tests/Momoka.HomeDirectoryTests/README.md`
- 本记录。

有效RED使用未改产品源码：当前Release HomeDirectory编译0警告／0错误后，31项17通过、14失败、0跳过、退出1。14项明确复现未撤销在飞登录、未来登录仍提交、关闭后新目录／聚合／登录页操作与已排队退出复活；旧15项目录及来源绑定合法收尾对照通过。初次测试边界编译的单条VSTHRD001警告原日志保留；仅在无原生UI／JoinableTaskFactory的测试dispatcher Post处加有理由的局部抑制后，重新编译至0警告，才取得有效RED。

GREEN使用最终产品及测试代码重新编译：HomeDirectory 31/31，原SignInInputGeneration 128/128，均0失败、0跳过、退出0；Core及三测试项目、实际x64 Release Shell编译均0警告／0错误。115项恢复身份GREEN见上，不与其它套重复相加。

证据相对本树路径：

- `artifacts/review/account-recovery/red-restore-home.log`
- `artifacts/review/account-recovery/red-build-home.log`、`red-build-home-clean.log`、`red-home.log`
- `artifacts/review/account-recovery/green-build-home.log`、`green-home.log`
- `artifacts/review/account-recovery/green-restore-signin.log`、`green-build-signin.log`、`green-signin-input.log`
- `artifacts/review/account-recovery/green-restore-shell.log`、`green-build-shell.log`
- `artifacts/review/account-recovery/green-source-hashes.json`、`green-assembly-hashes.json`、`green-HomeDirectorySources.g.cs`

本轮SignInInput与Shell还原使用本机固定包缓存作为唯一包源，NuGetAudit=false仅用于离线restore；未升级产品依赖。Core／Tests／Home受影响文件的whitespace检查退出0；Shell whitespace退出0但存在设计时工作区警告，诊断确认默认Debug引用找不到本树Core Debug DLL（实际只编译Release），不能作为完整Shell格式检查。原始日志 `green-format-shell.log` 与 `green-format-shell-diagnostic.log` 保留，不为格式检查另建Debug或修工具链。变更文本编码／行尾／BOM／空白脚本检查通过，最终提交前再次执行。

未创建真实HostWindow，不证明实际翻译／播放器停止、页面像素、窗口闪回、关闭输入队列或真实服务器；这些运行验收、主树最终完整测试和交付仍由批次交付会话执行。本树没有推送、发布或刷新快捷方式。
