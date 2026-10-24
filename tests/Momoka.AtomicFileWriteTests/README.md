# AtomicFile 独立真实文件系统契约

独立、无外部包的 .NET 10 控制台项目。完整 compile-link **当前工作树**
`src/Momoka.Core/Infrastructure/AtomicFile.cs`，不复制、提取或替换生产实现，
不引用或修改共享 `Momoka.Tests`、设置测试、方案或根进度文件。

## 范围与安全

- 必须在 Windows >= 10.0.14393、本地固定盘、非重解析目录上运行。
  缺少环境直接失败，不用 skip 取得绿结果。当前默认宿主为 x64。
- 所有目标、备份、读句柄、压力数据均为匿名夹具；只在本测试目录 ignored
  `outputs/` 中落地，每次使用独立目录并拒绝覆盖证据。
- 不读取真实设置、账号、凭据、注册表，不联网，不启动 UI、原生播放或产品进程，
  不发布、不改桌面、不 push。
- `NuGet.Config` 清空 feeds，csproj 移除根 props 注入的 PackageReference，
  restore 禁用 audit。这里只使用 SDK/仓库自带分析器；assets 中 external libraries 为 0。
- 编译时记录完整生产文件 SHA-256 和源路径；启动时对照当前文件哈希，
  非 Release 或陈旧产物直接失败。证据另存生产依赖逐字快照，但项目不编译该快照。
- 输出保留首个异常类型、HResult、堆栈、固定种子、目标/操作索引、真实属性、
  长度和 SHA-256。断言不匹配本地化错误字符串，也不要求 public 抛 Win32Exception。

## 明确契约

1. `FileShare.ReadWrite | FileShare.Delete` 的真实旧读句柄一直打开：
   public 文本/字节写入成功，新打开读到新完整目标；旧 reader 从非零位置继续读取、
   重新定位后都仍读到原完整数据。
2. 不含 Delete 的 reader：public 文本/字节及带备份写入必须抛
   IOException 或 UnauthorizedAccessException，原文件内容/属性、已有备份不变，
   临时文件清理。不能关闭 reader、放宽共享模式、重试或吞掉错误。
3. 真实设置 ReadOnly 并读回确认：文本/字节和带备份写入失败，
   内容、全部 FileAttributes、已有备份保持。只在用例 finally 恢复匿名目标属性。
4. 131 KiB 级正文末尾孤立 surrogate 触发严格 UTF-8 编码错误；
   另通过实际私有 `AtomicFile.Write` delegate 注入“已写部分正文后异常”，
   验证源确实存在、异常原样传播、finally 删除临时文件且目标/备份不变。
   私有入口仅用于真实正文故障和源属性观测，不替代 public 写入契约。
5. 无备份/带备份的新建、覆盖、空文本/空字节、BOM：新建不伪造备份；
   覆盖逐步验证备份精确等于上个完整前值。带备份新建时已有旁路备份保持。
6. 固定种子/固定预算，不开放种子、repeat 或成功比例参数：

   | 场景 | 种子 | 目标 | 写入预算 |
   | --- | --- | --- | --- |
   | 串行混合文本/字节 | `0x6A09E667` | 256 | 3072 |
   | 四写者混合竞写 | `0xBB67AE85` | 256 | 3072 |
   | 带备份串行 | `0x3C6EF372` | 256 | 3072 |
   | 带备份四写者竞写 + 串行尾写 | `0xA54FF53A` | 256 | 3328 |

   每个文档含版本、固定随机盐、Unicode 和完整尾标记，正文至少 65536 字符。
   串行逐次检查完整主值及精确前值；竞写检查主/备是不同完整候选，再用一次
   已知前件的串行尾写钉住精确备份关系。方法返回顺序不当作提交顺序。
   不在测试中围住 production 加额外 commit 锁。首个失败停止该压力场景，
   已在途调用正常收尾；之后其他独立用例可继续采证，不能抵消前面的失败。
7. 四个真实 reader 经屏障确认已打开并读原文前缀，保持旧句柄跨每轮四次提交。
   新 reader 不与每次提交加锁，完整数据必须属于本轮候选，路径空窗也算失败。
   32 轮/128 次提交/1280 次完整读取，文本/字节混写。
8. 同样的读者场景另覆盖**带备份连续替换**。这比普通备份串行/竞写压力更强：
   首次替换后旧 reader 的文件成为 backup，该 reader 仍打开时再次替换同一 backup。
   修前及无备份修复候选的独立红证据保持；最新备份修复候选必须跑满同一契约，
   不能用普通备份压力通过掩盖，也不能未经确认删掉。
9. 真实 public 写入口的路径边界：超过 320 字符的长路径和 Unicode；
   目录/文件名分别含孤立高/低 surrogate，三个 `U+FFFD` 路径别名哨兵必须不变；
   `\\.\C:\...` 与 `\??\C:\...` 设备前缀在短匿名路径下实际写入，
   不只调用 path helper。DOS-device 用例显式确认 `.GUID.tmp` 后缀也短于 MAX_PATH，
   并先校准正文创建/读取可用，避免把前置长度错误混成 rename 错误。

10. 两个已有备份的拒绝前件：backup 实际 ReadOnly，或 backup 的旧 reader
    不允许 Delete 共享。primary 是可写普通文件并由共享 Delete reader 保持打开，
    防止把 primary 拒绝混成 backup 拒绝；两个用例都通过 public
    `WriteAllText(..., backupPath)` 一次写入，必须抛 public IO/access 错。
    原 primary/旧 backup 字节与全部属性保持、旧 reader 保持、无临时残留，
    不能成功提交前预删/改写旧备份。ReadOnly 属性仅在匿名用例 finally 恢复。
## 正确／错误对照

4 项对照单独计数，不累加生产通过数：完整文档接受，半份/混拼/单字节损坏拒绝；
内容/属性破坏及临时残留拒绝；真实 File.Replace 校准 Delete-shared reader 的旧/新
完整语义；内核拒绝无 Delete 共享和 ReadOnly。
这些对照不是另一个 AtomicFile 实现，没有自制 Windows rename P/Invoke。

## 运行

在**当前工作树根目录**执行（该目录必须与 `git rev-parse --show-toplevel` 一致）：

```powershell
$source = Join-Path (Get-Location) 'src\Momoka.Core\Infrastructure\AtomicFile.cs'
$sha = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
powershell -NoProfile -ExecutionPolicy Bypass -File tests\Momoka.AtomicFileWriteTests\run-evidence.ps1 `
    -Label current-contract -ExpectedSourceSha256 $sha
```

脚本先离线 restore，再 Release 单节点编译：
`--no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
-p:MSBuildNodeReuse=false -warnaserror`；编译失败立即停止。
随后 `run -c Release --no-build --no-restore`。任一用例失败最终退出 1；
启动/完整报告/哈希一致性失败退出 2；不会拿旧输出补成功。

默认完整运行全部 35 项生产契约和 4 项对照。
`-ControlsOnly` 只跑 4 项对照，报告 `contractsExecuted=false`；
`-BackupFailuresOnly` 只用于新增两个备份拒绝前件的局部验证，报告
`allRequiredContractsExecuted=false`、`scope=backup-failures-only`，显式列出其余
33 项生产契约和 4 项对照未执行。二者不能混用，局部退出 0 不能作为全专项通过。

证据批次包含：`preconditions.json`（HEAD/工作树状态/依赖diff/源码与测试哈希）、
`AtomicFile.dependency-snapshot.txt`、restore/build/run 日志与退出码，
`run/result.json`、每例 checkpoint 和 `batch-result.json`。
每个 case 的匿名失败现场保留在本批输出中。不自动删除失败目标、不覆盖旧日志。

检查 PowerShell BOM/LF/语法：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-scripts.ps1 `
    -Path tests\Momoka.AtomicFileWriteTests\run-evidence.ps1
```

## 本次依赖与证据交接

基址 `48c43fc20d5827fe5f5f147eb73e1d1ca7aa2795`，managed worktree
`C:\Users\89400\.codex\worktrees\atomicfile-write-contracts\Momoka`。
按主线程明确授权，只将其生产候选单文件逐字复制到本树用于修后编译；
**Core 副本不暂存、不提交**。测试仍完整链接本树当前文件。

- 修前 production SHA-256：
  `F02A37106727993D9CF379F187D81C102F7ACE02D0A674CF59ADC58DF0424ED0`。
  `outputs/20261025-002333-before-repair-b02724e915e2410e95fa4d4bba6a9d93/`：
  当前 Release 编译 0 警告/0 错误，28 项生产契约 20 过/8 败，4 项对照全过，0 跳过，exit 1。
  首个红项 `shared-delete-text`，UnauthorizedAccessException / `0x80070005`。
  四组固定压力均跑满，但不抵消可控读者失败。
- 修后依赖 SHA-256：
  `DFD4C2D7DC6CE406481D2D67E35F5E09FE8461274DF85BF3A0EF6C5A6F670FD5`。
  `outputs/20261025-002912-after-candidate-a0f2fd102521424898a856ae9907d6c6/`：
  30 项生产契约 28 过/2 败，4 项对照全过，0 跳过，exit 1。
  `\\.\` 前件路径过长导致正文创建失败（保留证据，随后定点修短夹具路径，
  不改生产实现）；另一红项是备份 reader 连续替换。
- 最终夹具定点修正后的批次：
  `outputs/20261025-003452-final-fixture-9a5a67908a914cfdaff3c24e839d753b/`。
  Release 单节点编译 0 警告/0 错误；30 项生产契约 **29 过/1 败**，
  4 项对照全过，0 跳过，exit 1。四组 256 目标压力均跑满；
  无备份读者 128 次提交/1280 次完整读取全部完成。
  所有长路径/Unicode/孤立 surrogate/两种设备前缀边界通过。
  唯一首次失败 `readers-cross-commit-backup`：第 0 轮 operation=1（第 2 次）
  File.Replace 提交失败 IOException / `0x80070497`；成功提交数 1。
  生产依赖编译/运行前后 SHA 完全相同；此前 red 报告全部原样保留。

上述修前版和无备份修复候选版的备份连续读者都在第 0 轮第 2 次提交触发
IOException / `0x80070497`，第一步成功、旧 reader 未关闭。
这两份 red 是原 File.Replace 分支的实际边界，不是声明环境免责或批准通过。
原契约不删除/放宽，也不重跑旧生产版本求绿。

- 新增 backup ReadOnly/拒绝 Delete 两个反例的局部前件验证：
  `outputs/20261025-004802-backup-failure-preconditions-49bca1c7d34f42c18106f7255db4e13f/`。
  依赖仍为 `DFD4C2D7...F670FD5`；Release 单节点 0 警告/0 错误。
  2/2 通过：public 均抛 IOException / `0x80070497`，原 primary/backup 字节属性不变，
  reader 保持打开，目录仅原有两文件。局部报告明确完整专项未执行，不抵消 29/30 红。
  测试 body 后来逐字并入 `AtomicWriteCases.cs`，独立源证据仍留在该批输出，
  使提交保持原 11 文件集合；运行语义未变。

- 主线程最新 backup 修复候选依赖：
  `07504CFA73C72BC105051C3D720F60FEE2F1266BB2B79B57565A08F805AEB59E`。
  只作为未提交生产依赖复制；首次完整验证批次
  `outputs/20261025-005940-backup-candidate-full-b60ec6461c6c4b2fb686dcdd5661ab61/`。
  2026-10-25 01:02:15 +08:00 完成：Release 单节点编译 **0 警告/0 错误**；
  **32/32 生产契约、4/4 对照通过，0 失败、0 跳过、0 未执行，exit 0**。
  四组压力均 256/256 目标，提交 3072/3072、3072/3072、3072/3072、3328/3328；
  无备份/带备份跨提交读者各 128/128 次提交、1280/1280 次完整读取。
  新增 backup ReadOnly/拒绝 Delete 反例均过；所有路径边界保持且通过。
  首个失败为空，生产依赖编译和运行前后 SHA 一致，旧 29/30 红报告未改写。
  该次证据保留；提交前只定点修正 ReaderCases/TestFixture 两处空白换行，断言和预算未变。
  最终 `run/result.json` SHA-256：
  `7228D1759EF3DF7A7C4BD8AC7289B659034E606832C17DF0728CFD75BAF7A004`。

- 最终提交源码验证批次（两处纯空白修正后，未新增用例或改生产）：
  `outputs/20261025-012157-final-commit-source-67957965acba43ebb9d28aaf22b58fa8/`。
  2026-10-25 01:24:35 +08:00 完成：Release 单节点 **0 警告/0 错误**，
  `scope=all`、`allRequiredContractsExecuted=true`；**32/32 生产契约、4/4 对照通过**，
  0 失败、0 跳过、0 未执行、exit 0，全部固定预算完成。
  10 个非文档文件哈希与该次编译运行一致，之后只补 README 结果；生产 SHA 仍为上列 07504CFA。
  脚本 UTF-8 BOM/LF/语法、11 文件 UTF-8/LF/末尾换行和空白检查通过。
  最终 `run/result.json` SHA-256：
  `2FDF6A0996301DB445BF3F316AF808911A1DA85837BA48639C2532F32C72C90A`。
备份发布异常/回滚失败的故障注入验证由主线程另行侧车负责；本测试提交不扩展该注入，
不以正常路径全部通过声称异常回滚路径已被本专项执行。

本专项不建立 x86 ABI、其他平台、UNC/网络盘、断电/掉电持久性、真实设置持久化、
UI/WinRT/播放器或最终发布验证；这些由对应负责人和链路分别验证。

## 主线程整合后的回滚失败路径

当前项目已登记本树方案，另加 `BackupRollbackCases.cs` 的3项实际生产私有
`CommitWithIsolatedBackup` 执行测试：只替换末端发布 callback 为显式异常，
前向 File.Replace、回滚 Move/POSIX、真实读句柄与数据保持均是当前生产方法。
它不复制第二个提交/回滚实现，不以只测底层API冒称生产失败路径。

- 发布失败、没有旧读者：恢复完整旧主文件，保持早一版备份，临时前值消费，原异常保留。
- 发布失败、有共享 Delete 的旧主文件读者：同一文件对象可经原子rename回到主路径，
  旧读者仍打开且完整读取原文。不能用 exclusive File.Replace 重开前值制造回滚失败。
- 发布失败、实际新主文件另有不共享 Delete 的锁：明确抛出发布和回滚两个异常，
  `Data["RecoveryPath"]` 指向保留的完整前主文件；旧备份保持，新主文件仍完整，
  不清理恢复数据或把保存失败当成功。

末端发布故障是人为注入；真实Windows磁盘/ACL/空间在提交中途变化、其他底层
File.Replace部分失败和断电持久性没有全面注入，最终交付不冒称这些极端环境全覆盖。

主线程本树当前整合验证：outputs/postformat35-before-commit/result.json，
35/35生产契约、4/4对照、0失败/跳过/未执行，当前完整生产源哈希与报告一致。
文档中的worker 32项及原red为历史阶段证据，不当当前35项覆盖替代。
