# R022 飞牛用户默认权限真实 VM 离线回归

本专项直接编译链接当前树的 `FnNasUsersViewModel`、`FnNasSessionViewModel`、`FnNasAdminViewModel`、`PageViewModel`、`FnNasAdminRows` 和真实 Core；使用 Shell 已钉的 `CommunityToolkit.Mvvm` 8.4.2 生成属性与命令。仅 `Visibility`／`InfoBarSeverity` 两个展示枚举是替身，没有复制权限模型、会话规则或 HTTP 写入逻辑，也没有泛框架控件 stub。

## 当前树命令

从当前工作树根执行；显式传入本树绝对路径，夹具拒绝其他树输出。子代理未改共享入口；主树整合时已最小加入 solution 并同步开发命令。来源树记录不代替整合后当前验证。

```powershell
$tree = (Get-Location).Path
$sdk = 'C:\Users\89400\.dotnet\dotnet.exe'
$project = Join-Path $tree 'tests/Momoka.FnNasDefaultPermissionTests/Momoka.FnNasDefaultPermissionTests.csproj'
$env:DOTNET_CLI_HOME = Join-Path $tree 'artifacts/r022-default-permission/dotnet-home'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:Configuration = 'Release'
& $sdk restore $project --source 'C:\Users\89400\.nuget\packages' --disable-parallel -m:1 -p:BuildInParallel=false -p:NuGetAudit=false
& $sdk build $project -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
# 仅当前 Release 编译成功后执行；编译失败不能拿旧 DLL 当结果。
& $sdk run --project $project -c Release --no-build -- $tree
& $sdk format whitespace $project --verify-no-changes --no-restore --include (Join-Path $tree 'tests/Momoka.FnNasDefaultPermissionTests/Program.cs') (Join-Path $tree 'tests/Momoka.FnNasDefaultPermissionTests/UiEnums.cs') (Join-Path $tree 'src/Momoka.Shell/ViewModels/FnNasUsersViewModel.cs')
```

首次还原只用既有固定包缓存，缺包必须失败，不改源、不升级包。`NuGetAudit=false` 只用于该次完全离线 restore，不写项目或永久关闭审计。产物／命令日志只放当前树 `bin`／`obj`／`artifacts/r022-default-permission`。

## 覆盖与规则

- 库选项和默认模板各自有本次读取成功标记；两项均成功且模板权限有效，才允许新增／默认权限编辑与保存。库成功而模板失败、模板成功而库失败、相反在途顺序均验证实际零写。
- 首次未读、刷新失败、同 VM 同／不同 Source 重附加失败必须保持未知权限 `-1`，不解读成服务器的零权限；清旧模板／库名／输入与旧来源状态。只有成功读回的 `0` 才是无权限，合法 `2`／`1` 继续保持原语义。
- 直接调用生成命令背后的真实方法、绕过 CanExecute 或伪造 Editing 仍不得提交；读取恢复后实际默认权限写一次。真实客户端的管理员 GET 重核不替代模板门控。
- 未知 JSON 对象／数组／null 字段、未知库 id 和集合实例保留；模板写仍绑定捕获的原 scope，切默认来源不迁移到另一台服务器。
- 保存失败保持草稿与可返回状态；显式重试一次才追加一次请求。保存成功后的列表读取失败不重复提交。离页与在途重附加沿用真实 Session／Page 取消代次，旧读／旧写完成不填新来源缓存。
- 页面源码契约只检查两个入口的 OneWay 显隐与原进度／错误／刷新接线；权限值变化同步 CanExecute 通知。失败期间隐藏不可执行入口，保留错误和刷新恢复；过程 Busy 不等于不适用。

显式刷新开始新一轮读取并关闭旧编辑器，草稿不是当前模板；读取失败后的恢复必须重新读取两项。普通保存失败不触发此重置，取消再打开仍以已读模板为准。模板缺失／非法 `media_permission` 保守报错并保留刷新，不猜权限值。

## 安全边界与缺口

匿名内存会话和占位 scope 通过反射安装在真实 `EmbySession` 中，无 SignIn／认证恢复 API／DPAPI／凭据或 settings IO。保护器被调用即失败；所有请求只走假 handler，拒绝未登记来源、非指定协议、auth endpoints 和未登记写接口，拒绝事件本身也导致失败。暂存位置仅本树，没有真实 HTTP 后备路径。

只接受测试中明确登记的假默认权限写／假用户创建并断言次数和字段。没有真实服务器调权、用户创建、密码流程、R024 用户状态写回核验、App／WinUI 控件／native／播放，也不证明真实服务端覆盖还是 merge 语义。无完整 Core／SelfCheck 或用户级 mutex；主线程负责限定整合后重验。真实布局、截图、点击和服务端兼容仍是运行覆盖缺口；本专项成功不等于产品完整交付或发布。

基点修前有效回归 29 项为 10 过／19 败／0 跳过／退出 1；首修 29 全过。扩展显隐／标记／通知边界后 36 项首跑 35 过／1 败，补通知后 36 全过；详细当前日志与交回记录见本树 [PROGRESS](../../PROGRESS.md)。不改历史 R022 records 或基线。
