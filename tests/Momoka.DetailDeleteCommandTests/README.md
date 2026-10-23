# R025 实际详情删除命令专项

独立正式控制台项目，不进入 shared `Program`；来源worker未改方案，主树整合时已最小加入方案并同步开发入口。产品修复由主线程负责，worker只提供夹具；协议/约定基线未改。只测实际命令因果链，不启动 Momoka 的 `App`、窗口、播放器或真实服务器。

## 真实执行的部分

- 每次编译从当前工作树以唯一首尾标记**原样提取** `FnNasVersionsAsync`、`FnNasDeleteAsync`、`DeleteAsync`、共享 `GuardAsync/AskAsync/TellAsync/Root`、`Invoke` 的真实入口守卫及 `ManageVersions/Delete` 分支、详情 `OnMoreClicked`。缺失/歧义标记直接编译失败；生成文件在 `obj`，不提交、不手改。
- `ConfirmDialog`、`CardItem`、`IShellActions`、`PosterCache` compile-link 当前产品源。版本读实际 `FnNasClient` 的 `GET media/itemfile/item` 和匿名 JSON，写实际 `FnNasClient`/`EmbyClient` 的 DELETE，不复制请求实现。
- 显式加载**本树 freshly built** `src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.dll`，执行真 `DetailViewModel.Attach/Apply/CaptureRemoval/CaptureReload/ReloadAsync/StartAsync`。转交 adapter 只转换卡片/委托类型，不实现删除、过滤或重载规则。旧基点缺 Capture 方法时只转交当时真实 `Remove/Reload`；版本方法反射兼容 5/6 参数，不给旧代码偷偷补新行为。
- 当前共 18 项：last/nonlast；两层取消；HTTP/业务失败拒回调；无根零 HTTP；FnNas/Emby/Jellyfin 主体删除及取消；迟到 DELETE 跨 Attach；正常 GET 失败不是删除；原样详情→命令路由；lateApply；外源 B 选中、同 item/source ID 时只撤 A 的确认删除源。

## 离线边界

- UI enum/值、`ContentDialog.ShowAsync` 及版本选择为替身，真实 `ConfirmDialog` 仍决定同意/取消并检查默认 Close。`Show` 只转交提取的实际 `Invoke`，没有全页渲染或完整菜单 stub。
- 假 handler 无网络备援，仅接 `delete-fixture.invalid` 的明确 GET/DELETE 端点；任何登录、播放、图片、其它服务/路径都失败并累计 forbidden。只有占位身份；秘密保护/读取、设置/账号写入、无关 shell 动作均拒绝。
- 外源场景用真实 `MediaVersion/SourceRow` 装配已发现列表，没有跨服务器查找 HTTP。当前普通刷新故意返回 503，验证刷新失败不能变成删除。
- 不覆盖真实 WinUI 焦点/弹窗布局、全菜单渲染、跨服务器发现与在途 `LoadVersionsAsync` 的真实响应、真实服务器删除/权限、native/播放或 DPAPI。真 VM 方法确实执行，但不能把这些替身证据当成 UI/真实删除验收。

## 运行（PowerShell，当前工作树）

先把 fixture **单独合入主线程已完成产品修复的树**，不要拷旁路临时产品源、PROGRESS 或忽略证据。工作树应已完成各自产品源码整合。

```powershell
$root = (git rev-parse --show-toplevel).Trim().Replace('/', '\')
$fixture = Join-Path $root 'tests\Momoka.DetailDeleteCommandTests'
$shell = Join-Path $root 'src\Momoka.Shell\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Momoka.dll'
& (Join-Path $fixture 'Run-Fixture.ps1') `
    -Dotnet 'C:\Users\89400\.dotnet\dotnet.exe' `
    -Packages 'C:\Users\89400\.nuget\packages' `
    -ShellDll $shell
```

脚本不复制/修改产品，不发布、不 push、不运行全 Core/SelfCheck。它固定缓存 restore，**仅本次 restore** 关闭 `NuGetAudit`，串行 fresh Release build Shell 和 fixture，使用 `--no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false`；构建失败立即停止，不跑旧 DLL。证据只写本树 `artifacts/r025-delete-command-fixture/run-*`，包括命令、构建/测试日志、四产品源哈希及成功构建证明。测试再核 Shell 路径/哈希、四源哈希、Core 同一份载荷与 fixture 哈希。

定向空白检查（只包含本项目手写 C#）：

```powershell
$files = Get-ChildItem -LiteralPath $fixture -Filter '*.cs' -File | ForEach-Object FullName
& 'C:\Users\89400\.dotnet\dotnet.exe' format whitespace `
    (Join-Path $fixture 'Momoka.DetailDeleteCommandTests.csproj') `
    --no-restore --verify-no-changes --include $files
```

## 本轮证据口径

- 基点 `dab7d3c7bf807fc49fdd2999b71d27c6e67a7e65`：相同首 17 项 **10 过、7 败、0 跳过、退出 1**；Shell/fixture 成功构建后才运行。红项来自实际行为，不把初次夹具编译错误当产品红。
- 初次主快照 17 全绿只是中间证据；主线程随后授权追加外源选择修复并重新复制四源，新 fresh build 上原 17 项及外源新增项 **18 全过**。新增外源项未在旧基点运行，不把最终 18 与基点 17 错当同数量红绿。
- 来源是 **source from current main snapshot**，临时四源只做待测输入，不编辑、不提交。最终源哈希、日志及取舍以本树 PROGRESS 和忽略证据为准；历史 R025 records 未改。合入后的最终有效性须由主线程在自己的最终源上再 fresh build/run。

## 主树重验（2026-10-24）

已在最终主树重新运行 Run-Fixture.ps1，当前18项全过；11份相关C#solution Release/x64定向空白初查两份匿名fixture13处初始化布局，定向修后0修改／退出0无加载失败，再编译/生成当前构建证明重跑18项全绿。主树日志 `artifacts/staged-repair/batch05/r025-final-command-tests.log` 及 `r025-command-final-build-proof.json`，产品四源SHA256与worker最终待测快照一致，不拷worker产品源/DLL或旧证据。
