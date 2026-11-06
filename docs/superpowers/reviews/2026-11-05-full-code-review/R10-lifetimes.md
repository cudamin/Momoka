# R10 播放会话与资源所有权

对应HEAD `aa631ff7`，源码哈希见[R10验证](R10-verification.json)。下表是源码责任图；真实窗口、native与服务器行为未运行。

| 异步路径/资源 | 归属和失效条件 | 释放者/边界 |
| --- | --- | --- |
| Shell起播准备、详情/父剧读取 | StartIntent、每次preparation、捕获scope；交Core前再次核对 | 本次Start using/finally；失败恢复缺陷R10-004/005 |
| Core启动、全屏读取、后端建立 | PlayOperation.Startup关联调用者/来源/启动/continuation；latest request拒旧 | PlayAsync finally完成登记并释放CTS；实际已建立Lifetime独立 |
| 当前播放、初始轨道读取 | PlayingAttempt引用/generation、track revision；快照2秒截止 | PlayOne finally退订、释放或移交handle；初始报告顺序R10-006 |
| 状态/轨道/宿主/原生消息 | attempt当前且非Ending；外发PlaybackUpdate包含service generation | attempt.Unsubscribe；VM Connect订阅、Shutdown退订，UI落地再验 |
| 周期进度、退出任务 | attempt/Lifetime；退出后不继续timer写入 | Monitor finally标Ending并退订，timer using；最终报告由Finish |
| 暂停位置读取 | 当前attempt、3秒截止；同场事件缺独立顺序 | 每回调自行结束，Reports finally释放；R10-007 |
| 阈值停止 | ThresholdPending、Reports、StopReported | finally清pending/释放semaphore；成功后停止后续进度/停止重复 |
| 最终停止与已看 | attempt Reports、原捕获scope；报告各有5秒截止 | Finish finally，随后PlayOne清理；超时不等于服务端未写 |
| 手动已看补报 | 目标scope；manualReports准入；活动片借attempt，否则独立start/stop | 自身finally完成登记；Shutdown关闭准入并等待已登记任务 |
| EOF retained handle | RetainedPlayback的handle+generation+lease；Claimed/ContextClosed分离 | 消费或Release注销来源回调，lease Lazy任务只释放一次 |
| lease迟释放 | 锁前后复查holder；后继消费使旧等待失效 | ReleaseContinuationAsync停/释放并finally放gate；不等待后继整片 |
| 自动下一集递归 | 原caller attempt/intent、独立lease token/expected lease | await-using由内向外释放；最后hold=0才Leave/idle；R10-001/002/004 |
| 手动边界选集GET | episodeLookup对象、token、activeIntent、PlayingItemKey | Cancel先摘owner再取消，本次using释放，旧finally不清新owner |
| 跨来源版本发现 | versionLoad、VM generation、discovery、NowPlaying对象 | 新读取消旧读，finally只摘自己的owner；选择时Capture再次验来源 |
| 同场换版本 | versionSwitchTarget对象、捕获scope、Start生命周期 | NowPlaying清换版守卫；finally仅清自己的对象；R10-002/003 |
| 封面/双解码 | coverGeneration、owner token、scope lifetime和最终可用性 | ReadCoverBackdrop finally Dispose；页面视觉交R16 |
| 音量请求及保存 | handle/service generation、VolumeMemory revision和独立命令队列 | gate/finally与独立保存循环；人工Swap反例尚无独占真实入口 |
| 音频延迟/着色器事务 | 活动attempt、handle、service generation、native FileGeneration | 写和回滚固定原代次，各自semaphore finally；完整语义交R12 |
| 字幕外观/通用属性命令 | revision或native单命令代次；多await文件事务需专项复核 | 各自方法/native gate；交R11/R13，未据窄时序直接报错 |
| 退出抓帧/覆盖层 | presentation generation、窗口句柄、InteractionContext | ReturnToBrowseBeforeStopAsync finally释放；现有3例仅几何/等待 |
| PlayerPage Attach/Detach | presentation generation、Attached/inputSuspended | Detach退VM/窗口订阅并清shell/window；Shutdown释放视频表面 |
| 应用退出 | Shell/VM shutdown task、关闭准入、VM lifetime | App保留dispatcher/session等待，25秒外层期限，最终设置Flush |

R10只确定共享生命周期和交接；R11接native句柄/IPC/uosc/窗口输入，R12接画质/音频选项，R13接字幕翻译，R14接弹幕客户端排空。各专项必须沿这里的场次与资源所有者检查迟到结果，不以“同一个句柄”替代“同一个文件”。
