# 跟随系统主题回归

标准入口：`tools/run-regressions.ps1 -Suite Momoka.SystemThemeTests`。

编译链接当前生产 `ThemeHost`、`SystemThemeWatcher`、`InputControlTheme` 和 `SystemManagedThemeRole`，使用真实 Core 主题模型。覆盖启动读取、稳定 system ID、根元素与主题角色同步、高对比保留、后台通知合并、UI 队列拒绝与读取失败恢复、切回固定主题后的解绑和旧回调失效，以及弹窗跟随与播放信息局部深色。

`UiBoundaries.cs` 仅提供资源、元素、画刷、UISettings 通知和 DispatcherQueue 的内存替身。测试不创建 WinUI 窗口，不切换真实 Windows 应用模式，不访问账号、服务器或媒体；通过不代表真实系统通知、窗口材质、模板资源查找或屏幕颜色已经验证。
