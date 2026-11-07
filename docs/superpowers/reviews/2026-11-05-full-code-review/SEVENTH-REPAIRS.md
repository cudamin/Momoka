# 第七批 Plex 写后确认与验证台账

基点29cf082；本批仅处理 R05-002。用户已授权自行选择必要修复并连续完成。产品提交9bf59ff，开发树review-recovery-batch3，匿名控件探针独立review-plex-state-probe。

## 修复

- Plex原HTTP写任务成功后返回null，明确只确认目标位；metadata用户状态独立读取，补读401只重读，写本身401仍按原认证重试。
- 卡片复用既有Patch、RefreshConfirmed、Complete，确认位不被过期补读推翻，统计仍从metadata取得；503刷新失败为警告，403及协议失败为错误。
- 详情先保留确认位，再独立刷新，沿现有“观看状态已保存，但刷新详情失败”反馈；没有重构详情共享字段排序。
- show/season计数回归改为确认写后显式GET，保留未看数0/2的断言。没有伪造完整UserData或改HTTP正文确认语义。

## 开发验证

- Tests/Core/Shell与PlexAffordance Release/x64编译零警告错误。
- 旧代码协议228项有4项有效红，卡片/详情77项有8项有效红；修后协议238/238、实际卡片/Attach-Reload详情81/81、既有状态排序73/73通过。
- 完整4119/4119零失败零跳过。首轮16个自检/输出失败均报告“已有当前用户的自检运行中”，另一聊天占用共享运行锁；原日志保留，锁释放后完整重跑通过。
- 定向Core、Shell、主测试、PlexAffordance空白校验与13变更文本行尾/BOM检查通过，相关文档相对链接通过。
- 新401用例执行实际匿名Session的重新认证；仅占位凭据、内存handler和本树临时配置，不访问真实服务。
- 一次独立只读产品复审零必要发现。取消重入测试覆盖取消拒绝及随后重新就绪，没有把它声称为旧响应悬停至新访问的完整排队竞态。

各专项与完整测试有重叠，计数不累加。证据artifacts/review-batch7。

## 在途与边界

匿名WinUI探针、最终主树重验及实际发布仍在途，不声明交付完成。主树a6eea28已整合到开发树，以保留其它聊天刚落地的详情布局；主树和桌面写入仍由原交付聊天结束后接回。

不读取真实设置/凭据，不进行真实服务器登录、写入、播放、Finish/EOF或上传。ResumeSlot原有NextUp读取失败消费策略不在本批；不宣称真实续播卡片在读取失败时保留。无新版本、依赖、ZIP或MSIX。
## 主树最终交付完成

- 产品9bf59ff、探针f1dbff2与前件修正7a23432整合，保留主树已交付a6eea28详情布局；最后产品/验证提交5a7b9e0。
- 主树当前Release/x64 Tests/Core/Shell和专项零警告错误，完整4121/4121、零失败零跳过；Plex协议238/238、实际卡片/详情81/81。5a7b9e0重新编译后完整4121再次通过，不用旧Git元数据字节冒充最终程序集。
- 旧产品真实WinUI有效红PID2560：确认写后补读503，卡片丢失状态。此前Hidden宿主不可见、假探测遗漏/sys/version和运行锁拒绝均仅前件失败，原日志保留，不作为有效产品红。
- 主树WinUI PID18972与实际发布PID28496各十二场景通过、二十张实屏；1320/760详情、两目标、503/403状态及Toast/InfoBar实测，实际发布图已目视。仅plex-state分区，其余175项未执行，不称全量探针。
- 首次主树GUI四卡片通过后，详情文本定位把Button模板额外TextBlock算入；5a7b9e0改为直接StackPanel内容定位，保留状态文字/可见性断言，重建重验全绿。没有为通过而移除实际绑定检查。
- publish重新生成Shell：发布Core仍与最终完整测试Core一致；发布Shell与当前Shell一致，针对该最终Shell再跑81/81及发布程序十二场景，字节变化不直接沿用发布前GUI报告。
- 发布版本0.2.5.0，546文件、300333951字节（286.4MB）；载荷、14GLSL/4HOOK、mpv清单及逐许可验证通过。桌面Momoka.lnk目标/工作目录为主树artifacts/publish/win-x64，参数空。
- Core SHA256：35F94BCDC40594899BEE5EF4AD55A3E8AFACA40604B536D007CB155EF958F65F；发布Shell：055AC8ABB80A8206351A6B7E9D2B66F635668EF4034D19A1F5CF65934243C64C。
- 22变更文本和定向空白通过，89文档相对链接通过。62份初始开发证据逐SHA保存；最终19份探针开发证据另有回执，GUI/原红/最后日志在artifacts/review-batch7-master。
- 用户关闭桌面程序后，另聊天inspect残留无窗口PID24320仍占锁；经用户明确允许仅结束该诊断，随后验证，未擅自结束用户进程。

专用review-plex-state-probe工作树干净、题内内容已整合或被主树最终定位修正取代，19份开发证据逐SHA保存；原生归档成功，附件已变archived_worktree。当前聊天primary树保留。当前没有本批待合或待交付产品；最终回执delivery-receipt.json、tested-source-final.json及gui-published/logs。最终文档提交不改产品，不因此重建/重发。无真实Plex登录/服务器写入/播放/Finish/EOF、无新ZIP/MSIX或上传。

下一候选只读确认R04-003仍在：EmbyClient把groupItems=false编码成null，Jellyfin最新剧集明确调用false而遗漏参数。本批未修；R04-002已修不重复，旧133项不作为当前剩余数量。不扩大为全库复审。
