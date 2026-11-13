# P8 全方案空白检查收尾

日期：2026-11-13。独立树：`C:\Users\89400\.codex\worktrees\winui-p8-whitespace-20261113\Momoka`，临时分支`codex/winui-p8-whitespace`，基线`8d4802c96211b002eb978ed9505c79bb70a91ce6`。协调树全方案空白检查列25文件（含本批及已有格式债），本单元仅修这25文件的空白与LF；不改产品逻辑、命名、样式、权限或原生路径。

## 改动与等效检查

先复制25文件的精确基线到本树`artifacts/p8-whitespace/before`，从协调日志提取include清单，再运行`dotnet format whitespace Momoka.sln --no-restore --include <25文件>`。格式化日志确认只修改25/3356文件。原生字符串、注释和C# token全部保留；使用SDK10.0.400自带Roslyn解析前后源码，逐项比较Token.RawKind/Text与所有非空白/非行尾Trivia，25/25一致。较大的ItemBatchPlaybackTests差异来自整体缩进，词法序列等效。

25文件为：

- Core：FnMusicHttp.cs、FnMusicMediaAdapter.Models.cs、LocalMediaName.cs、TmdbClient.cs、PlaybackService.Reporting.cs、SrtDocument.cs。
- Shell：PlayerCaptureController.cs；ShellNavigationProbe的BatchCommandBar、ConnectedAnimation、FnNasMixed、InfoTable、InfoTableFixture、KeyboardJourney、KeyboardJourneyFixture、LibraryBatch、MaterialLifecycle、PlayerCapture、WindowResearch。
- Tests：LocalLibraryPageTests/Program.cs、PageContractTests/CardPresentationTests.cs；Momoka.Tests的DetailTrailerTests、ItemBatchPlaybackTests、ItemTrailerTests、PlaybackHttpNativeProbe、PlaybackHttpTransportTests。

键盘源契约原先用相邻两个字段的单行字符串定位，合法空白格式化将其拆行后19项1失败。此失败保留，不把格式化改回违规形式。经协调批准，仅将此断言改为分别核`resumedFromSearch = resumeFromSearch`、`seededIdentity = resumeFromSearch`各2次（阶段与最终读数），新增同样表达式换行接受、缺读点/错字段/错值拒绝反例，未改变人工输入或匿名播种规则。该测试文件是本单元唯一非纯空白的额外代码变化。

## 当前验证

使用用户级SDK10.0.400、Release、单节点、BuildInParallel=false、UseSharedCompilation=false、MSBuildNodeReuse=false；完整方案build显式`PythonLauncher=C:\Windows\py.exe`。新树restore使用已安装本地包来源，不升级依赖、分析器或运行时。两次完整方案编译分别9m03.96s及最终3m28.50s，均0警告0错误。

| 检查 | 本轮结果 |
| --- | --- |
| 指定25文件空白复验 | 0文件修改，退出0 |
| 新键盘测试单独空白检查 | 0文件修改，退出0 |
| 最终指定26文件空白复验 | 0文件修改，退出0；94.052s |
| Roslyn token/非空白trivia前后 | 25/25一致 |
| Main当前测试/Core | 5029通过，0失败，0跳过 |
| PageContractTests | 215通过，0失败，0跳过 |
| KeyboardJourneyTests | 修前18通过1失败，修后20通过0失败0跳过 |
| ConnectedAnimationTests | 34通过，0失败，0跳过 |
| LocalLibraryPageTests | 12通过，0失败，0跳过 |
| WindowResearchTests | 6通过，0失败，0跳过 |
| MaterialLifecycleTests | 56通过，0失败，0跳过 |
| DetailImageArrivalTests | 20通过，0失败，0跳过 |
| 编码与Git空白 | 最终27文件UTF-8/LF/BOM/diff全部通过 |

曾在完整方案后台尚未编到末尾两项目时提前运行Keyboard/Connected，命令因无exe退出1，属于执行顺序错误，未作为验证通过；等完整编译成功后重新运行取得上述真实结果。Main结果对应当前Core和主测试编译产物，不把格式检查本身当功能或原生验证。

证据保留本树`artifacts/p8-whitespace/`：原文件清单与before副本、restore.log、format.log、verify.log、verify-final.log、tokens-final.log、solution-build.log、solution-final-build.log、main-tests.log与各专项日志。SDK Roslyn小工具仅在该ignored证据目录，不成为干净检出前提。

## 整合边界

本树仅25原文件、1格式兼容测试及本报告；不触共享PROGRESS、XAML产品页面、用户数据、桌面或实际交付目录。协调树后续键盘FromResult实现和源接线变化可能与ShellNavigationProbe.KeyboardJourney.cs及KeyboardJourneyTests/Program.cs重叠，整合必须保留较新的产品行为，仅吸收空白和最窄契约修正。纯空白等效结果不能替代主树最终整合后编译/测试/原生路径验收；P8剩余运行及发布由主协调会话完成。
