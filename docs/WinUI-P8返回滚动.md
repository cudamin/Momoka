# P8 返回后的正文滚动

日期：2026-11-13。独立树 `winui-p8-return-scroll/Momoka`，起点 `8dbeccd`。当前单元只增加诊断证据，尚未改返回判据或宣布产品修复完成。

最终整合版自检 `run-21d2dde02b5344609b83c191ca2a4095` 的“播放回来那一页”失败：偏移 787 → 刷新后 787 → 收起后 787 → 最终 442。集带仍在正文第一块，失败来自偏移变化；原文案泛称“滚下”，数值实际变小。真实媒体信息十行和主题角色覆盖在同报告通过，不能据这两项掩盖返回失败。

已读实际 `ShellSelfCheck.ReturnFromPlayer`、`DetailPage.ReturnRead`、`ShellPage.ShowPlayer/RefreshActive` 与 `DetailViewModel.LoadAsync/Apply`。工具先保存当前偏移，按正式播放层显示/焦点、原地刷新、收起/恢复三步执行，不启动播放。`KeepScrollThroughReveal` 当前只监听揭示后 600ms 的 ViewChanged；异步 Apply 清空并重填既有货架会改变正文高度，因此需要区分迟到焦点滚动与临时 extent 夹取，不能直接扩大计时窗口当作修复。

新增 `selfcheck-playback-return.json` 逐阶段保存 offset、scrollable/extent/viewport/content 高度、Busy/Ready、恢复目标与已过时间、滚动状态、焦点类型及集数；没有影片名、地址或凭据。返回前、显示播放层、请求刷新、收起播放层及每次 ViewChanged/ExtentChanged/StateChanged、Busy/Ready 变化全部追加，最终读数解除观察。写失败仍留前序内存记录，下一次写不删除早先阶段；报告原失败判据不变。

专项直接链接 `PlaybackReturnTrace`，覆盖全部前后读数、原始偏移不重写、写入失败再重试及正式观察接线/解绑。它不证明真实 WinUI 事件时序。实际 trace 由协调会话同轮串行运行原自检后取得，再决定最小生产修复；未驱动桌面、读取真实账号或播放。
