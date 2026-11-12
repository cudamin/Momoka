# 服务器清单身份、同步通知和分页回归

本项目编译链接本树正式 `ServerListsService`、`ServerListModels`、`ServerListsViewModel`、`PageViewModel`，并引用同树当前 Core。只替换 XAML 的 Visibility/InfoBarSeverity 值与严格的 `.invalid` 匿名 HTTP，不打开窗口、不访问真实账号或设置、不播放。

14项包含：旧未知写/确认后刷新失败/旧读取失败迟到不能污染新账号；Members/Lists Reset 同步离页或切账号后不继续回填；Plex成员/目录空缺页、总数变化、重复页；未声明总数的完整成员读取；原来源撤销不借另一账号执行。

干净整合树的默认命令（用户级 dotnet.exe）：

```powershell
& 'C:\Users\89400\.dotnet\dotnet.exe' restore ./tests/Momoka.ServerListsOwnershipTests/Momoka.ServerListsOwnershipTests.csproj --source 'C:\Users\89400\.nuget\packages' --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
& 'C:\Users\89400\.dotnet\dotnet.exe' build ./tests/Momoka.ServerListsOwnershipTests/Momoka.ServerListsOwnershipTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& 'C:\Users\89400\.dotnet\dotnet.exe' run --project ./tests/Momoka.ServerListsOwnershipTests/Momoka.ServerListsOwnershipTests.csproj -c Release --no-build --no-restore
```

只读审查可传 `-p:ServerListsSourceRoot=<已核固定快照根>`，项目根仍是本测试树，Core和所有链接源均来自同一快照；这不覆盖父树构建产物。快照不是干净整合树的前提，不作为默认路径。

2026-11-12独立树读父正式文件的首个固定快照：Release0警告0错误、14项11过3红，同步Reset取消/切账号后继续Add被实际VM检出。父修复逐次通知后重新检查来源/token/revision后，只替换快照内该正式VM，复编0警告0错误，14/14、0失败0跳过。service SHA256 `586345f4d1960a948c2505d7f7df1192b7595db9c549fb4b343945efd3c23220`；原VM `d0b1e255343e837b13397e4f3007fbab58704d02bf10c316fef348150a844273`，修复VM `3379794b9f41094294bc21736a69de4fa7e5188a118baef5ab1ea43016bc1889`。测试仅证明上述当前实现的匿名生命周期行为，最终整合树需按默认命令重新编译运行；不替代真实WinUI事件/页面截图、PMS、DPAPI、播放或主套验证。
