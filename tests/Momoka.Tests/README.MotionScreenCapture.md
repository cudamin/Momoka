# 原生动效实际ROI采屏回归

`motion-probe-fixture` 逐字编译当前 `CompositionPlaybackProbe.CaptureMotionFrame`、客户区定位和遮挡守卫，只有Win32矩形、BitBlt与PNG编码端点使用匿名替身；真实屏幕和动画仍由主树原生检查验证。本轮44/44、失败0、跳过0。

Rendering先发布不可变的原客户区像素区域，后台每次开始时固定该区域；未有ROI时仍采完整客户区，但不猜绿色标记。已发布ROI只拷贝实际区域，端点均包含，例如76..204、12..232为129×221，而非128×220。每帧保留完整客户区屏幕原点/尺寸、实际ROI、PNG宽高和测量时间；`capturedRegions`逐帧写出，`nativeIntermediateCrop`与`retainedFailureCrop`对应同一份实际PNG。PNG是屏幕裁切，没有拼回整窗或合成背景。

读数平移回原客户区像素，仍保留±1位置、颜色/面积、800ms/16ms、真实关闭调度与三帧落定判据。回归覆盖同帧整窗/裁切读数一致、负屏幕坐标、单像素含端点、空帧原哨兵、区域外绿色噪声、整窗及ROI遮挡、客户区移动/缩放、错误尺寸/越界/溢出和同byte[]编码。本轮不执行GDI/窗口输入；像素量降至约4%不等于已测得同等耗时提升。

先按仓库标准单节点Release编译当前Tests与Core，再运行：

```powershell
& "$HOME/.dotnet/dotnet.exe" run --project tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- motion-probe-fixture
```
