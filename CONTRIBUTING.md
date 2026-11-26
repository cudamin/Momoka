# 参与贡献

感谢你帮助改进 Momoka。向本仓库提议外部代码贡献时，请先开 Issue 说明问题或方案。小范围文档修正可以直接提交 Pull Request。维护者在工作区直接委托的开发按项目规则执行。

动手前先读[项目常设规则](CLAUDE.md)，再按[技术文档与专题地图](docs/技术文档.md#14-文档地图)查阅相关实现说明。文档维护与清理遵守 [CLAUDE 的文档规则](CLAUDE.md#文档查阅与维护)，相关行为变化与文档在同一逻辑变更中更新。

## 开发环境

- Windows 10 1809 或更高版本，x64
- .NET SDK 10，版本以 [`global.json`](global.json) 为准
- PowerShell 5.1 或 PowerShell 7
- Python 3（文档和报告检查需要）
- Node.js/npm（重复代码检查需要）
- 如果要运行播放或发布，另需本地匹配的 x64 `libmpv-2.dll`；它不会提交到仓库

## 提交前检查

按[项目规则的验证分级表](CLAUDE.md#四道闸门与日常交付)选择所需检查。纯文档使用[文档验收](CLAUDE.md#文档查阅与维护)列出的检查。以下是代码改动的构建和离线检查示例，在当前工作树根目录运行。

```powershell
dotnet restore .\Momoka.sln
dotnet build .\Momoka.sln -c Release --no-restore -warnaserror
dotnet run --project .\tests\Momoka.Tests\Momoka.Tests.csproj -c Release --no-build --no-restore
py .\tools\check-docs.py
py .\tools\check-main-tree-policy.py --self-test
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check-scripts.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check-doc-commands.ps1
py .\tools\check-report-credentials.py
```

需要完整离线专项时，再运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\run-regressions.ps1 -All -Dotnet (Get-Command dotnet).Source -Packages (Join-Path $env:USERPROFILE '.nuget\packages')
```

这些检查不等于真实媒体服务器、真实账号、真实播放、触摸、读屏或所有 DPI 环境验收。不要为了让检查变绿而删除测试、放宽基线或提交真实凭据。

## Pull Request

- 每个提交保持一个可以独立回滚的逻辑变更。
- 新行为或缺陷修复同时补相应测试；纯文档变更说明检查过的链接和命令。
- 描述实际验证过的内容，以及没有验证的边界。
- 不要提交 `bin/`、`obj/`、`artifacts/`、本机工具会话数据、截图、日志、密码、令牌或 Cookie。
- 如果改动第三方组件、许可证或发布包，请同时更新归属和对应源码信息。

真实服务器播放、服务器写入、触摸和屏幕验收不属于普通 CI 步骤；如改动涉及这些行为，请在 Pull Request 中明确标记未覆盖的范围。
