# R-010 — 共享本地保存欠项与改密页安全重试

本专项执行当前真实 `SettingsStore` / `SettingsService`、完整 `FnNasPasswordViewModel` / `FnNasSessionViewModel` / `PageViewModel` 与固定 `CommunityToolkit.Mvvm 8.4.2` 生成属性和命令。只有枚举、页面调度器与匿名改密确认端点是窄边界。

## 当前运行

从所属树执行，路径必须与 `git rev-parse --show-toplevel` 一致。先将证书生成关闭，使用 `global.json` 固定的 10.0.400 SDK。新项目用固定本机缓存离线 restore，缺包停止；`NuGetAudit=false` 只用于当次离线还原，不改仓库策略。

```powershell
$tree = (Get-Location).Path
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$dotnet = 'C:\Users\89400\.dotnet\dotnet.exe'
$project = Join-Path $tree 'tests\Momoka.SettingsPersistenceTests\Momoka.SettingsPersistenceTests.csproj'
& $dotnet restore $project --source 'C:\Users\89400\.nuget\packages' --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
& $dotnet build $project -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
# 只在当前编译成功且此后未改输入时运行；每次使用新目录，旧日志保留。
& $dotnet run --project $project -c Release --no-build --no-restore -- (Join-Path $tree 'artifacts\r010-local-save\run-current')
```

实现树本次沿用已核过的本树固定 feed 和 `work/r027/n`；命令、退出码与日志保存在 `artifacts/staged-repair/batch05/r010`，不从别树复制 DLL。测试目录只含匿名非秘密文档，不清理真实设置或凭据。`Core` 用 `ConfigurationCore` 编译别名供配置/本地保存类型使用；VM 只能看到本程序集的假 `EmbySession` 确认端点，不能调用真实 Session。`DenySecrets` 两个方法若被调用立即拒绝，每次确认后的实际 Store 写盘使用非秘密字幕字号代表内存变化，服务器与账号列表为空。

## 实际行为覆盖

- 真实文件锁让 Store 原子替换失败，旧主/备字节完整、临时文件收尾；晚建/不同 SettingsService 同读 Store 欠项，后续直接保存或 Service 重试成功才清除。
- 每次尝试通知、订阅退订、坏订阅者不截断后续、不把成功误判失败；通知不持文件锁、旧 catch 不覆盖通知内较新成功；导出偏好不清当前文档欠项，两个独立 Store 不串状态。
- 匿名服务器确认后失败清本次已确认草稿、保后来草稿，并明确“服务器已修改/本地未保存”。连续本地重试只增加真实保存次数，不追加改密确认请求、不修改 Remember、不恢复来源；新草稿仅显式另点保存才提交。
- 正常成功/R-027 草稿文案、生成本地重试命令及属性通知；Busy、不附加、缺窄依赖、未知/非飞牛/撤销/默认切换、显式非默认来源、Cancel/Dispose/重Attach均有执行层守卫。
- BeginLoad 的同步通知离页/重Attach/改默认来源时零旧本地保存；迟到确认仍保共享欠项但不回填旧页，重入能看到欠项；队列跨同源重入和通知乱序只读当前状态，监听不泄漏。
- 实际改密页 XAML 与 code-behind嵌入并检查完整提示/稳定 `x:Name`/OneWay/命令/调度接线。父设置页的实际提取控制流与当前真实 Shell VM另在 `Momoka.SettingsLifetimeTests/SaveStateLifetimeTests.cs` 执行，验证实时同步、隐藏退订、迟到回调、重入及保草稿，不仅匹配文本。

## 同夹具基点记录

固定基点 `9f07bbddbc1795b9e426cb6a25d0650dd60c8d9c` 的两个持久化文件和密码VM以精确 Git blob 写到本树隔离 `baseline-source`，不覆盖产品：

| 文件 | SHA-256 |
| --- | --- |
| SettingsStore.cs | `cd4f505f1e05ccf8a4de0396b4dcede5ac973c1b174f83716bf59f6c7d39a022` |
| SettingsService.cs | `bb68c0b91e17f64145253261ee6aa5a01db98bd7559b33fa828d2219fc1b044e` |
| FnNasPasswordViewModel.cs | `7ef02198fb24e54f56bff68c905b0c165bc9dc61c2bd8ac4192e36e10fcd20ff` |

`PersistenceBaselineSourceRoot` / `PersistencePasswordSource` 仅选择明确旧输入重新编译同一夹具。嵌套 BaselineCore项目不加入正常方案，未给路径即拒绝编译，不 reuse 旧输出。首轮同夹具38项6过32败（退出1），包括根因行为断言失败和旧版缺新增恢复表面造成的反射拒绝/缺通知；**不声称32个都是产品运行反例**。当前首轮38全过/0跳过/退出0；补较新保存结果不被旧Retry失败文案覆盖后最终39项全过，父设置页专项45项（新增6项）和帐号68项全过，各套不相加。当前31个csproj方案Release、Core/Shell零警告错误，完整Core2549全过；Release/x64方案空白0修改/退出0、28份PS编码语法与新交互定位通过。证据仍在本树上述目录，旧红不洗掉。

## 不覆盖

没有执行生产 `ChangeFnNasPasswordAsync` / `ApplyPasswordChange` / 登录、恢复、重认证、vault、令牌/密码持久化、DPAPI或真实服务器。Core程序集虽为真实配置类型加载，生产认证方法零调用，真实Session直Store接线只静态检视；非秘密字号成功落盘不能外推为凭据或改密端到端完成。没有 App/WinUI控件/真实binding消息循环、像素/焦点、native/播放器或真实库，未发布/推送/更新快捷方式。仍不重试已停止的 R-001/R-003/R-005/R-031。
