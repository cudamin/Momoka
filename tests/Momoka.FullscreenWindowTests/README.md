# Momoka 全屏窗口离线测试

这套测试只验证全屏生命周期的**离线契约**：哪些调用按什么顺序发生、样式位与演示器状态如何收放、
诊断判定在错误几何下是否拒绝。它不创建 HWND、不跑 WinUI、不播媒体、不执行应用自检，因此
**不是真实屏幕验收，也不是原生崩溃/析构验收**。

## 构建与运行

源码片段在构建时从生产文件按精确起止串原样提取。默认指向本工作树，验证交付时必须显式指向主树：

```bash
"$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.FullscreenWindowTests/Momoka.FullscreenWindowTests.csproj \
  -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false \
  -p:FullscreenSource=<主树>/src/Momoka.Shell/Windowing/HostWindow.cs \
  -p:SelfCheckSource=<主树>/src/Momoka.Shell/ShellSelfCheck.Fullscreen.cs \
  -p:NativeSource=<主树>/src/Momoka.Shell/Interop/Native.cs \
  -p:GeometrySource=<主树>/src/Momoka.Core/Playback/AspectLock.cs

"$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.FullscreenWindowTests/Momoka.FullscreenWindowTests.csproj \
  -c Release --no-build --no-restore
```

构建时必须 0 警告 0 错误；运行退出码 0。构建会生成 `obj/.../FullscreenProduction.g.cs` 与
`obj/.../FullscreenSourceEvidence.g.cs`，后者记录四个源文件的 SHA256 与每段提取片段的偏移、长度和
哈希；测试会重新读取源文件核对，防止拿旧产物冒充新源码。

## 覆盖范围

离线覆盖：

- 自定义标题栏进入全屏：先 `ApplyTitleBarMode`（Collapsed、输入区）再
  `OverlappedPresenter.SetBorderAndTitleBar(false, false)`，且不写 `GWL_STYLE`。
- 退出全屏：先 `SetBorderAndTitleBar(true, true)`，再恢复 saved style，再按最新意图
  `ApplyTitleBarMode`，最后还矩形/任务栏。
- 回退边界（NoAppWindow / NoTitleBar / NotExtended / NoNonClient）：仍按样式移除
  `WS_CAPTION|WS_THICKFRAME`，并且从不调用 `SetBorderAndTitleBar`。
- 多轮进出后 `PresenterChrome` 每轮回到 `(true, true)`；演示器桩拒绝
  `Border=false, TitleBar=true` 的非法组合。
- 诊断谓词 `FullscreenFrameMatches` 的正例/负例：frame 与 client 四边、整体偏移、少 7px、
  非正显示器尺寸、非零标题栏 metrics、演示器残留边框/标题栏、`WsCaption` 掩码为 0、回退模式忽略
  演示器与样式但几何仍须全覆盖。
- 既有生命周期：意图不被全屏改写、退出按最新意图、最大化/浏览几何恢复、z-order 意图、幂等、
  零句柄、异常 finally 恢复 FreeSizing、Configure 失败回退。

**仅由真实运行自检覆盖**（本套离线测试刻意不假造）：

- `OverlappedPresenter.SetBorderAndTitleBar` 在真实窗口上的视觉效果。
- `WM_NCCALCSIZE` / `HostWindow.FitFullscreenClient` 是否真的把客户区扩到整扇窗口、是否消除
  客户区内缩 8px。`RECT`/`NCCALCSIZE_PARAMS` 指针无法用离线桩有意义地模拟。
- `SetWindowPos` 后 DWM 实际报告的 frame/client 几何、任务栏 z 序、圆角与原生标题栏是否被析构。
- 原生崩溃、ComCtl/AppWindow 生命周期问题。

这些请以 `ShellSelfCheck.Fullscreen.cs` 的真实窗口读数与 Windows 崩溃转储为准。
