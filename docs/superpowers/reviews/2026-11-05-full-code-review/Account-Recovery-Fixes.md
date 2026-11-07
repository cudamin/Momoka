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
