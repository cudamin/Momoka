# 匿名键盘流程工具回归

直接compile-link `ShellNavigationProbe.KeyboardJourneyFixture.cs`，验证正式工具匿名HTTP和最终/焦点/按键读数的成功与拒绝路径。只读两个`.invalid`虚构来源；人工登录固定虚构账号密码，所有其他写入拒绝，没有真实设置、桌面或播放。

```powershell
./tools/run-regressions.ps1 -Suite Momoka.KeyboardJourneyTests
```

此离线测试不创建WinUI窗口，不证明真实Tab/Enter、焦点/peer或框架返回。实际检查台 `InspectKeyboardJourney` 在阶段JSON指定的进程/HWND等待根代理的真实按键；`InspectKeyboardJourneyFailure`用错误字段名测试原生失败路径，预期报红。两条实际运行均由主控串行执行。
