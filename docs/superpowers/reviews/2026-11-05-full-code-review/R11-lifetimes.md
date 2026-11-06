# R11 后端、管线与资源边界

对应HEAD12e99d8；这是源码所有权图及本轮证据边界，联[R10生命周期](R10-lifetimes.md)。

| 后端 × 管线 | 实际画面/输入 | 本轮运行证据 | 缺口 |
| --- | --- | --- | --- |
| libmpv × 集成 | CompositionVideoTarget交换链，HostWindow/PlayerPage输入；原生键盘关闭，不装uosc | 参数/纯规则/完整暂停方法替身；真实DLL无initialize配置解析 | WinUI/交换链/DPI/fullscreen/键鼠未运行 |
| libmpv × 独占 | mpv顶层VO窗口，uosc输入；可retain/Swap同窗 | 真实随包LuaJIT的完整uosc49+8夹具和2失败反例 | 实际native VO/自动媒体连播未运行 |
| 外部mpv × 集成设置 | 始终外部窗口；设置里的管线档位不改变后端声明 | 正式MpvProcessTests假子进程/PID/argv/管道/退出 | 真正用户mpv版本、窗口/input.conf未运行 |
| 外部mpv × 独占设置 | 同上；不是内置headless/uosc | 同一假子进程证据，不重复计数 | 不能由内置uosc证明外部UI |

| 资源/异步路径 | 所有者、失效和释放 |
| --- | --- |
| 原生DLL绑定 | NativeModuleBinding成功后固定绝对路径/进程生命周期；失败不钉死 |
| libmpv context、事件线程 | handle/gate关闭准入；Stop/Dispose公开3秒上限，后台清理先等gate/线程退出再destroy；旧40项沿R10，不重复 |
| mpv node/frame | 命令门内复制托管值，finally释放node/string；VideoFrame拥有独立像素，无借用指针跨await |
| Composition交换链 | callback借指针时AddRef，pending仅保留最新；dispatcher失败/Dispose释放pending；attached/brush/visual收尾释放 |
| Composition timer/host订阅 | target创建订Loaded/Unloaded/Size/Root，Dispose退订并停止；退场retain有保险丝，本轮未量实机 |
| inline场次 | native FileGeneration在HandOver/retain/Swap推进，旧命令票拒绝；脚本控件自身拖动未清，R11-002 |
| 外部进程和IPC | idle无媒体/头部argv启动，管道PID验证后才发认证/字幕/loadfile；startup通道不可用则失败，无凭据回退；process退出与最终reply各有边界 |
| 外部request TCS/reader | request-id pending，timeout/cancel移除，断线失败pending；Dispose关闭reader/process；假子进程仅测试协议契约 |
| uosc menus/input | snapshot-id:index与VM交互context；file边界清menu/picture，Speed/Volume手势缺失R11-002；延时Button未决 |
| 拖边暂停 | page resize generation保护拖动接管；恢复又依UI交互context，首次NowPlaying造成R11-001 |
| 窗口交接暂停 | HandoffPause责任应按实际场次；首次UI发布相同边界需修复回归，不能只改方法名 |
| watchdog | worker判断Attached/onStage，排队回调仅验watchdog；下台后可复活ticker，仅额外轮询观察，后果未升缺陷 |
| Win32 hook/class cursor/taskbar | delegate有存活字段，退出/销毁退hook、还任务栏、类cursor/计数；InputCursor包装有意进程级引用，未据此报泄漏 |
| stats页面 | 脚本拥有timer/offset/native bindings，start-file关闭；集成键归Shell缺转发，R11-004 |

三条集成本地媒体探针本轮未运行，且即使通过也不覆盖独占/uosc或真正外部后端。JSON/字体、托管替身与真实窗口分别记录，不能互相替代。
