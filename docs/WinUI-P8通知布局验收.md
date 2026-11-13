# P8 通知页面布局验收等待

日期：2026-11-13。独立树 `winui-p8-notifications-layout`，起点 `14428af`。只修自检读取时机，不改变通知产品、服务操作或基线。

完整自检 `run-ac35d9a8e64241a3bc7a7dd0cb37c200` 记录通知页面1条、已渲染0；原报告保留。
`NotificationsViewModel.ReloadAsync` 更新Entries、ShowEmpty与Subheading后结束加载，IsReady表示HTTP和VM结束，
不证明嵌入HostedFrame刚显示时ListView已经量过；旧VisitNotifications一见Ready就读取Realised并转到下一页。

现在Ready后先对当前NotificationsPage执行UpdateLayout，再读取原Realised。非空列表drawn小于bound时最多另等8个既有500ms拍，
空列表0/0不增加等待。期限到仍保存当前bound/drawn，ReportNotifications仍要求两者相等；没有用可见项数量替换总数，
没有GetOrCreate、扩大缓存、调用刷新或触发Test/Edit/Delete。真正画不出行时仍红，最多增加4秒等待。

`selfcheck-notifications-layout.json` 保留每拍tick、waits、bound/drawn、Page.IsLoaded和实际宽高，
不含通知名称、内容、服务地址或凭据。写入失败保留最终证据失败资格，不阻断原行数检查，也不把等待变成无界。

KeyboardJourneyTests编译时严格提取完整正式VisitNotifications，缺失或重复起止边界即构建失败；
仅UI布局和下一导航为窄边界，并链接真实数字记录器。新增4例核对Ready先于模板、下一拍真正生成行、
未Ready/空页、8拍期满仍保留1/0、证据写入失败保持红；当前46/46、0失败0跳过。
Core/专项和Shell Release构建0警告0错误。首轮提取结束哨兵误用半角冒号，实际源码为全角冒号，
修成真实唯一标记后重新编译通过，失败日志保留。

本子任务不驱动共享桌面、不读写真实通知、不修改共同PROGRESS或发布目录。正式完整自检成功/失败路径和主树交付由主协调会话完成；
离线等待回归不等于通知页面已在实际WinUI通过。
