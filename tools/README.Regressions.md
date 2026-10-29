# 专项测试统一入口

`regression-suites.json` 显式登记当前全部顶层测试项目，`run-regressions.ps1` 检查清单与实际项目一致。新增或退役项目必须复核并更新清单，不自动猜测参数。既有测试、包装脚本、源码 pins 和断言不迁移；`build.cmd` 行为不变。

```powershell
# 默认和 -List 都只检查并列出清单，不还原、不构建、不运行。
& ./tools/run-regressions.ps1 -List

# 精确项目名；一次多选时串行执行。
& ./tools/run-regressions.ps1 -Suite @('Momoka.SettingsSearchNavigationTests', 'Momoka.SettingsPersistenceTests')

# 显式选择全部已登记的入口。
& ./tools/run-regressions.ps1 -All
```

所有选择在任何构建前完成校验，未知项目不会导致前面的有效项目先执行。默认用用户目录下 `.dotnet/dotnet.exe` 和 `.nuget/packages`；可用 `-Dotnet`、`-Packages` 指定现有本机路径，不下载工具或替代包。SDK 与 `global.json` 精确匹配。普通项目逐一执行本机缓存 restore、当前 Release 单节点 build，成功后才 `--no-build --no-restore` 运行；还原或编译失败不会运行旧产物。一个专项失败后继续记录其余选择，最后总体退出非零。

需要 Shell 的托管夹具先构建本树标准 Release Shell，当前一次成功构建可供后续同批夹具使用，程序本身不启动。`DetailDeleteCommandTests` 保留原 wrapper 的源码和 DLL 构建证明；`NaturalContinuationCallerTests` 保留原 wrapper 的 pins、固定缓存和构建要求，用户指定 SDK/缓存不会覆盖后者内部已有固定约束。原 wrapper 失败不会退回裸运行。

`ServerSettingsTabs.Tests` 的完整 DOM 测试需显式 Node、Playwright 和浏览器参数，本入口登记为外部前置项，选择它或 `-All` 时报告 **skipped 并总体非零**。按该项目 README 另行运行完整 `Run-Fixture.ps1`；不会将 `contracts` 子集冒充整套通过。`FrameInterpolationTests` 登记的是无参规则入口，原生无媒体探针另按 README 执行；各项目额外的提取器失败场景、真实 UI 或播放探针也不由本脚本冒称覆盖。

每次生成本树 `artifacts/regressions/` 下新目录，可用 `-OutputDirectory` 指定本树 artifacts 下尚不存在的目录。每套保留 restore/build/run 日志，`summary.json` 记录项目、阶段、状态、错误、清单 hash 与可识别的原测试计数。原测试返回 0 但报告 case skip 时记为 incomplete、总体非零；未解析的原包装器计数保持 null，不虚构数量。套件间覆盖重叠，不相加为总用例数。清单或环境预检错误在输出创建前直接非零并说明原因。

这些入口只含现有匿名测试，不加载保存账号或启动产品 UI。主 Core 套件有匿名回环、测试子进程与 `SelfCheckRun` 同用户互斥锁，运行时仍需与其他自检串行。该工具不替代最终主树 UI/运行验证、发布和快捷方式交付。

脚本自身的匿名端点测试：

```powershell
& ./tools/test-run-regressions.ps1
& ./tools/check-scripts.ps1 -Path @('./tools/run-regressions.ps1', './tools/test-run-regressions.ps1')
```

自测复制真实 runner/manifest 到本树 artifacts 下的匿名 Git 目录，使用假 SDK 端点，不编译或运行产品。覆盖默认/List 零调用、成功、restore/build/run 失败停止次序、case skip、外部前置 skip、wrapper 成败、混合未知选择零构建、多套 Shell 依赖只编一次，以及遗漏新项目即拒绝。日志和匿名目录保留，不递归清理用户文件。
