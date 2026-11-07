# R05-002 匿名真实控件探针

入口：当前 x64 Release 的 `Momoka.exe --probe-shell --probe-section plex-state --screen 1`。该分区仅执行本项，不运行整个 Library 分区；全量 Shell 探针也登记本项。

本探针使用独立运行目录、`.invalid` 内存 HTTP、临时假设置和假身份恢复，不读取真实账号、凭据或服务器。真实 ShellPage 仅挂载通知与内容 Frame，没有 Attach 到正常播放服务；动作代理拒绝播放、外部程序、账号切换和其他管理操作，只将通知交给正式 Toast。详情沿正式 Frame 导航、Attach、Reload、生成命令和真实 InfoBar；初次详情必须成功，命令主体必须就绪，HTTP DTO 必须与导航种子独立。

## 场景和证据

- 真实 PosterCard 点击处理器与 ActionRequested：已看／未看确认后补读 503、403；检查确认位、实际已看角标、一次写一次读和正式 Toast。503 为警告，403 保持错误，不重发已确认写。
- 真实电影 DetailPage：1320／760 DIP 两档、两种目标、503／403；检查宽窄版式、正式按钮的生成命令绑定、确认位、按钮文字、真实 InfoBar 与一次写入。确认视图和滚至通知后的实际屏幕分别截图。
- 计数回执 `plex-state-receipt.json` 记录十二个成功场景的读写数、状态、严重度。每张图是当前探针窗口的真实屏幕截图，保留屏外与遮挡守卫。
- `plex-state-card-<503或403>-<True或False>-screen.png`，`plex-state-detail-<1320或760>-<503或403>-<True或False>-confirmed-screen.png` 和对应通知屏幕图。

## 开发核验与待办

专用开发树 `C:/Users/89400/.codex/worktrees/review-plex-state-probe/Momoka`，基点 `29cf082`。新的分区解析测试在旧 Core 有效失败（16 项中 15 过、1 败），添加分区后 16/16，0 失败、0 跳过。证据在本树 `artifacts/review-batch7-probe`。

编译和无窗口分区测试不证明上述控件场景通过。开发期间不启动 GUI，主任务在桌面空闲且独占时串行运行旧产品红测，再运行整合修复代码绿测；发布前还须以主树最终代码重新验证。未执行结果不计通过，不能以文件名或截图存在替代运行判定。

本探针不覆盖真实 Plex 协议、401 实际账号重认证、真实服务器写入、播放、续播失败消费或详情共享字段排序。401 和写失败／取消由本批独立匿名 Session 回归负责。
