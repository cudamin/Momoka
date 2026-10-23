# R-071 / R-072 报告收尾专项

独立无窗口控制台项目，已由主线程接入 Momoka.sln 构建映射与开发文档的独立验证命令；不修改公共 Momoka.Tests Program、csproj或报告基线。普通 Core / 页面契约运行不会自动执行本项目。入口为 `Momoka.Tests.Program.Main()`；独立计数，不能把内部断言或重叠覆盖加成其它套件的用例数。

## 验证真实生产控制流

- 直接编译链接 `src/Momoka.Shell/ProbeReports.cs` 与 `ShellSelfCheck.Finalization.cs`。它们也是 Shell 真正使用的报告、Record / Case、收尾与退出码实现，不是仅供测试的 Core 模型。
- 构建时从当前生产文件逐字提取自检 Run 的最终报告段、Schedule 的最后一次回调，以及两种探针的实际入口、Record / Case 委托和退出码属性；边界缺失或起点不唯一会使构建失败。生成源仅在本项目 obj；字符串定位只用于编译真实代码，不以源码匹配充当行为断言。
- 只替换 UI 读数、窗口关闭 / Dispose、Application.Exit、未使用的客户端资源和可选截图端点。两种探针的默认报告写入仍走生产 `File.WriteAllText`，并实际用独占文件锁和目录阻塞路径验证 IO 失败。
- 9 个实际入口用例执行两种探针真实调用方，覆盖成功、断言失败、首次持续写失败、最终持续写失败和 Shell 最终范围报告失败；字幕入口还核对最外层 using 资源确实释放。

## 实现取舍

- 自检 `CompleteReport` 写前保留退出码 1，完整写入返回后且失败数为 0 才置 0；读取、日志或写入异常仍经过真实 `finally` 关闭。未完成报告和可选 tree / PNG 的现有语义保留，不增加或放宽自检基线。
- 两种探针共享 Shell 内部的无 UI 收尾，不把验收基础设施塞进公共 Core。首次写失败仍中止宿主工作并保留失败；最终写只尝试一次，随后无论成功与否都到 Dispose / Exit。
- Shell 最终先尝试范围报告、再写 JSON 正文；范围失败会保存在内存检查中，正文若可写便能带出该失败。两份 IO 不相互短路，任何报告、断言或清理失败均为非零，不用重试刷绿。
- 本专项证明写入 API 完成与报告可读，不声称能抵御系统断电 / 存储控制器缓存，也不增加 fsync 或原子替换协议。

## 精确运行命令

在当前工作树根目录 PowerShell 执行。SDK 为 `$HOME/.dotnet/dotnet.exe` 10.0.400；首次还原，当前 Release 编译成功后才 no-build。固定缓存离线还原，不升级依赖：

~~~powershell
& "$HOME/.dotnet/dotnet.exe" restore 'tests/Momoka.ProbeFinalizationTests/Momoka.ProbeFinalizationTests.csproj' --source "$HOME/.nuget/packages" -p:NuGetAudit=false --disable-parallel -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" build 'tests/Momoka.ProbeFinalizationTests/Momoka.ProbeFinalizationTests.csproj' -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project 'tests/Momoka.ProbeFinalizationTests/Momoka.ProbeFinalizationTests.csproj' -c Release --no-build --no-restore
~~~

`NuGetAudit=false` 仅用于本次缓存还原，不改变仓库审计设置。产物只能来自本工作树；测试会检查 Core 程序集位置。JSON 结果及所有夹具目录保留于本树 `artifacts/r071-r072/fixture/run-<随机值>`，不递归删除。

## 本轮覆盖与证据

- 64 个注册行为用例：自检 12、两种探针各 16、Shell 范围 / inspect 9、实际探针入口 9、收尾异常边界 2；564 次内部断言不作为额外用例累计。全部 0 失败 / 0 跳过。
- 两种探针均覆盖首次写失败持续 / 恢复、仅最终持续失败、普通断言失败、部分最终写、同一异常对象持续抛、取消、路径缺失、真实锁、报告路径为目录、清理抛异常以及报告与清理同时失败。成功用例验证最终 writer 返回前始终为非零。
- 修前保存起点 `b7e91ea8d0304223e13d51741aac333db631e782` 五份源文件及 SHA-256；运行原样提取的收尾片段，17 场景 10 失败 / 7 通过 / 退出 1。它是修前失败证据，不是通过门禁：本树 `artifacts/r071-r072/before/source-manifest.json`、`build-final.log`、`run-final.log`。
- 官方 `tools/selfcheck-diff.ps1 -Log <本次生成报告> -Baseline <独立 A 夹具基线>` 另跑 6 条只读路径：完整报告退出 0；断言失败、锁后未完成、部分写、缺失、目录非报告均退出 1。官方源码和 `docs/selfcheck-baseline.txt` 不修改；不是正式闸门 4 全绿，也不启动其默认产品 / 登录路径。

## 未覆盖边界 / 正式注册需求

没有运行 App、真实 self-check 或完整 `--probe-subtitles` / `--probe-shell`，因为本任务明确禁止 UI / native / 登录 / 配置与凭据 IO。未验证 WinUI 消息循环、真实窗口销毁、截图 / 原生渲染、实际 DPAPI 或真实服务；`close` / `dispose` / `Exit` 可达由源码链接控制流与注入端点证明，不冒充实际 HWND / WinRT 验收。

主线程仅整合题内生产和本专项项目文件，不复制支线PROGRESS。项目已加入 Momoka.sln 与开发文档的独立 restore → Release build → run 命令；公共 Core Program不注册本项目，没有新增产品开关或基线条目。普通完整构建只编译，不自动执行本套件。未发布、推送或更新快捷方式。
