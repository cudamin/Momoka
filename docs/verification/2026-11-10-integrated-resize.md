# 集成播放窗口缩放验收

## 2026-11-11 主树合并与最终验收

合并提交 `d649eba` 保留主树已有的收藏导航、紧凑菜单、详情云母与媒体库改动；产品自动合并，仅 `PROGRESS.md` 的追加记录冲突并保留双方内容。

主树连续取屏发现“拖回原尺寸后松手”还有一帧露底：自动验收 `auto-build/resize-TopLeft-bad-3173.png` 保留原帧。`IsContentReady` 只证明缓冲尺寸一致，不证明最终合成变换上屏；提交 `889c219` 将有承接帧的收尾统一交给稳定和提交等待。源链接新增用例先失败1项、修复后17/17通过，失败与通过证据分别为 `settle-red`、`settle-green`。

- 当前主树 Shell 和 Tests/Core Release构建：0警告、0错误。
- 完整测试：4574/4574通过，0失败、0跳过。
- 独立专项：ResizeHandoff 17/17、PlayerInputRecovery 17/17、PlayerMotionProbe 23/23通过；不合计重叠用例。
- 修正后自动缩放：右下159、左上158个稳定样本，坏帧0；119个抓取中几何变化样本单列，未计通过。
- 当前主树真实鼠标：用户手动激活窗口后4次原生拖边事务、26次尺寸事件；2557个稳定样本、坏帧0、18个几何变化样本。暂停、松手恢复和撤层均通过。前两次前台保护拒绝的零输入运行保留为未通过，未换工具绕过。
- 证据根为 `artifacts/resize-delivery-20261111`：`tests-final.log`、`shell-final-build.log`、`settle-green/summary.json`、`auto-final/resize-probe.txt`、`manual-user-activated/resize-probe.txt`、`manual-user-activated.mp4`。真实录像提取连续画面后目视核对。
- 主树发布成功：547文件、287.5MB、60份批准mpv载荷全部校验通过，回执 `publish.log`。发布版又做自动缩放531个稳定样本、0坏帧、120个几何变化样本，见 `published-auto/resize-probe.txt`。
- 桌面 `Momoka.lnk` 指向 `C:\Users\89400\Momoka\artifacts\publish\win-x64\Momoka.exe`，工作目录正确、启动参数为空。EXE、Shell/Core DLL、PRI与本次构建SHA256一致，见 `delivery.json`。本次已完成主树交付；未上传GitHub。

以下为11月10日开发阶段历史证据；不替代本节主树最终验收。

范围：本地静音六彩条视频、内置 libmpv 集成管线。没有登录真实媒体服务器，没有播放真实库或上报观看记录。正常片源信箱边、独占/uosc、外部 mpv、HDR 色调没有在这次验收中覆盖。

## 复现与修复

旧代码先改变宿主 HWND/XAML 岛尺寸，随后写入视频合成属性；窗口与合成提交间可显示上一尺寸的视频。独立后台取屏在右下连续反向拖动中记录109个稳定样本、11个露底帧；截图右侧及底部露底与用户报告相符。提前请求异步提交仍有9帧露底。

修复在首个实际尺寸意图后暂停并异步抓取当前帧；首帧就绪前保留旧窗口并合并最新目标，之后由原生帧承接层随目标尺寸更新。松手待新视频缓冲稳定后撤层并恢复。纯移动不暂停、不抓帧；旧收尾不能撤掉新拖动或新收尾仍持有的帧。

## 最终证据

- 当前 Release Shell：0警告、0错误。
- 当前编译的主测试：4537/4537通过，0失败、0跳过；`work/resize-acceptance/core-tests-delivery.log`。
- `Momoka.ResizeHandoffTests` 16项、`Momoka.PlayerInputRecoveryTests` 17项、`Momoka.PlayerMotionProbeTests` 23项通过；各套可能重叠，不累加。回执：`artifacts/regressions/20261110-142556219-fff40dc7e7d24f428b9c71c4c5e89bfa/summary.json`。
- 新源链接回归的三个隔离源码负例分别撤掉异步让步、正确交接责任、待目标回原更新；各准确触发对应失败，再用真实源码通过。
- 最终3px近边采样自动测试（等号命令行写法）：右下221、左上232个稳定样本，坏帧0；另有121帧因取屏期间尺寸变化标为ambiguous，不计通过。日志目录：`%LOCALAPPDATA%/Momoka/logs/player-motion-probe-20261110-142817184-23288/logs`。
- 最终3px真实鼠标测试：4次系统拖边事务、18次尺寸事件，1982稳定样本、0坏帧、18 ambiguous；暂停/恢复与撤层通过。日志目录：`%LOCALAPPDATA%/Momoka/logs/player-motion-probe-20261110-142926880-9720/logs`。录像：`work/resize-acceptance/manual-final.mp4`，已检查取出的连续截图。
- 行尾/BOM/空白检查通过。四种缺参、空值、URL拒绝的批量运行命令被自动审批策略阻止，未执行、不计通过。

## 11月10日待办记录（11月11日已完成）

修复提交 `8a68a6f`，源链接测试提交 `1288773`。主树媒体库第一批交付后，已在本树合入到 `e91d7f5`（本树整合提交 `7e08947`）；相关缩放代码无变化。组合后的 Release Shell 0警告0错误，主测试4573/4573通过、0跳过。第一次组合全测因其他会话占用自检互斥锁而失败16项，保留在 `core-tests-integrated.log`；确认占用退出后 `core-tests-integrated-uncontended.log` 全绿。

组合代码自动缩放再验：398稳定采样、0坏帧、116 ambiguous；日志目录 `player-motion-probe-20261110-144521808-9736/logs`。此前8a68a6f工作树自包含发布验证547文件287.5MB通过，发布版缩放392稳定帧0坏帧（`player-motion-probe-20261110-143443520-22636/logs`）。

主树随后被另一界面会话接管合并，观察到其文档冲突和产品改动，未介入。当前工作树的组合代码独立发布已通过：547文件、287.5MB、批准mpv载荷60文件完整校验。最终发布程序自动缩放复验442稳定样本、0坏帧、112 ambiguous，日志目录 `player-motion-probe-20261110-145035129-24632/logs`；`work/resize-acceptance/publish-integrated.log` 留发布回执。

截至11月10日未写共同交付目录、未更新桌面快捷方式；该待办已由本报告顶部11月11日主树合并、重验与发布完成，不再适用于当前桌面版。

首次完整 motion 在时间轴截图遮挡守卫失败，未到缩放阶段，不宣称完整 motion 通过。专用缩放探针及真实鼠标路径提供本次受影响行为证据；验证期间若窗口移动导致取样几何不一致则保留为ambiguous，稳定遮挡仍失败。
