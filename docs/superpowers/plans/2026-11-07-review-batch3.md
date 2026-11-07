# 第三批恢复与章节跳过修复计划

> 执行：使用 executing-plans 连续实施，保留每项红绿证据，最后独立复审。用户已授权自行选择必要修复、不自动暂停。

**目标：** 修复仍存在的 R02-002 与 R10-008，验证后合入主树并刷新本地交付。

**设计：** 旧目录先将主配置完整复制到当前目录的临时文件，再以不覆盖现有目标的移动提交；失败返回空，继续后续候选，附属文件失败不撤销已成功的主配置。章节兜底删除类型齐全的短路，沿用现有同类区间重叠合并，不改跳过方式或界面布局。

**技术：** 现有 .NET 10/C#、AppPaths、SkipSectionPlanner、主 Tests 与实际源码提取的 NaturalContinuationCallerTests，无新增依赖。

**范围与约束：** 以 6b9fb3c 为隔离基点，主树轮播已由其聊天交付并明确释放，后续合入前重新核 Git。版本保持 0.2.5.0；不读取真实配置/凭据、不实服播放或写入、不上传。旧报告的133项不作为当前未修数量。历史来源 pin 漂移先核实际差异再逐项更新，守卫及负向用例保留。

## R02-002 配置复制成功边界

文件：src/Momoka.Core/Infrastructure/AppPaths.cs、tests/Momoka.Tests/SettingsTests.cs，以及新增 SettingsDirectoryMigrationTests.cs。

- [x] 在新树还原、编译并运行未改源码的完整主测试基线。
- [x] 添加真实临时目录/独占读锁回归：第一来源主文件失败时继续第二来源；失败不复制该来源备份或附属文件；单来源失败不残留半成品且解锁后可重试；主配置成功后附属目录冲突不撤销成功；目标为目录时拒绝提交。
- [x] 运行 settings-storage-fixture，核失败原因来自当前迁移行为。
- [x] 实现完整主文件提交后再尽力复制附属树，更新成功语义注释；重跑专项并提交。

## R10-008 章节跳过兜底

文件：src/Momoka.Shell/ViewModels/PlayerViewModel.Events.cs、tests/Momoka.NaturalContinuationCallerTests 的 PlayerBoundary/CallerCases/source pins，及播放技能中对应的旧语义说明。

- [x] 审计四项旧 pin 漂移并记录来源/理由；仅更新已核对项，不放宽守卫。
- [x] 在现有实际方法提取夹具中加薄入口与回归：两类服务器段已齐仍保留不重叠Opening；重叠服务器优先；空章节、重复读、未知时长的有界章节保持正确。
- [x] 当前产品上运行 Caller fixture，确认新行为断言失败。
- [x] 删除双类型短路，修正仅缺类型兜底的旧注释；定点更新Events pin。重跑 Caller 及来源提取负例并提交。

## 批次收尾

- [x] 对两项最终差异做独立复审，必要修正保留红绿证据。
- [x] 主树干净且唯一写入，合入本批并重验当前 x64 Release Core/Tests/Shell、完整主Tests、设置专项和Caller/提取负例。
- [x] 按日常交付范围发布到 C:/Users/89400/Momoka/artifacts/publish/win-x64，核载荷/许可与桌面快捷方式；不声称真实播放或未跑探针通过。
- [x] 保存415份原红/绿及来源证据、逐文件核SHA，记台账和PROGRESS。
- [ ] 原生归档本批隔离树：已尝试，应用拒绝当前聊天主工作树；树干净且全部合入，保留997e122。
