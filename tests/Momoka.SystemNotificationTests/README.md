# Windows 系统通知专项

在仓库根目录用 `global.json` 选择的 .NET SDK 运行。需要支持 Windows 通知的普通权限桌面会话。两个测试各发一条匿名通知，用 Windows 通知中心回读唯一匹配的通知 ID，再清除本次记录和随机测试身份。测试占用项目的共享桌面诊断锁。

```powershell
$project = 'tests/Momoka.SystemNotificationTests/Momoka.SystemNotificationTests.csproj'
$output = 'artifacts/system-notification-tests'
dotnet publish $project -c Release --self-contained true -p:WindowsAppSDKSelfContained=true --artifacts-path "$output/obj-self-contained" -o "$output/self-contained"
if ($LASTEXITCODE -eq 0) { & "$output/self-contained/MomokaSystemNotificationProbe.exe" --send-self-contained }
dotnet publish $project -c Release --self-contained false -p:WindowsAppSDKSelfContained=false --artifacts-path "$output/obj-framework-dependent" -o "$output/framework-dependent"
if ($LASTEXITCODE -eq 0) { & "$output/framework-dependent/MomokaSystemNotificationProbe.exe" --send-framework-dependent }
```

框架依赖检查需要已安装兼容的 Windows App Runtime。程序仅在指定与发布形态匹配的参数后运行通知验收。退出码 0 表示收件与清理成功，1 表示检查失败，2 表示入口参数无效，3 表示桌面被占用或当前会话不允许通知。该专项覆盖 Windows 通知中心投递，横幅显示、点击激活和商店安装包须另外验证。
