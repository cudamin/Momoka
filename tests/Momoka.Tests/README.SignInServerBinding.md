# R03-001：手动登录保留服务器凭据绑定

同地址的保存服务器换成另一台后，新账号登录不得把原账号的令牌、密码和服务器身份一起迁移。修复保留同 ID 的产品／反代纠正、同产品缺 ID 的旧配置兼容，以及输入取消和提交代次。

`SignInServerBindingTests` 使用实际 Core Session、Gateway、SettingsService、SettingsStore、CredentialVault；专用入口另外加载显式传入的当前树 x64 Release Shell，执行完整真实 SignInViewModel 和生成命令。替身只在 HTTP 与保护器边界：限定 `binding.invalid` 和固定占位凭据，不读真实配置、不联网、不启动 App、WinUI 窗口或播放器。

先按 CLAUDE.md 编译当前 Release Shell、Core 和主测试，再运行：

```powershell
$tree = (Get-Location).Path
$shell = Join-Path $tree 'src\Momoka.Shell\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Momoka.dll'
& "$HOME/.dotnet/dotnet.exe" run --project tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- signin-server-binding-fixture $shell $tree
```

独立入口执行 Core 与真实 VM 两组。Core 组也由 `SessionTests.Register()` 加入普通主套件，不重复计数；另外回归 `restore-identity-fixture` 和 `Momoka.SignInInputGenerationTests`。输入专项的假 Session 只证明输入与取消契约，不能替代本专项的真实提交／保存重载检查。

只改 Core 时可先使用 `signin-server-binding-core-fixture`，不加载 Shell。最终再编译 Shell 并运行完整入口，不能用 Core 单组代替完整 VM 验证。

已有绑定的产品只允许同产品或 Emby／Jellyfin 族内纠正；族内换产品要有本次非空确认 ID，已知旧 ID 还须精确相等。同产品允许任一身份响应缺 ID，保存旧 ID；两个非空响应 ID 必须互相相等。旧 Emby→Jellyfin 的后登录公开信息缺 ID 仍拒绝，避免仅凭认证响应把共享旧凭据换产品。

匿名设置 IO 限于本树测试输出下 `signin-binding-fixtures/<随机>`，保留输出供复核。证据不包含请求正文、请求头或凭据值。真实服务器、DPAPI、WinUI 输入／焦点／视觉、发布与安装未覆盖；R03-003 的并发恢复及 R03-005 的 Shell 关闭接线另行处理。

2026-11-07 本树最终串行验证：Core 单组 83 项、完整 VM／Core 105 项、恢复 86 项、输入 128 项均全过，失败／跳过均 0。每套独立计数，不累加。首轮 38 项 10 过 28 败、产品族收束 55 项 45 过 10 败、复审新增边界 Core 83 项 55 过 28 败，均保留有效红日志。最终 Shell、Core／Tests 与输入专项零警告／错误。

证据根 `artifacts/r03-001-signin-binding`，`final-evidence.json` 固定本树最终源文件及加载程序集哈希，`log-hashes.json` 固定各阶段日志。最后 `review-final-*` 验证时段已明确串行；较早的潜在团队时段重叠不作为最终证据。初输入专项缺 `EmbyApiException` 边界类型的编译失败已保留，补仅测试异常类型后重编译；题内 format 退出 0 但有工作区加载提示，未称完整方案空白检查通过。完整主套件与主树整合交付由主代理收尾。
