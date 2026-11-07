# 媒体修复匿名界面验收补件

父线程已确认范围：新增两个 metadata 用例，更新一个 favorites 用例。复用现有控件、布局及 UIA 调用，不设计或修改产品界面。

1. `ShellNavigationProbe.MediaRecovery.cs` 使用内存委托操作 Identify 搜索/选择/保存与移除；确认失败留稿、在飞关闭受保护、成功只确认一次及关闭后迟到结果拒绝。
2. 同文件操作 FnNasMetadata 真实保存/关闭按钮；确认失败留输入、原始 JSON 不变，在飞关闭受保护，确认后才关闭。
3. `Favorites.DeleteDetailCardAsync` 同步主体删除要求：当前主体、播放目标、标题链接及货架清空，保留明确删除通知。
4. MediaDialogRecovery 编译链接完整新增探针体，仅控件、ShowAsync、截图与布局为无窗口边界；正常流程与 pending 截图失败均执行，确认 finally 结清在飞保存。编译 Shell 和该专项后交父线程。

真实 GUI、截图与整体分区由父线程在最终代码串行运行；本代理不启动 UI、真实客户端或播放器，不提交服务器写入。Artwork 64MiB 已由原样 PickAsync 无窗口拒绝测试覆盖，不扩大到原生 picker。

完成证据：本树当前 Core/Shell 与 MediaDialogRecovery Release/x64 构建0警告0错误，完整该专项12/12通过、0失败0跳过；新增探针体2种正常及2种pending截图失败均实际执行。9个文本文件的行尾/BOM/空白检查及专项定向format通过。独立代理只读复核真实按钮入口与finally结清路径，在本轮范围内未发现需要处理的缺陷。

过程限制与红证据：第一次Shell构建新增NoticeMessage nullable警告已定点修正，第二构建0/0；第一次format4项空白红已按具体位置修正再验证。日志保留于 `artifacts/media-ui-recovery`。没有跑该树完整主测试、真实GUI或媒体服务器；两个metadata用例、Favorites主体删除断言及所列截图仍待父线程最终合流程序运行。
