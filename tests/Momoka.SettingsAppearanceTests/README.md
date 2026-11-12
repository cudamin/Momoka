# 设置窗口系统外观

完整编译链接生产 `SettingsWindow.Appearance.cs`，执行系统状态选择、事件派发/合并、显示期降级轮询及停止清理。OS、dispatcher、timer和窗口事件是匿名边界；caption仅记录调用和真实选色结果，不代替生产标题栏像素验证。

覆盖普通 Mica 画布 `0x99`、关闭透明/高对比实色、系统配色变化、非 UI 通知排队、拒绝/异常后重新派发、关闭后迟到回调、缓存显示隐藏、缺事件降级和部分订阅失败。每项结束检查事件与 timer 归零。

运行 `tools/run-regressions.ps1 -Suite Momoka.SettingsAppearanceTests`。设置页草稿/活动期另由 SettingsLifetimeTests 覆盖；真实系统开关、五套主题、同窗恢复及截图另运行匿名窗口。此项目不访问生产配置、不改变 Windows 设置、不创建 HWND、不播放。
