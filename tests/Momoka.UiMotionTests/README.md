# Momoka.UiMotionTests

`SurfaceMotion`（常驻元素/浮层的进出）与 `GlyphPulse`（图标换状态时弹一下）的独立回归。

生产文件按原样链接进本工程，WinUI 类型与时钟由 `UiBoundaries.cs` 提供：

- `Storyboard.Advance(ms)` 推进假时钟；`AutoReverse` 与 `CubicEase` 的 EaseIn/EaseOut 都按生产的语义走；
- `FrameworkElement` 的 `XamlRoot` 可设为 `null`，用来模拟真正离树；
- `HomeMotion` 是可切换的动画偏好替身，与生产的开关同名。

覆盖的是**时间轴与值**：起点、中点、落点、打断续接、重复显示不重播、关掉动画立即落定、
元素自己的 RenderTransform 不被抢占、离树复位、图标脉冲的峰值与回位、连点不越弹越大。
不覆盖真实渲染、合成器上的实际补间、Acrylic 材质在 Opacity 动画下的观感 —— 那些要实机看。

```powershell
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.UiMotionTests/Momoka.UiMotionTests.csproj -c Release
& ./tests/Momoka.UiMotionTests/bin/Release/net10.0/Momoka.UiMotionTests.exe
```
