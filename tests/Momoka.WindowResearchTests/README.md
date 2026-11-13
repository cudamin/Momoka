# P7 隔离窗口生命周期回归

原样链接 `src/Momoka.Shell/Windowing/FrameworkWindowResearch.cs`；Window/AppWindow 是事件与调用结果边界，不存在 HWND、真实激活、用户配置或播放。

6项覆盖草稿/内容保留、用户Close取消并隐藏、Dispose真正关闭、重复调用、订阅解除、Activate/Hide/Close失败保持正确状态与重试、延迟Closed不提前释放。

先以用户级 SDK restore，按 Release、单节点、禁共享编译与nodeReuse构建，然后运行本工程。2026-11-13当前6通过、0失败、0跳过，构建0警告0错误。

真实双宿主研究入口在 `--probe-shell inspect` 的 `InspectWindowHostResearch` 和 `InspectWindowHostFailure`；主会话须同轮运行成功与注入异常路径，核对报告/截图及关闭清理。本夹具不证明真实框架窗口、mpv合成、HDR、不同DPI显示器、Narrator或主宿主迁移兼容性。
