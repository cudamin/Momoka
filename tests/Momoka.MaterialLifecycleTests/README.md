# WinUI 材质生命周期

完整编译链接生产 `IslandMicaBackdrop`、`HostWindow.Backdrop`、`DetailMenuBackdrop`、`DetailMenuSurface`。WinUI/控制器、窗口目标与主题值为匿名边界；同引用赋值不会重连，默认配置按 target 独立登记。

覆盖不支持、构造/属性/配置/连接失败、false 回执、部分连接后的失败清理、状态读取失败、同值重试、宿主激活快照、系统 Fallback 与连接结果的区别、主/子菜单主题更新和独立释放，以及真实附加属性 Loaded/Unloaded/替换/禁用的控制流。每项结束核对主题订阅归零；Dispose 抛异常只证明释放被尝试及登记清理，不证明 native 资源已释放。

运行 `tools/run-regressions.ps1 -Suite Momoka.MaterialLifecycleTests`。真实系统状态、菜单像素和窗口行为另由匿名 `--probe-shell --probe-section materials` / `InspectDetailMenu` 验证。此项目不创建 HWND、不读取保存账号、不启动播放，不把模拟高对比度称为系统实测。
