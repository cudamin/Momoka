# P6 动效系统观察异常清理

日期：2026-11-13。独立树：`codex/winui-p6-reliability`，起点为根整合版本`8eee129b65fa2fea9275e69809f5616e0ee4b6db`。此项来自P8只读审查，不在本机声称实际发生了系统事件注册故障。

## 有条件缺口与修复

`SurfaceMotion.Play`原先在`Storyboard.Begin`的try之前调用`WatchPreferences`；`GlyphPulse.PulseMotion.Start`也在try前注册`UISettings.AnimationsEnabledChanged`。若原生事件注册抛异常，无法进入既有Begin失败落定路径，留下本地`_board`/Active和应用偏好订阅。HomeMotion的Start由外部try覆盖，但其Stop直接退订原生事件，退订异常会挡住Storyboard停止和最终可读状态。

现在Surface/Glyph把注册纳入同一启动try，任何阶段失败共用正式Finish/Stop落定。三者停止时单独捕获原生退订异常，先撤本地记录、应用订阅/完成订阅，再继续停止Storyboard和写终值；HomeMotion用finally保留Stop尝试。旧系统通知按代次、Active归属及`_stopped`拒绝，不能影响下一次启动。

原生remove在执行前失败意味着观察者释放未知，本地异常隔离不等于原生释放成功；无法将这种情形写为“零泄漏”。修复保证页面/动作可用、当地时钟终止、终值和后续重复调用正确，报告仍保留此失败边界。

## 原样生产测试

UiMotionTests继续原样compile-link三动效文件。假UISettings新增注册前/注册后、退订前/退订后抛错；12项分别覆盖Home/Surface/Glyph的失败终值、空本地动画、落定回调一次、再次启动及迟到完成/系统通知。注册后故障仍尝试移除已登记事件；退订前故障明确断言剩1个模拟原生事件handle、退订后剩0。测试在验证以后只结束这个假事件边界，没有修复产品状态或宣称原生释放成功。

当前Core/Shell/专项Release编译零警告零错误，46项通过、0失败、0跳过。本子任务没有桌面、真实服务、系统设置变化或播放；最终主树Shell编译与实际原生动效/输入由主控串行验证。

```powershell
./tools/run-regressions.ps1 -Suite Momoka.UiMotionTests
```

此可靠性修复不新增动效、不改变当前时长/轨迹，不补称尚未实施的ConnectedAnimation，也不替代完整键盘/Narrator/DPI验收。
