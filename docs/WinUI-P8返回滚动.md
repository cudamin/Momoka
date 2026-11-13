# P8 返回后的正文滚动

日期：2026-11-13。独立树 `winui-p8-return-scroll/Momoka`，起点 `8dbeccd`。当前单元只增加诊断证据，尚未改返回判据或宣布产品修复完成。

最终整合版自检 `run-21d2dde02b5344609b83c191ca2a4095` 的“播放回来那一页”失败：偏移 787 → 刷新后 787 → 收起后 787 → 最终 442。集带仍在正文第一块，失败来自偏移变化；原文案泛称“滚下”，数值实际变小。真实媒体信息十行和主题角色覆盖在同报告通过，不能据这两项掩盖返回失败。

已读实际 `ShellSelfCheck.ReturnFromPlayer`、`DetailPage.ReturnRead`、`ShellPage.ShowPlayer/RefreshActive` 与 `DetailViewModel.LoadAsync/Apply`。工具先保存当前偏移，按正式播放层显示/焦点、原地刷新、收起/恢复三步执行，不启动播放。`KeepScrollThroughReveal` 当前只监听揭示后 600ms 的 ViewChanged；异步 Apply 清空并重填既有货架会改变正文高度，因此需要区分迟到焦点滚动与临时 extent 夹取，不能直接扩大计时窗口当作修复。

新增 `selfcheck-playback-return.json` 逐阶段保存 offset、scrollable/extent/viewport/content 高度、Busy/Ready、恢复目标与已过时间、滚动状态、焦点类型及集数；没有影片名、地址或凭据。返回前、显示播放层、请求刷新、收起播放层及每次 ViewChanged/ExtentChanged/StateChanged、Busy/Ready 变化全部追加，最终读数解除观察。写失败仍留前序内存记录，下一次写不删除早先阶段；报告原失败判据不变。

专项直接链接 `PlaybackReturnTrace`，覆盖全部前后读数、原始偏移不重写、写入失败再重试及正式观察接线/解绑。它不证明真实 WinUI 事件时序。实际 trace 由协调会话同轮串行运行原自检后取得，再决定最小生产修复；未驱动桌面、读取真实账号或播放。

## 原生轨迹后的恢复修复

协调会话 `run-2fce1e53acf14c359fc754be880af384` 显示真实过程：原偏移/max=801；141ms 清空集带时 max=456、Busy=true、集数=0；219ms offset=456；297ms Ready，但原 extent 尚未恢复；328ms content/max 与 offset 回到 801。这次旧逻辑偶然通过，不抹掉原 787→442 失败；数值证明临时 extent 缩小，不能说只因为焦点跳动。

生产恢复保留原偏移目标，滚动请求按当前 ScrollableHeight 夹取，范围恢复时再次落实原目标。除 ViewChanged 外观察 ExtentChanged 与本次 Busy/IsReady；600ms 布局期从真实 Ready 开始，而非网络刷新开始，因此没有把时间数值加大。同 correlation 尚在途且目标未变不重复发请求；完成只认当前 correlation，旧回执不能覆盖新目标。用户滚轮、指针操作、Tab/方向/翻页键或原生 Interaction 立即解除所有观察；Release/真 Unloaded 同样清理。单次定时器到期主动退订，避免旧逻辑无后续 ViewChanged 就长留观察。

严格原样提取实际恢复方法执行，覆盖临时夹取→extent 恢复、加载超过旧 600ms、在途同目标去重/新范围、用户输入放手、重复登记/无效目标、释放后的迟到完成；不存在并行复制算法。真实 WinUI 范围/事件时序与自检稳定性仍由主控当轮复验，离线通过不等于最终交付。原10行媒体信息判据与自检基线保持。
