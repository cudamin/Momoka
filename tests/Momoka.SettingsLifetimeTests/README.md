# R-018／R-026 设置目录与隐藏生命周期专项

独立离线项目；子代理未修改共享测试 Program／方案／开发文档，主树整合时已最小加入方案并同步开发入口。下方红绿证据属于明确列出的来源独立树，不代替主树当次重验；按下面命令在最终树单独运行。

## 真路径与边界

- R-018 反射加载**显式传入、本树标准 Release 路径**的真实 `Momoka.dll`，调用真实 SettingsViewModel.Attach／RefreshServerAdministrationAsync／Cancel／Dispose，使用真实 Core ServerCapabilities、EmbySessionScope、EmbyClient 与 HTTP 读取。身份只在内存构造和切换，不调用登录、SelectSource、SignOut、Persist 或凭据 API。
- R-026 构建时从当前 SettingsPage.xaml.cs／SettingsWindow.cs **原样提取并编译**构造器事件接线、OnNavigatedTo、刷新、UpdateCategoryGroups、Select、ShowHosted、Release、通知、Show／Hide／OnClosing／Close。边界标记缺失或起点不唯一立即构建失败，不允许静默退回复制实现。
- 原生偏好页编译链接完整真实 FnNasPreferencesPage.xaml.cs；其 Attach／Reload／Cancel 透传同一真实 Shell DLL 的 FnNasPreferencesViewModel／FnNasSessionViewModel。实际 GET 为真实 user/info 与 sys/config 调用，假 handler 可控制旧响应晚到。
- UI 值、Frame 导航、窗口显隐、DispatcherQueue 是无 WinRT/HWND 的边界替身；VM 不复制。页面只运行已有缓存的 built 路径，草稿行由真实类型在内存建立；完整 XAML 建页、字体/音频/native、未用的 sibling 页和服务是拒绝执行的边界。
- 真实会话的来源只在本项临时 AppPaths 下关联内存占位 profile；假 HTTP 仅接收 settings-lifetime.invalid 上允许的 GET，不做额外网络、真实账号、配置、DPAPI 或凭据读取/写入。未启动 App、native、播放、SelfCheck、桌面 UI、发布或推送。
- 共 39 项：R-018 21 项，R-026 18 项。用户级 mutex 的全 Core／SelfCheck 完整套件不在本项运行。该结果不是实际 WinUI 验收，不覆盖 HWND/消息循环、XAML 渲染、焦点/滚动/输入、真实服务器或真实账号出现频率。

## 命令

在**当前工作树根**运行，输出只来自当前树。先确认 git 根和 HEAD，再还原并编译当前源码；不得编译失败后跑旧 DLL。

```powershell
$root = (Get-Location).Path
$dotnet = 'C:\Users\89400\.dotnet\dotnet.exe'
$shell = Join-Path $root 'src\Momoka.Shell\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Momoka.dll'

& $dotnet restore .\src\Momoka.Shell\Momoka.Shell.csproj --source 'C:\Users\89400\.nuget\packages' --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
& $dotnet restore .\tests\Momoka.SettingsLifetimeTests\Momoka.SettingsLifetimeTests.csproj --source 'C:\Users\89400\.nuget\packages' --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
& $dotnet build .\src\Momoka.Shell\Momoka.Shell.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $dotnet build .\tests\Momoka.SettingsLifetimeTests\Momoka.SettingsLifetimeTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $dotnet run --project .\tests\Momoka.SettingsLifetimeTests\Momoka.SettingsLifetimeTests.csproj -c Release --no-build --no-restore -- $shell $root
```

NuGetAudit=false 仅这次固定缓存的完全离线还原；不改变仓库审计、包版本或构建基线。新增项目沿用共享 props 的现有分析器，不升级或卸载依赖。

## 本树红／绿证据

证据根：`C:\Users\89400\.codex\worktrees\r018-r026-settings-lifetime\Momoka\work\r018-r026`（本地保留，忽略日志不入 Git）。

- 基点：`7f18f128fdefb7e4a9f94728be68701914b92052`；`build-red-shell.log` 与 `build-red-fixture-4.log` 均为 0 警告／0 错误。
- 有效修前：`red-regressions-2.log`，39 项通过 10、失败 29、跳过 0，退出 1。R-018 普通 B 隐藏后被 A 旧空表重新打开、新管理员 B 插件被清掉，以及 R-026 Hide/X 后新增偏好页/GET 均为真实控制流断言失败。
- 最初 `red-regressions.log` 的构造器选择错误及前三次 fixture 构建调试日志单列保留，**不计作产品缺陷红证据**。
- 第一轮 `green-regressions.log`：37 通过、2 失败。暴露同服账号变更后重开导航尾部重复创建原生偏好页；产品 OnNavigatedTo 最小去重后重验。
- 最终 `build-green-shell-2.log`／`build-green-fixture-2.log` 均为 0 警告／0 错误；`green-regressions-2.log` 39 通过、0 失败、0 跳过，退出 0。
- 相关既有 `settings-page-state-fixture`：`build-related-tests.log` 0 警告／0 错误，`green-related-state.log` 27 通过、0 失败、0 跳过；分开报告，不加总成 R-018／R-026 新覆盖。
- `format-shell-release.log`／`format-fixture-release.log` 为定向 whitespace 校验通过、改动 0 文件；初次默认 Debug 的 format 提示缺本树 Debug Core 输出，不冒称完整校验，改用当次 `$env:Configuration = 'Release'` 后零加载警告。

## 实现取舍

复用既有 LoadGeneration，生产改动只在三个授权文件；没有抽新服务/规则类。VM 捕获当前来源身份与键集合、核旧作用域仍可用、每次 await 后核活动期/刷新代次/来源并重读权限；旧空结果不能清新插件。页面活动期与读取代次分开：隐藏前排队的回调不能借重开复活，取消只停读取/订阅，不清草稿或绑定集合。X 复用幂等 Hide，Close 先释放。重开同步刷新已经建出新的原生页时不在导航尾部重复 GET。

## 相关定向复验与空白检查

```powershell
& $dotnet restore .\tests\Momoka.Tests\Momoka.Tests.csproj --source 'C:\Users\89400\.nuget\packages' --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
& $dotnet build .\tests\Momoka.Tests\Momoka.Tests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $dotnet run --project .\tests\Momoka.Tests\Momoka.Tests.csproj -c Release --no-build --no-restore -- settings-page-state-fixture $shell $root

$env:Configuration = 'Release'
& $dotnet format whitespace .\src\Momoka.Shell\Momoka.Shell.csproj --no-restore --include 'src/Momoka.Shell/ViewModels/SettingsViewModel.cs' 'src/Momoka.Shell/Views/SettingsPage.xaml.cs' 'src/Momoka.Shell/Views/SettingsWindow.cs' --verify-no-changes --verbosity diagnostic
& $dotnet format whitespace .\tests\Momoka.SettingsLifetimeTests\Momoka.SettingsLifetimeTests.csproj --no-restore --include 'tests/Momoka.SettingsLifetimeTests/AdministrationLifetimeTests.cs' 'tests/Momoka.SettingsLifetimeTests/SettingsHiddenLifetimeTests.cs' 'tests/Momoka.SettingsLifetimeTests/LifetimeFixture.cs' 'tests/Momoka.SettingsLifetimeTests/PageBoundaries.cs' 'tests/Momoka.SettingsLifetimeTests/ShellRuntime.cs' 'tests/Momoka.SettingsLifetimeTests/Program.cs' --verify-no-changes --verbosity diagnostic
git diff --check
```

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

保留原 45 项 R018/R026/R010，新增 5 项 Attach 边界检查，当前 **50 过/0 败/0 跳过**。
三个页面夹具的弹幕 callback 都只转发当前真实 Shell VM；ShellPrefs 是内存事件通道。

页面 wrapper 保留原 14 个必需参数及顺序，images/dandan/后来可选凭据按原声明尾序列传入；
反射补齐读取实际 `GetParameters()` 的默认值，不硬编码 16 或 17，也不把非 null 默认强行改 null。
缺少必需值/多余参数仍 fail-closed，显式传入值（包括 null）不被覆盖。新增用例核真实参数名称、
images/dandan 的位置、任意可选尾部默认值以及非 null 默认值反例。

完整页面 `Attach` 仍明确拒绝 UI 重建，`ReloadAsync` 仍拒绝整页/native Reload；实际缓存 VM 的
Attach 则走当前真实 DLL 方法，不为兼容新凭据开放 UI/native 或 Protect/Unprotect 边界。
复跑命令与上文相同，额外测试自动注册到本专项而不碰共享 Program。
