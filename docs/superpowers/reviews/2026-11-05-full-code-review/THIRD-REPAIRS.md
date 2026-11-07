# 第三批迁移与章节跳过修复

用户授权自行挑选必要修复并连续执行。隔离基点6b9fb3c，主树轮播已由其聊天完成交付并明确释放。本批仅处理R02-002和R10-008；历史133项不是当前剩余问题数量。

| 问题 | 修复行为 | 当前证据 |
| --- | --- | --- |
| R02-002 / 3a93774 | 主配置先完整复制到同目录临时文件，再不覆盖地移动提交；主文件失败继续候选，不带入失败来源备份/缓存；附属复制失败不撤销已提交主配置。 | 最终182项原4败，当前182/182；全部使用真实临时目录与非秘密占位数据。 |
| R10-008 / b61fd63 | 删除“片头片尾类型齐全”的提前返回，按现有同类区间重叠规则补章节；服务器重叠区间及已接受跳过决定保持。 | 逐字提取实际VM方法59项原4败，当前59/59；明确片头/片尾、未知时长、空表、重复与迟到更新。 |

## 来源校验

R046原本在编译前拒绝四项历史pin漂移，原拒绝日志保留。与旧hash完全相等的Git源码已定位并读完差异：IPlaybackBackend/LibMpvBackend/HandleFixture旧源d2febcd，差异1dcb33b的可选集成批量切链及31d82c7格式；EmbySession旧源3ae8878，差异5c67fcb的登录绑定保护与a271874的同服恢复确认绑定。这些均为已交付提交，原取消/身份/代次边界保留；caller不执行这些原生批量或登录恢复方法。

仅定点更新这四项与本批Events，共五项；其余14项及原基点/日期元数据不改。10个提取拒绝反例全部预期退出1，随后当前源重新零警告错误编译并实际59/59通过。未把负例退出1当作产品错误，也未弱化来源守卫。审计收据artifacts/review-batch3/source-pin-audit.json。

## 验证边界与记录

原样主Tests基线3996/3996。设置第一次修后181/182的失败来自匿名文档未写schema，补明确schema27后最终原样产品再次178/182、修后182/182，初次失败不删。所有红绿和来源拒绝日志保留在artifacts/review-batch3；夹具构建、结果、语法跨度与10个反例在artifacts/r046-caller/batch3-*。

Shell/Tests最终Release/x64零警告错误；独立复审无必要发现。完整主Tests4000项3984过16败，16项均由另一聊天UI探针持有共享自检锁导致；原失败日志final-full.log保留，未改守卫、跳过或降级。固定缓存还原已解决相对packageFolders导致的NETSDK1064；生成缓存保留在ignored证据目录，未改包版本。上述为整合前的历史阶段；最终主树验证与交付已完成，见下。两条实际状态/异步章节入口共用修复方法经源码核对；夹具的入口邻接操作为替身，不证明真实mpv章节到达、WinUI按钮或实际跳转。未使用真实配置/凭据，未实服播放或写入，未上传；版本保持0.2.5.0。

## 主树最终交付（2026-11-07）

产品合入提交d2f1232，保留3a93774与b61fd63；PROGRESS冲突逐块保留双方历史。主树Core/Tests/Shell Release/x64零警告错误，完整主Tests4000/4000、设置182/182、实际章节调用夹具59/59全部通过，零失败零跳过；10个来源提取负例全部拒绝后，当前源码再次59/59。

发布调试参数为DebugSymbols=false、DebugType=None，首次交付Core与开发测试Core不同；使用相同参数重新编译主Tests，Core逐字节相等后再跑完整4000/4000。未复制DLL或放宽守卫；两份完整通过及原16项共享mutex红均保留。证据artifacts/review-batch3-master，最终full为published-core-full-tests.log。

实际交付C:/Users/89400/Momoka/artifacts/publish/win-x64/Momoka.exe，0.2.5.0；546文件、300297671字节（发布工具286.4MB），批准载荷与许可全过。桌面Momoka.lnk实际指向它、参数为空。Momoka.dll SHA256为C2F2CE335ED5B51A63B0A2C714439AA8BB94643EAC1A885860E2FDE6245EE630；Core为8BB7CCBDA2349B186CDEAED500F0CDF49B24EABD4069C18DF859CB7F36AEB564。未生成新ZIP/MSIX或上传，未实服播放/写入，实际章节到达与WinUI跳过未执行。

415份隔离树日志/原样来源/红绿已保存至artifacts/review-batch3-master/preserved，逐文件SHA一致。临时树997e122已全进入master且干净；原生归档拒绝当前聊天主工作树（The task's primary checkout cannot be removed.），因此保留C:/Users/89400/.codex/worktrees/review-recovery-batch3/Momoka。主要证据为delivery-receipt.json、publish-config-source.json、preservation-receipt.json、publish.log与各专项日志。

本批没有未交付产品。主树写入、测试与发布已结束；其他聊天按现行规则接回主树并重验其新改动。后续候选R05-004（Plex码率单位）与R05-005（剧/季观看统计）仅只读复核，未纳本批或修复；不据历史133项推断当前剩余数量。
