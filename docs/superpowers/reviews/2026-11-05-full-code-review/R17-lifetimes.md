# R17 运行、报告与资源边界

当前树15c4204；本轮不修改产品，不向桌面注入，不访问真实账号。源码、临时受控观察、正式离线入口、实际UI/原生覆盖分列。

| 对象 | 创建与归属 | 成功/失败收尾与本轮处置 |
| --- | --- | --- |
| TestHarness case | 静态列表，由各Register登记；真实Action调用 | 同步失败优先，Skip互斥；async void超出边界见R17-001 |
| StubTransport | 完整收到请求列表_sent、脚本命中计数_asked | Sent含404，Total漏计见R17-002；未知回应不能当零请求 |
| 源提取器 | Compile链接/UsingTask/targets/Python以当前固定输入生成obj | Missing/歧义/hash/越树拒绝；R04110反例实际过；R045/R046显式x64任务AssemblyFile旧位置红保留 |
| 主/专项参数 | 60顶层项目、6辅助调用者、59登记 | LibraryHeader漏登记使整个List/Suite/All先拒绝；主Tests错非空参数退默认见R17-003 |
| Shell/Subtitle报告 | 真实ProbeReports独立Attempt Scope/Main/Dispose/Exit | 正式69含持续IO/取消/inspect ownership过；inspect已执行失败0见R17-007 |
| Composition最终报告 | StreamWriter与handle/page/window | Write异常可跳exit，stop失败跳page/window清理；匿名完整finally确认见R17-004 |
| Cursor/Motion报告 | 原using StreamWriter，90/150秒保险 | 成功码置于最后Write前，失败不能保0；Motion外finally exit，Cursor报告初始化在try外；真实窗口未跑 |
| 局部Shell case | 临时root children、主题/TopMost/窗口尺寸 | ButtonEdges/DetailArtwork/EpisodeBand/Subscriptions/State先日志再清理；Case失败后继续，见R17-004 |
| 临时PlayerPage | 构造StartWatchdog真实System.Threading.Timer，Attach VM/window/Win32 keys | PlayerTransport53–57、Shortcuts365–371漏完整Detach/Shutdown；PlayerSkip209–217、DanmakuSend141–148仅Detach/释放surface未stopwatchdog；Danmaku313–336holder亦仅表面。R17-012 |
| Rendering/Loaded | Performance局部delegate捕获run/page，静态Rendering | 仅callback退订，3s timeout后仍subscription；匿名afterFinally1/afterLate0，sinkDispose忽略late write；R17-012 |
| Diagnostics日志 | sink序列水位＋弱visit＋单pending，Detach结束visit | 正式Perf含69成功行为及两来源测试；默认71过。播放初始标题另R17-014，不混成log缺陷 |
| SelfCheckRun | 真同用户mutex，副本配置和独立目录 | 未读真实源/Secret；普通selfcheck仍可登录真实库，未启动；local3probe未同锁只登记硬隔离缺口 |
| 屏幕像素 | Theme.SampleScreen、Composition.Native capture | 真实Capture有归属/BitBlt/取样；SelfCheck五点仅首点owner使foreign颜色入判据见R17-006；RenderTargetBitmap不是实屏 |
| 指针工具 | poke选第一个Momoka进程、全局输入；cursor-watch只看系统 | 未运行，不把返回坐标当事件送达；句柄归属/落点失败/恢复与GetCursorInfo失败标签需修核，见报告 |

所有真实材质、滚轮/峰值、窗口销毁、HWND输入、实际服务器协议与播放器行为仍有运行缺口。没有根据计时或像素边界替身宣称产品交付成功。

固定主树9ed8d24的53/27差异未接入；新DetailCoverGap的finally也先读数后清理，与本树相同根因。Shader旧摘要在该主树已同步59；R046三pin未同步，本树全匹配不能推主树绿。后来d7bb六方法漂移仅登记。
