# 独占弹幕输入栏工作记录

日期：2026-10-25。独立工作树：`C:\Users\89400\.codex\worktrees\dandanplay-input\Momoka`；分支：`codex/dandanplay-input`，基点 `fa111f2`。

本子任务只修改 `assets/mpv-ui/scripts/uosc/` 必需文件及 `tests/Momoka.Tests/Lua/uosc-interaction-tests.lua`。Core、Shell、设置项与 HTTP 由交付会话整合。

顶部影片标题右侧增加文字输入栏；宽度不足先缩短标题，避开窗口按钮。显隐、能否发送、发送中状态及播放上下文由宿主传入。没有匹配弹幕库时保留输入提示，收起发送按钮。输入通过 Unicode 事件及已有剪贴板读取，最多 100 个 UTF-16 单位；聚焦期间暂停宿主键位并屏蔽已有播放器键位，打开菜单、窗口失焦和隐藏时恢复 IME 与键位。

发送只产生宿主消息，不访问网络。每次提交带上下文与唯一请求编号；连续回车只提交一次，匹配成功回执仅清除提交时未再修改的草稿。隐藏保留同上下文草稿，换片与上下文变化清除草稿并拒绝旧回执。

验证：`py tools/test-uosc.py --output artifacts/dandanplay-input-tests-verified` 退出码 0，46 项通过、0 失败、0 Lua 错误；包含新增 8 项输入、关联回执、迟到、菜单、失焦、点击与宽窄／缩放检查。`git diff --check` 通过。产物 `artifacts/dandanplay-input-tests-verified/results.json` 与 `source-hashes.json`。仅用随包 libmpv 的 Lua 运行时，假键盘、假属性、假时钟，无窗口、媒体、真实 HTTP 或真实用户设置。

未覆盖真实 Windows IME 候选窗、真实独占画面像素与系统键盘优先级；交付会话须完成相关窗口验证，不能以本次假输入测试代替真实窗口证据。
