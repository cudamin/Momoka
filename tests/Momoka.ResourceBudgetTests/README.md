# Momoka.ResourceBudgetTests — 资源预算定向回归

这是独立的 .NET 10 控制台测试项目，直接引用实际 Core，并源链接原有离线兼容测试；不使用模型副本冒充生产路径。规则见 [CLAUDE.md](../../CLAUDE.md)，SDK 以 [global.json](../../global.json) 为准。标准工具清单可以直接引用本 README，运行命令不依赖 ignored artifacts。

## 从干净 checkout 运行

在要验证的工作树内执行以下 PowerShell 命令。只还原和构建本专项及其 Core 引用，不构建方案或运行主 Tests 全套；先成功编译当前 Release，才使用 `--no-build`。

```powershell
$repo = (git rev-parse --show-toplevel).Trim()
$dotnet = Join-Path $HOME '.dotnet/dotnet.exe'
$project = Join-Path $repo 'tests/Momoka.ResourceBudgetTests/Momoka.ResourceBudgetTests.csproj'
& $dotnet --version
& $dotnet restore $project --disable-parallel -m:1 -p:BuildInParallel=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '资源预算专项还原失败' }
& $dotnet build $project -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '资源预算专项编译失败，不运行旧输出' }
& $dotnet run --project $project -c Release --no-build --no-restore -- all
if ($LASTEXITCODE -ne 0) { throw '资源预算专项测试失败' }
```

[`Program.cs`](Program.cs) 无参数时同样运行 `all`。末尾的 `all` 可替换为下表入口；统计是产品提交 `dd407b53376eaa493636f3826709a378c9dcc10f` 的实测快照，不是以后固定的基线。

| 入口 | 范围 | 该提交注册项数 |
| --- | --- | ---: |
| `all` | 全部预算及兼容回归 | 141 |
| `budgets` | 下列三个 PERF 预算子集 | 68 |
| `websocket` | PERF11 | 26 |
| `logs` | PERF13 | 28 |
| `fonts` | PERF16 | 14 |
| `compatibility` | 原 Diagnostics／Realtime／字体解析、目录和搜索回归 | 73 |

`file-tail-child` 是日志尾部反例内部启动的私有子进程入口，只接受本测试输出目录内的夹具；不是标准清单的独立任务。项目复用 Core 既有的 `Momoka.Tests` friend assembly 名称，输出位于本专项自己的 bin/obj，不能误运行主 Tests 的同名 DLL。项目及源链接文件见 [csproj](Momoka.ResourceBudgetTests.csproj)。

## 测试覆盖与精确预算

- **PERF11**：整条 WebSocket 消息累计最多 **1,048,576 bytes（1 MiB）**，不是单次 Receive 或单片。text 按 UTF-8 字节计数，binary 同限但不构造消息文本或通知。覆盖 1 MiB−1／恰好上限／上限+1、逐字节 Unicode 碎片、多消息预算重置、text/binary、结束/未结束、取消/EOF，以及超限 Abort 后沿既有退避重连。合法但超过 1 MiB 的大消息也会被拒绝；这是快路径预算，不是实测服务器上限，原只读轮询兜底未改。实现为 [EmbyRealtimeConnection.cs](../../src/Momoka.Core/Emby/EmbyRealtimeConnection.cs)，用例为 [WebSocketBudgetTests.cs](WebSocketBudgetTests.cs) 与 [Boundaries](WebSocketBudgetTests.Boundaries.cs)。
- **PERF13**：默认单卷 **8,388,608 bytes（8 MiB）**、总量 **67,108,864 bytes（64 MiB）**、保留 **14 天**。UTF-8 和换行计入预算；超长 entry 省中留头尾；每行 AutoFlush，重启新卷，不覆写或拼接历史半行。启动一次目录索引，普通写/定时器不枚举目录；每写最多 8 次删除，启动/每 15 分钟维护最多 64 次，严格超过 14 天才过期。覆盖实际文件轮转、超长 Unicode、总量洪峰、运行期/空闲保留、时钟回退、并发、删除/创建失败、子进程不 Dispose 退出后的完整尾部，以及目录/祖先/文件重解析。删除失败时可丢本次磁盘写；既有超量文件分轮回收，外部后加文件重启才纳入索引，所以不能保证无协作外部写入者的整个目录总量。AutoFlush 不是强制物理磁盘 fsync 的断电保证；普通路径检查不宣称抵抗恶意检查/IO 竞态。实现为 [Log.cs](../../src/Momoka.Core/Diagnostics/Log.cs) 与 [FileLogFormat.cs](../../src/Momoka.Core/Diagnostics/FileLogFormat.cs)；用例见 [Boundaries](FileLogBudgetTests.Boundaries.cs)、[Runtime](FileLogBudgetTests.Runtime.cs)、[Reparse](FileLogBudgetTests.Reparse.cs)。
- **PERF16**：decode 前 trim 并限制 **128 个 UTF-16 码元**，完整代理对占两个码元；不是 128 个 Unicode grapheme。name 表最多 **1,048,576 bytes（1 MiB）**，既有 TTC 512 字体上限保留。family/typographic **各 64 候选，不是全字体合计 64**；满桶后首次有效英文替换末候选，可能少留一个本地化名，但保留英文 family 优先、缺 family 才 fallback typographic 和 Unicode 别名语义。覆盖 128/129、Unicode/坏编码、长 padding、候选耗尽前拒绝分配、晚到英文、name 表/TTC 上界和真实临时 FontCatalogue 搜索。实现为 [SfntNames.cs](../../src/Momoka.Core/Infrastructure/SfntNames.cs)；用例见 [FontDecodeBudgetTests.cs](FontDecodeBudgetTests.cs) 与 [Semantics](FontDecodeBudgetTests.Semantics.cs)。

## 夹具与未覆盖边界

- WebSocket 执行真实 Core/session/ClientWebSocket IO，监听仅为匿名 `127.0.0.1` 随机端口；REST restore 使用假传输和占位身份，不登录真实服务器，不改 HTTP 协议。既有握手/重定向兼容用例源链接自 [EmbyRealtimeTests.Transport.cs](../Momoka.Tests/EmbyRealtimeTests.Transport.cs) 与 [Redirect](../Momoka.Tests/EmbyRealtimeTests.Redirect.cs)。
- 日志和合成字体使用测试输出下的私有 GUID 目录；[BudgetDirectory](FileLogBudgetTests.cs) 在递归清理前验证绝对路径边界，[BudgetClock](BudgetClock.cs) 默认使用惰性时钟/定时器。重解析测试只创建、移动、移除自有夹具链接/目录；运行期替换用例反射调用生产 Close，保留索引后替换路径，不把 Windows 打开的目录移动失败当产品缺陷。Windows 重解析/共享锁行为需要支持创建符号链接的环境；失败如实保留，不自动跳过。
- 不读取真实日志、配置、凭据、DPAPI、系统字体目录或媒体；不启动 Momoka、UI、播放器，不上报观看历史。不执行 fullCore/selfcheck、共享 publish、push、master 或 kill。私有日志子进程自行正常退出，没有杀宿主进程。
- 这些断言不证明真实服务器兼容、TLS/代理、真实 WinUI 或播放体验，也不是主 Tests 全套；73 个兼容回归与主套件有源链接重叠，不可作为全 batch 独立净新增数重复累加。统一 runner/sln/共享文档登记、父树集成后的全 batch 验证与交付由父会话负责。

## 已有验证证据

产品提交 `dd407b53376eaa493636f3826709a378c9dcc10f`：SDK 10.0.400，实际 Core 与专项 Release `-t:Rebuild`、单节点及上述 MSBuild 标志，**0 警告、0 错误；all 141/141，失败 0、跳过 0，20,332 ms**。接手原草稿先重跑 137/137，再补四个重解析用例。此 README/PROGRESS 补充仅做文档自查，不声称重新运行上述测试。

原实现 12 个红项（WS 8、日志 1、字体 3）及新增重解析 3 个修前失败均保留在原树 ignored `artifacts/verification/perf-resource-budgets/`：`red-tests.log` 与 `takeover-20261030-165520817/reparse-red2-tests.log`；最终证据为同一 takeover 目录的 `final-build.log`、`final-tests.log`、`final-hashes.json`。这些历史产物不随 clean checkout 提供，不能替代在父最终集成代码上的重验；干净 checkout 使用上面的命令即可。
