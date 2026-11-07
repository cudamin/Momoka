# 媒体弹窗恢复专项

当前正式入口共12项。编译链接生产 IdentifyDialog、FnNasMetadataDialog、ArtworkFile 及完整 `ShellNavigationProbe.MediaRecovery.cs`；控件、ShowAsync、截图和布局是无窗口边界，业务委托仅使用内存 Task，没有会话、客户端、设置或媒体写入。

8项原弹窗规则继续检查识别写在飞关闭、关闭搜索迟到结果、失败保反馈、飞牛保稿重试和64MiB选图拒绝。新增4项执行两个真实探针体的正常/在飞截图失败流程，确认失败不能冒成功且 finally 结清保存并关闭。

```powershell
& 'C:\Users\89400\.dotnet\dotnet.exe' build tests/Momoka.MediaDialogRecoveryTests/Momoka.MediaDialogRecoveryTests.csproj -c Release -p:Platform=x64 --no-restore -m:1 -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& 'C:\Users\89400\.dotnet\dotnet.exe' tests/Momoka.MediaDialogRecoveryTests/bin/x64/Release/net10.0/Momoka.MediaDialogRecoveryTests.dll
```

实际GUI由最终交付线程以当前Shell运行 `--probe-shell --probe-section metadata`，本专项不能代替WinUI模态关闭和截图验收。新增两项使用现有真实控件和UIA：Identify搜索/选择/Apply、移除识别，FnNasMetadata主按钮/关闭；对应截图为 `identify-{apply,remove}-{failed,pending}-screen.png` 与 `fnnas-metadata-{failed,pending}-screen.png`。没有真实写入、播放或文件选择器。

Favorites既有详情删除用例同步当前主体删除语义：CurrentItem、PlayTarget、TitleLink及单集货架清空，并显示明确删除通知；没有弱化为仅“不能跳播”。权限拒绝仍由现有实际删除命令专项覆盖，未声称新增两弹窗用例测试权限或真实删除。
