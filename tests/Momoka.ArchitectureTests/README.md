# 架构依赖专项（NetArchTest）

读已编译的 `Momoka.Core.dll` 与 `Momoka.Shell.dll`，用 NetArchTest 的规则表检查分层方向；不加载 WinUI、不启动应用、不连接服务器。已纳入 `Momoka.sln` 与 `tools/regression-suites.json`，入口是 `dotnet run --project tests/Momoka.ArchitectureTests -c Release --no-build -- <工作树根>`。

## 规则

| 规则 | 想守住的东西 |
| --- | --- |
| Core 不依赖 UI 技术栈 | `Microsoft.UI` / `Windows.UI` / `Microsoft.Windows` / `System.Windows` / `System.Drawing` 都不许进 Core（CLAUDE.md「Core 不依赖 UI 框架」） |
| Core 不依赖 Shell | 依赖方向只能是 Shell → Core |
| Shell 不依赖任何测试程序集 | 测试类型不许漏进发布件 |
| Shell.Services 不依赖 Views | 服务层不回头摸视图 |
| Shell.Media 不依赖 Views | 同上，媒体层 |
| Shell.Windowing 不依赖 Views | 同上，窗口层 |
| Core 的程序集引用只有框架 | 直接读引用表：Core 只允许 `System.*`、`Microsoft.Win32.Primitives`、`netstandard`、`mscorlib`，防止悄悄拖进 NuGet 运行时包 |

七条都先在当前树上跑过并通过，不是照搬架构模板。`ViewModels → Views` 这类真实存在十几处的方向没有写成规则：那是重构目标，不是今天守得住的线，写进来只会让整条检查长期变红。

## 运行

~~~powershell
& "$HOME/.dotnet/dotnet.exe" restore 'tests/Momoka.ArchitectureTests/Momoka.ArchitectureTests.csproj' --source "$HOME/.nuget/packages" -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build 'tests/Momoka.ArchitectureTests/Momoka.ArchitectureTests.csproj' -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project 'tests/Momoka.ArchitectureTests/Momoka.ArchitectureTests.csproj' -c Release --no-build --no-restore -- (Get-Location).Path
~~~

先构建当前工作树的 Core 与 Shell（`-c Release`）；本专项自己只读它们的 DLL。默认取 `src/*/bin` 下最新的 Release 产物并在启动时打印路径与时间，找不到就报错，不静默用旧 DLL。

## 边界

- 只证明程序集里的类型依赖与引用表，不证明运行时行为、UI 或真实服务器。
- NetArchTest 的 `HaveDependencyOnAny` 认的是类型全名/命名空间前缀；字段、常量、特性里的引用也在它的扫描范围内，但反射字符串（`Type.GetType("...")`）看不见。
- 依赖 `NetArchTest.eNhancedEdition` 1.4.5（原版 1.3.2 已停更）；升级时按 CLAUDE.md「依赖与分析器升级」单独成轮。
