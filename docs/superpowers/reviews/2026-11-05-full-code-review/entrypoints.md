# 验证入口补充矩阵

基点 `6ba7a7f`；[validation-matrix.json](validation-matrix.json) 展开全部 68 个项目（2 产品、60 顶层测试、6 支持）的引用、源码链接、UsingTask、Exec、Import 与现有 runner 参数。输入盘点不等于业务测试通过。所有输出必须在当前树使用新目录；固定输出例外在下表明列。R01 实际执行见 [R01.md](R01.md)。

默认运行仍有两处范围差异：`LibraryHeaderTests` 未在59项回归清单中；方案漏 MoviePilotSubscribeSettingsTests、ServerSettingsTabs.Tests、SettingsPriorityMotionTests、SettingsRowsMotionTests、SettingsSearchNavigationTests、SiteDialogFlowTests。统一 runner 在列清单之前就因第一项差异拒绝。33个测试 csproj 含提取任务/targets/Exec（23个含UsingTask、7个含Import、3个含Exec），3项依赖 Python；源码提取与辅助项目不单独累计为业务用例。

## 主测试参数

`tests/Momoka.Tests/Program.cs` 有62个显式参数分支：38个单参数专项、19个 Shell 专项、1个原生专项、4个内部子入口，另有 MpvProcessTests.IsChild。无参主套件注册 UpdateTests 与 AgreementsTests，各7项，没有独立 update/agreements 参数。无参包含匿名网络、子进程与自检互斥锁，不能称纯函数测试。Agreements 输出固定 `artifacts/code-review/agreements-actual.txt`。

以下每个模式只传 `<mode>`；不启动 WinUI/浏览器或产品原生库，日志由外层重定向：

```text
moviepilot-boundary-fixture
episode-removal-fixture
detail-customization-fixture
toolbar-icon-fixture
account-menu-fixture
media-source-size-fixture
artwork-preview-fixture
navigation-timing-fixture
performance-probe-fixture
input-focus-fixture
acrylic-controls-fixture
input-theme-fixture
moviepilot-media-management-fixture
composition-readiness-fixture
settings-storage-fixture
danmaku-fixture
mpv-ui-fixture
selfcheck-layout-fixture
budget-cache-fixture
subtitle-forced-only-fixture
svp-fixture
playback-review-fixture
audio-output-fixture
selfcheck-output-isolation-fixture
http-download-error-fixture
restore-identity-fixture
moviepilot-login-redirect-fixture
plex-protocol-fixture
deep-browse-protocol-fixture
plex-login-fixture
realtime-handshake-fixture
emby-metadata-fixture
metadata-date-fixture
anibt-receipt-state-fixture
marked-watched-source-fixture
moviepilot-episode-group-playback-fixture
moviepilot-series-transfer-fixture
moviepilot-transfer-submission-fixture
```

以下参数为 `<mode> <本树当前标准x64 Release Momoka.dll> <本树绝对根>`；加载真实 Shell VM/源码链接，使用匿名端点，不是 WinUI 窗口验收，无浏览器或原生前置：

```text
startup-state-contract-fixture
startup-reapply-notice-fixture
settings-search-fixture
settings-page-state-fixture
settings-applicability-fixture
library-resume-paging-fixture
library-deep-return-fixture
home-performance-fixture
detail-deferred-cards-fixture
library-favorite-source-fixture
settings-screenshot-restore-fixture
fnnas-library-editor-fixture
fnnas-user-state-fixture
player-track-readback-fixture
player-volume-gesture-fixture
video-sync-review-fixture
detail-subject-removal-fixture
detail-action-readiness-fixture
moviepilot-subscription-page-state-fixture
```

`svp-native-fixture <本地libmpv-2.dll>` 为原生无媒体入口，需普通非重解析本地路径；`svp-fixture` 只是规则及匿名管道。内部子入口为 `selfcheck-mutex-fixture <source> <target>`、`test-harness-fixture <scenario>`、`activation-listener-fixture <scenario>`、`svp-pipe-child <pipe>`；`mpv-process-child` 由 `--force-media-title=` 前缀识别，不独立当专项运行。

## 专项模式与输出

| 项目/入口 | 额外模式或参数 | 前置/输出边界 |
| --- | --- | --- |
| AtomicFileWriteTests | `--evidence-directory <tests/Momoka.AtomicFileWriteTests/outputs/NEW>`；可选互斥 `--controls-only` / `--backup-failures-only` | Windows≥14393，源hash匹配，不能将artifacts当该项目evidence；默认生成direct时间戳目录 |
| AniBtSourceContractTests | `shell-probe`；无参完整 | R042Legacy=true 排除caller/probe子集 |
| FrameInterpolationTests | 无参、`regression`、`native <DLL>` | native需本地libmpv，后者不是无参覆盖 |
| ResourceBudgetTests | all/budgets/websocket/logs/fonts/compatibility | 默认all；file-tail-child为内部 |
| ResourceBrowserTests | `<root> [anibt-layout或search-sites]` | root单参完整 |
| PageContractTests | `library-anchor-page-fixture` | 无参页面契约另计 |
| PlayerMotionProbeTests | `<root>` / `pre-fix-control <root>` | 旧源对照不能称当前通过 |
| HomeOrderProbeTests | `<shell> <root> [legacy-red]` | 完整依赖当前Shell |
| SettingsProbeContractTests | `<shell> <root>` 或 `core-settings <新Momoka.Tests.dll> <root>` | core-settings仅注册Settings/ServerSettings/StartupEntry子集 |
| CurrentPermissionSnapshotTests | `<root> [shell]` | 只有root是托管/源码子集；统一runner传Shell |
| MissingEpisodesAvailabilityTests | `--report <树内新文件>` | WriteAllText会覆盖，调用者须保证新路径 |
| DanmakuPerformanceTests | `--output <NEW> [--measure-only]` | Python提取；measure-only不能当回归 |
| SettingsDeferredSaveTests | `<shell> <root>` | 固定覆盖 artifacts/perf-settings/slider-results.json |
| SettingsPersistenceTests | `<root/artifacts/NEW/fixtures>` | 不自行拒绝复用目录 |
| ImagePerformanceTests | `perf-images` | 固定生成 work/perf-images/runs/时间戳/results.json |
| ArchitectureTests | `<root>` | 先构建当前Core/Shell；从整个bin选最新Release，必须记录实际路径/hash；不证明真实窗口 |
| LibraryHeaderTests | 无参 | 源链接AlphaBar/提取LibraryPage.Header，另注册Agreements，不能重复计数 |

## Wrapper、语言及工具入口

| 入口 | 参数/前置 | 输出与限制 |
| --- | --- | --- |
| DetailDeleteCommandTests/Run-Fixture.ps1 | `-Dotnet -Packages [-ShellDll] -EvidenceRoot` | EvidenceRoot树内artifacts新目录；build-proof；Packages不是离线source，restore可能联网 |
| NaturalContinuationCallerTests/Run-Caller.ps1 | `-OutputDirectory <artifacts/r046-caller/NEW>` | 用户SDK、19项SourcePins、3个固定analyzer包；内部feed/TEMP/packages隔离 |
| ServerSettingsTabs.Tests/Run-Fixture.ps1 | 必须`-Node -NodeModules -Browser`；可选`-Dotnet -EmbyManager -OutputRoot` | Playwright/浏览器、当前Core；默认work/server-settings-tabs/final可覆盖；contracts只是子集，统一runner external为skipped非零 |
| tools/test-uosc.py | `--root <tree> --output <NEW>` | 本树libmpv+Vulkan，Lua/uosc-interaction-tests.lua；idle不播放 |
| tools/test-danmaku.py | 同上 | Lua/danmaku-tests.lua |
| tools/test-subtitle-translation.py | 同上 | Lua/subtitle-translation-tests.lua |
| DanmakuPerformanceTests/run-lua.py | `--root --output [--baseline-source] [--real-timers] [--inject-failure]` | 前两可选模式互斥；4份性能/真实计时Lua，native无媒体 |
| tools/test-uosc-review.py | `--lua <可信Lua5.1 DLL> --suite uosc或translation` | 独立Lua解释器，不是libmpv；stdout |
| website/worker/credential.test.mjs | `node --test website/worker/credential.test.mjs` | Node内建test/assert/WebCrypto，无npm依赖，匿名Secret不部署 |
| 技能check-skill.py/test-check-skill.py | `py -B -X utf8 …/check-skill.py --all --root <tree>`；测试无参 | .claude/skills/momoka-skill-creator/scripts |
| 音频技能test-probe-audio-native.py | `py -B -X utf8 …/test-probe-audio-native.py` | 不加载native |
| 音频技能probe-audio-native.py | `--dll <libmpv> --out <NEW report.json>` | native无声；open x拒覆盖，默认work/audio-probe时间戳 |
| tools/check-server-support-docs.py | `--root <tree>`；tests/test_server_support_docs.py | 内容/文档一致性 |
| tools/check-main-tree-policy.py | `--self-test` | 文档/规则检查 |
| tools/scan-automation-ids.js | node无参 | 静态句柄，不能替代UIA树 |
| test-mpv-payload / payload-boundaries / package-content / publish-layout / runtimepack-content | `-Phase r01-…` | artifacts/r067-approved-payload/phase-时间戳；生产checker+固定非PE合成native，package-content还需本树Shell restore及只读固定Toolkit签名包 |
| test-mpv-dotnet-runtimepack-content | `-Phase r01-dotnet`；可选PublishedMappingOnly/PreviousChecker | artifacts/dn/随机目录；固定SDK managed NuGet、内存合成CMS；不写证书库、不加载产品native |
| test-shader-publish | 无参 | artifacts/shader-publish-tests/GUID，校验文本载荷不证明画质 |
| test-release-maintenance / test-maintenance | 无参 | artifacts/release-maintenance-tests/GUID、tool-tests/GUID；匿名签名/打包边界 |
| test-smoke | 无参 | artifacts/smoke-tests/GUID；restore/build临时Console helper后启动，无Momoka；默认restore可能联网 |
| check-scripts / check-diff | 无参 / `-All` | 编码/语法/字节规则，不能证明功能 |
| quality.ps1 / run-regressions.ps1 | 当前源码帮助与清单 | quality可构建/联网npx；回归缺项守卫不得绕过；失败/跳过保留非零 |

UI、自检、字幕、指针、Composition、PlayerMotion等运行入口由 R17 主责，`ShellProbeSections.All` 与实际Case注册联审；普通自检可能读取已保存账号，不能默认作为离线入口。真实服务器、播放和写操作均未获本轮验证授权。二进制/浏览器前置不足时保留具体缺口，不以源码/DOM替代运行结果。

## R05本轮执行入口（2026-11-06）

证据根`artifacts/review/6ba7a7f/R05/20261106-first`。临时runner引用当前Core DLL并核SHA256，反射当前Momoka.Tests的完整Register：fnnas为Protocol/Media/LibraryInput/UserStateWrite，plex为Protocol/Media（V2Login由Media内部注册）。cross加载ServerSettingsReview输出的Momoka.Tests.dll，只运行完整PlexDiscovery/PlexSearch/FnNasArtwork.RunAsync，正式全入口未跑。repro最终输出在repro-final目录，反例成立不算产品通过。

四专项先缓存restore及当前Release build，FnNasErrorClassification无参数，FnNasDefaultPermission传当前根，PlexAffordance传当前x64 Release Shell DLL与根；均没有真实网络/WinUI后备。当前计数121、148、21、36、68；cross7/10/3场景分列。命令、源码哈希、退出和边界见[R05](R05.md)及[R05验证](R05-verification.json)。

## R06本轮执行入口（2026-11-06）

证据根`artifacts/review/6ba7a7f/R06/20261106-first`。`run-checks.ps1`串行缓存restore/build/run四专项；Core/主Tests/Shell自R05成功构建后源码未变，运行前后核哈希，四实际VM入口按开发文档传当前Shell DLL与根。计数HomeDirectory15、HomeFold56、LibraryHeader18（含7Agreements）、SearchLifetime42、resume38、favorite13、deep17、home19；临时runner的core模式反射16组完整Register共211，9入口合429项但不声称互不重叠覆盖。

`runner`引用当前Core DLL并校验哈希，repro模式反射实际Shell和原夹具；最终`repro-final-4/repro-results.json`9条反例成立，失败日志不覆盖。原固定fixture写入本树随机匿名图片缓存目录，不接真实HTTP/配置/凭据；源码确认两项没有运行真实计时器/点击。命令、哈希、失败原因与未运行范围见[R06](R06.md)及[R06验证](R06-verification.json)。

## R07 本轮执行入口（2026-11-06）

证据根`artifacts/review/6ba7a7f/R07/20261106-first`。本轮重新编译当前主Tests/Core；原`tests/Momoka.DetailDeleteCommandTests/Run-Fixture.ps1`另重编Shell/fixture并生成证明，19项通过。状态顺序73、主体删除33、详情就绪24、延迟集卡8、Emby元数据61、日期96、详情配置28、外观31通过；`fnnas-user-state-fixture`29过3败保持红。临时runner引用当前Core/Tests/Shell，9组完整Register386过；11入口共791执行、788过3败0跳过，不累计旧分区、不声称互不重叠。

`run-checks.ps1`在正式飞牛夹具失败后按原行为停止；其后三个独立参数入口另行执行，日志原样保留。`repro-final3/results.json`7组反例/对照成立，包含两个收藏方向、500失败前提修正对照和4个识别关闭写场景；正式产品/测试未改。原样提取只接受唯一标记，最初歧义失败与后续成功均保留。删除wrapper使用固定packages目录但未锁--source；不得称完全离线restore。产品执行均匿名、无外部HTTP/真实写入/播放，细节见[R07](R07.md)与[验证JSON](R07-verification.json)。

R07最终漂移复验：`final-current`对应b8c1d61/ac4e3fa组合，重新编译Core/Tests/Shell、原删除wrapper、状态顺序/外观/临时runner后复跑。外观39、新增Gaussian完整Register5，其余计数不变；最终12入口804项、801过3败0跳过，旧791项不重复相加。`final-current/repro/results.json`7组与`blur/results.json`1组观察成立；正式飞牛夹具仍红。最终日志和DLL哈希见[R07验证](R07-verification.json)。

## R08 本轮执行入口（2026-11-06）

证据根`artifacts/review/6ba7a7f/R08/20261106-first`。`run-checks.py`串行重新构建Core/主Tests/Shell，6专项固定本机缓存restore后Release构建/运行；Journey传当前Shell DLL与根，ResourceBrowser传根，其余四项无参数。主Tests另跑moviepilot-login-redirect-fixture43、moviepilot-episode-group-playback-fixture58、moviepilot-subscription-page-state-fixture31。六专项21/174/19/3/74/13；临时runner反射11个完整Register238，合10入口674执行全部通过，包含重叠，不与历史或相同Register重复累加为独立覆盖。

最终采证runner-build-5为0警告0错误；final-core/repro/sites/anibt/files全部退出0。repro-results、sites-repro、anibt-repro、files-repro包含6组观察，成立是缺陷证据不是修复通过。所有产品代码及真实夹具来自当前树；默认Cookie探针只连同时保留的匿名双回环，其他handler不对外联网。没有真实UI/配置凭据/服务写入/播放/发布。完整命令、哈希、失败和未覆盖见[R08](R08.md)及[验证](R08-verification.json)。

## R09 本轮执行入口（2026-11-06）

证据根`artifacts/review/861f48b/R09/20261106-first`。五专项AniBtEntry51（传当前根）、AniBtConfirmation57、AniBtSourceContract40、TransferSubmission78、MissingEpisodesAvailability58，固定本机NuGet缓存restore后Release构建运行。主Tests/Core重编后执行moviepilot-series-transfer-fixture166、moviepilot-media-management-fixture81、anibt-receipt-state-fixture44。临时runner原样注册完整AniBtTests/SeriesIdentity/PrepareConcurrency/Integration108；合9入口683执行全过，重叠不作独立覆盖。生成绑定检查17过；ResourceBrowser174复用R08无源码变化证据，不计入683。

最终runner-build-4与anibt-runner-build-2均0警告0错误。verified-core/transfer/storage/missing/dependents/library与anibt-final退出0；upstream-functions原样5函数经假仓储/记录型存储验证同源H2清理。storage-final的Undefined JsonElement序列化失败保留，仅修临时采证；正式代码未修。

Shell复用R08当前Release同哈希，Library采证执行真实VM的普通库Reload，完整Shell分支由源码联证；不是WinUI/实际删除。匿名handler、原样上游函数及公开源码/规格访问范围见[R09](R09.md)与[验证](R09-verification.json)。旧红/部署/真实UI/来源ABA缺口保留。

## R10 本轮执行入口（2026-11-06）

证据根`artifacts/review/aa631ff/R10/20261106-first`。`run-checks.py`串行固定本机缓存restore/Release构建并运行NaturalEpisodeContinuation27、PlayerExitHandoff3、PlaybackSourceRevocation31、LibMpvEndFile40；原`Run-Caller.ps1`直接执行，输出本树`artifacts/r046-caller/review-r10-20261106-first`，19份pin/完整源校验后54通过。没有绕过wrapper或改pin，10个提取拒绝反例本轮未跑。

`run-core.py`重新构建主Tests/Core后，临时runner反射7组完整Register（PlaybackLifecycle、MarkedWatchedSource、MediaVersionDiscovery、MediaVersion、KeywordPriority、StartIntent、EpisodeRemoval）和PlaybackTests的6个完整注册方法（Planner、SkipSections、SkipCoordinator、EpisodeNavigation、Donghua、StopReport），258全过。合6入口413执行全过；重叠不当独立覆盖，不与历史累加。

临时caller原样链接本轮wrapper生成源/既有匿名边界，额外原样提取StartVersionAsync；12条观察覆盖6组确认问题。临时service执行真实Core与假handle/HTTP，2条观察确认起停/暂停顺序。人工volume采证完整收尾后为真，但当前独占用户入口不可达其队列，撤回确认分类，保留原证据与两次收尾失败。源码/产物/日志哈希、临时编译失败、命令与未运行范围见[R10](R10.md)及[验证](R10-verification.json)。Shell未重编或加载，没有真实native/WinUI/服务器写入/播放/发布。

## R11 本轮执行入口（2026-11-06）

证据根`artifacts/review/12e99d8/R11/20261106-first`。当前主Tests/Core固定缓存restore后Release重编0/0；临时reflection宿主注册19完整Register202过，另18个完整PlaybackTests方法153过。首次宿主base目录不符导致90秒timeout，修临时APP_CONTEXT_BASE_DIRECTORY后假mpv子进程按正式契约启动；无产品测试修改。原log与输入hash见[R11验证](R11-verification.json)。

`py -X utf8 tools/test-uosc.py --output <E/uosc-run>`，本树批准hash一致的libmpv及Vulkan，完整uosc49过。原`tools/test-uosc-review.py --lua`无可信独立Lua5.1 DLL，完整8项正式Lua fixture经临时runner在随包LuaJIT执行8过，不能称原wrapper通过。合412次通过执行，含重叠。额外完整uosc反例51中2失败、错误0；完整stats30pass两观察和真实Core暂停/配置5观察、真实native未initialize配置两观察另列。

Shell本轮未重编/加载，暂停用完整原样成员+UI状态替身，统计用完整实际脚本+fake属性输入。没有任何媒体/窗口/HTTP/真实凭据，native只create/load_config/destroy，无initialize。R10此前40项EndFile与caller通过不因换区重复跑。采证失败与旧红项保留，R11后暂停。

## R12 本轮执行入口（2026-11-06）

证据根`artifacts/review/11921ff/R12/20261106-first`。`run-review.py`按固定本机NuGet缓存依次restore/Release构建FrameInterpolation与ShaderCommandGeneration，默认45/regression23/native184、本树49过；随后当前Shell重编0/0，Monitor专项32过。主Tests/Core已有R11当前源码输出，初AnyCPU/x64 hash不同后本轮主Tests `-p:PlatformTarget=x64`重新Release编译0/0并核Core与Shell相同；最终svp31、player-volume-gesture35、settings-screenshot-restore14及观察均重跑。

临时`registered/Program.cs`反射原完整AudioOutputTests/VideoOutputTests/ShaderTransactionTests.Register及PlaybackTests八个完整Register（Shaders、UpscaleTier、OutputWatch、OldVersusNew、ShippedShaderFiles、RendererContract、ShaderDescriptions、OutputOptions）151过；初宿主AppContext资源根错误145过6败保留，只改ignored宿主基目录。正式托管合380次执行含重叠。FrameInterpolation native184/`svp-native-fixture`24及`native-options.py`33唯一受控方案另列；全部批准DLL，无媒体/VO/AO。音频技能原`probe-audio-native.py --dll <本树批准DLL> --out <E/audio-native.json>`采集21候选/4零信号场景，shared wasapi/null AO、失效端点预期no sound分列，不把采集退出0称全部设备成功。

`observations/Program.cs`只反射正式匿名State及当前真实VM/Service，八观察成立；原生事件可达竞态用固定client/事件线程源码联证，未真实执行native线程争用。观察宿主编译缺using原日志保留。脚本及最终source/Core/Shell/输入hash见[R12验证](R12-verification.json)。

master f71883d由`git archive`仅导出所需Core/Shader专项/资源/共享构建文件到E/master-snapshot，当前固定源码副本restore/build0/0后59全过。母树不写master；final50fab2f相关增量无漂移，其他38总体差异保留。新batch实际config通知合并只读源码/真实DLL配置采证，无GPU媒体/GLSL；原wrapper提取器10反例、shader发布反例、完整主套件/全回归、真实UI/媒体/服务/发布未执行。R12完成暂停。

## R13 本轮执行入口（2026-11-06）

证据根`artifacts/review/8e8619e/R13/20261106-first`。run-review.py使用固定本机NuGet缓存/隔离home-temp-packages，SDK10.0.400；串行Release/x64 restore/build翻译119、Runtime19、Shell与主Tests，再显式player-track-readback15、subtitle-forced-only22。正式Dialog当前编译CS0103、未跑；Drop首x64 MSB4062后按README默认Platform当前编译0/0、74过。

run-additional.py只在ignored宿主补一个null_blurCancel字段，完整正式Dialog源/原样caller/parent、正式37断言全过，不能称正式Dialog已修。registered反射六完整Register（SubtitleBehavior/File/Management/Preview/HtmlColor/InlineSwitch）及四Playback方法（TrackMapping/TrackDisplay/TrackSelection/PlaybackBatches）100过；资源基目录显式本树。run-final-tests.py执行完整PlaybackTrackState.Register7过，真实SettingsApplicability.Register后仅筛名称R-055的14条；筛选范围明列，不改断言。正式托管370次含重叠，诊断37另列。

批准native tools/test-subtitle-translation.py原入口9过，fake属性/画布/事件，实际Lua/parser/libass无媒体窗口。lua-payload观察是实际Payload字节+原样Lua/真实parser，revision匹配后large不显示/short显示2条成立；第一次revision错配对照失败保留。native-track观察仅本树静音WAV/SRT/null输出/禁脚本网络，正常/首外挂缺失/三个外挂3方案，真实sid与visibility读回分列；不声明线上字幕URL/画面验收。

observations完整native handle/gate/真实Service及正式匿名ServiceFixture，两条subtitle-style交接/旧写覆盖观察；vm-observations加载当前x64实际VM，正式匿名构造/队列＋定点handle，最终隐字幕选轨和reset-delay观察成立。初active缺失/只读字段错误的独立日志保留。dialog-observations完整实际caller/Dialog/Core检查非默认A及写后default变化；queue-observations完整实际Controller/Client+原样Shell通知方法及受控最终队列，关闭后旧warning观测成立。临时类名编译错误改ignored后通过；queue初restore/build及Lua初对照同名日志被后次覆盖，只有原退出/hash保留，JSON明确原完整输出缺失。旧cue服务退避观察撤回缺陷。命令/日志/hash/观察输入见R13-verification.json，真实UI/费用/服务器/完整回归未跑。

## R14 本轮执行入口（2026-11-06）

证据根`artifacts/review/a998403/R14/20261106-first`，runchecks.py串行固定SDK10.0.400、本机缓存/隔离home-temp-packages；当前Core/Shell/主Tests0/0，danmaku-fixture59、Worker4。SourceBoundary/Performance标准MSBuild Exec py9009环境失败，PATH/禁server尝试均红；ignored Python.targets仅绝对Python调用原提取器适配0/0，17/71全过；不得冒标准入口通过。所有checks日志退出/hash保留。

tools/test-danmaku.py原42过；Performance run-lua弹幕50/翻译17、actual timers各3过，属性/画布/overlay替身；uosc最终增强50过含49原断言+1C1观察。initial input-lua旧夹具与final分开，最终验证send count1/payloadC1再编辑pending仍存在。原Parser实际拒绝C1，未真实剪贴板或事件联跑。

完整Client/Source匿名可读不可删文件锁→旧签名2/端点0，解锁实际Worker向量C#接受新身份端点1。observations加载当前真实VM/正式匿名构造、原样Panel四完整方法，服务草稿/source陈旧、旧Close取消实际AutoMatch请求、SettingsRows空custom labels成立。新场次由generation/item模拟，不是实际换片/WinUI；初宿主1警告日志保留、最终0/0。初root错误保留；临时Worker import错误无独立日志、工具输出可追。证据hash/源提取范围见R14-verification，旧红和发布缺口保持。

## R15 本轮执行入口（2026-11-06）

证据根`artifacts/review/6fc7f0d/R15/20261106-first`，runchecks.py固定SDK10.0.400、本机缓存及隔离home/temp/packages，restore/build/run串行。当前Core/Shell/主Tests Release/x640/0；标准55/15/46/24/69/68、主search14/state31/applicability32合354次含重叠；首55旧Shell只保留，当前Shell重编后复跑55。ServerSettingsReview标准全部注册组过，无统一总计。ServerSettingsTabs完整官方Run-Fixture：合同14、DOM25，请求0/pageerror0；依赖来自已安装Node/Playwright/Edge、隔离profile、不下载浏览器，无可选EmbyManager。

observations当前真实VM/Session/Client/FnNas与内存写gate，完整StartAsync原样提取和生产完整NotificationDialog+标准UI边界。managed-observations最终11条记录覆盖新根因；原稿/门标志私字段、ShowEditor手动Closed、空事件目录等限制见R15 JSON。CSS固定Jellyfin10.10.7真正onTypeChange/HTML/.hide，当前Core导出脚本，none hidden/0submit vs当前Jellyfin inline-flex/1submit；没有上游submitForm/POST。旧失败完整log和退出/hash留checks；一次未知mode仅工具输出。无产品修复/真实应用/服务器/交付。

## R16 本轮执行入口（2026-11-06）

证据根`artifacts/review/9532593/R16/20261106-first`，runchecks.py固定SDK10.0.400/本机缓存与隔离home-temp-packages，编译/运行串行。当前Core/Shell/主Tests Release/x640/0；标准Dialog10/Opacity39/Gaussian5/Image22+49/ResourceFonts14/Page默认137及主显式focus21/acrylic25/input28/budget7合357执行含重叠。未执行Page独立Anchor、全Resource/all、完整主Tests/统一回归/交付闸门。

subset编译为不同程序集R16Subset，显式装载刚编译本树主Tests DLL，执行Theme/CardSize/CardStrip/Screen/WrapLayout/HtmlColor/About七完整Register及FontParser/Catalogue/Search三私有注册76过，未扫本机字体。初internal Register反射NullReference保留；最终补NonPublic，未改正式断言。不能称标准入口覆盖。

observations完整source-link CardItem/BannerSlide/Banner/HeroLoader/Cache及真实Core/WinRT；BitmapImage仅WinRT读BMP尺寸，PosterLoader为正式字节边界，handler只匿名图片GET。坏盘/清盘、fallback/Primary对照、两拼图各holder-false/true/old-holder合10观察，原6条保留在先次run.log；不是正式产品通过用例。临时静态类型/AppPaths编译和错误DLL路径红均日志保存，最后0/0。

固定master d984ac9仅git archive所需源码到E/master-snapshot，未新建或改其他工作树。首archive BannedSymbols位置错导致缺项目，随后漏该输入编译红；补同提交输入后编译0/0，Opacity因缺HomeBanner.xaml41过1败，补同提交XML后42过，Gaussian7过。是固定副本适用检查，非最终主树Shell/屏幕验收。完整输入hash见R16 JSON，初tar不是后来完整输入的唯一清单。

自动化扫描退出0：598交互/456已定位/无句柄0，模板132/样式4/重复控件6仍缺UIA。官方Acrylic稳定2.3.1和固定main两cpp/h同hash，保留URL/commit；不声称随包2.3.9精确源码或真实材质像素。只有工具输出的错误release路径404、runpy无参数和初archive错误不补造独立log。旧R01/R07/R13/R14红及其他限制保持，R16完成后暂停。

## R17 当前入口审查与执行

68项目runner/buildInputs完整保留；60顶层/6辅助/59清单，LibraryHeader仍漏登记，因此统一入口拒绝。正式托管282次280过2败与工具70分列，计数不当唯一覆盖。Perf默认71/x64 69+2；Shader本树49过但wrapper旧37红，主树59只静态；Shell120s超时124，SettingsProbeContract/HomeOrderProbe未跑。R045/R046显式x64任务旧AssemblyFile与R13正式红保持。全部命令、源码hash、原日志及旧状态见[R17验证](R17-verification.json)，不跑旧DLL。

## R18 文档与公开资料审查补记（2026-11-06）

284主责处置和22联审见[R18](R18.md)及[验证](R18-verification.json)。八入口仅文档/工具：skill-check15（2未校验metadata警告）、skill-test5、support-check/support-test7、policy3文档/21负例、audio-helper14、官网真实main.js匿名DOM17断言、结构/链接检查；正式工具26项与匿名断言分列，不算应用全量或交付。6dist相等、81JSON解析、1386本地链接1359存在/27缺失，历史缺出处保持。

R04实时URL/旧路由、R10全剧连播、R14退役凭据/四卡与R16计数交接已核，必要文案未修；错误参考链接不在主SKILL结构检查范围。R03-006与R17-013同根因，最终只沿R03-006，保留原报告。R02schema27、历次增量和当前master产品仍待全项目最终差异复核。R18完成后自动暂停，不合主树/修产品/push/发布。


## 最终矩阵闭合（2026-11-06）

[最终68项状态](FINAL-matrix.json)保留原runner、status和全部旧执行；LibraryHeader输入已删除退役AlphaBar链接并登记现行Core引用。方案外合计9项目＝6顶层专项＋3旧支持，未登记runner仍只有LibraryHeader。62主参数之外有MpvProcessTests.IsChild；未知参数落入主套件仍沿R17-003。全部18wrapper及Lua/Worker/DOM/native/UI边界分列，不能由矩阵中的两个runner wrapper推断仓库只有两个。schema27新增匿名观察10输入6符合4不符，不算正式测试全绿。
