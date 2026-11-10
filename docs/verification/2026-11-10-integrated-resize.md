# 集成播放窗口缩放验收

范围：本地静音六彩条视频、内置 libmpv 集成管线。没有登录真实媒体服务器，没有播放真实库或上报观看记录。正常片源信箱边、独占/uosc、外部 mpv、HDR 色调没有在这次验收中覆盖。

## 复现与修复

旧代码先改变宿主 HWND/XAML 岛尺寸，随后写入视频合成属性；窗口与合成提交间可显示上一尺寸的视频。独立后台取屏在右下连续反向拖动中记录109个稳定样本、11个露底帧；截图右侧及底部露底与用户报告相符。提前请求异步提交仍有9帧露底。

修复在首个实际尺寸意图后暂停并异步抓取当前帧；首帧就绪前保留旧窗口并合并最新目标，之后由原生帧承接层随目标尺寸更新。松手待新视频缓冲稳定后撤层并恢复。纯移动不暂停、不抓帧；旧收尾不能撤掉新拖动或新收尾仍持有的帧。

## 最终证据

- 当前 Release Shell：0警告、0错误。
- 当前编译的主测试：4537/4537通过，0失败、0跳过；`work/resize-acceptance/core-tests-delivery.log`。
- `Momoka.ResizeHandoffTests` 16项、`Momoka.PlayerInputRecoveryTests` 18项、`Momoka.PlayerMotionProbeTests` 23项通过；各套可能重叠，不累加。回执：`artifacts/regressions/20261110-142556219-fff40dc7e7d24f428b9c71c4c5e89bfa/summary.json`。
- 新源链接回归的三个隔离源码负例分别撤掉异步让步、正确交接责任、待目标回原更新；各准确触发对应失败，再用真实源码通过。
- 最终3px近边采样自动测试（等号命令行写法）：右下221、左上232个稳定样本，坏帧0；另有121帧因取屏期间尺寸变化标为ambiguous，不计通过。日志目录：`%LOCALAPPDATA%/Momoka/logs/player-motion-probe-20261110-142817184-23288/logs`。
- 最终3px真实鼠标测试：4次系统拖边事务、18次尺寸事件，1982稳定样本、0坏帧、18 ambiguous；暂停/恢复与撤层通过。日志目录：`%LOCALAPPDATA%/Momoka/logs/player-motion-probe-20261110-142926880-9720/logs`。录像：`work/resize-acceptance/manual-final.mp4`，已检查取出的连续截图。
- 行尾/BOM/空白检查通过。四种缺参、空值、URL拒绝的批量运行命令被自动审批策略阻止，未执行、不计通过。

## 未完成交付项

主树另一会话仍在修改 `ShellNavigationProbe.Collections.cs` 并进行验收，本轮不并发写入主树或刷新共同桌面快捷方式。当前证据属于 `codex/integrated-resize-smooth` 工作树；待整合后须对主树最终代码重验和发布。桌面快捷方式仍是旧版。

首次完整 motion 在时间轴截图遮挡守卫失败，未到缩放阶段，不宣称完整 motion 通过。专用缩放探针及真实鼠标路径提供本次受影响行为证据；验证期间若窗口移动导致取样几何不一致则保留为ambiguous，稳定遮挡仍失败。
