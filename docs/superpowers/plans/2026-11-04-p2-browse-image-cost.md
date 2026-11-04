# P2 浏览恢复与详情图片成本实施计划

> **For agentic workers:** Use executing-plans task by task; preserve measurements before optimizing.

**Goal:** 补齐暖恢复和详情背景处理的测量，选择一项实测成立的优化并完成主树交付。

**Architecture:** 复用 NavigationTiming 的匿名阶段与现有真实 VM／WinUI 探针。先记录恢复、发布、主动布局和背景处理的分段时间，再按证据缩小实现范围，不重做已有稳定集合。

**Tech Stack:** WinUI 3、.NET 10、C#，现有 WinRT 图像解码和 BMP 交接。

**Spec:** [导航延迟方案](../../页面切换与封面响应延迟审查及优化方案-2026-11-04.md)，第 4 节 P2、第 9.4 节。用户 2026-11-04 已确认从复测和补计时开始，选择一小片实施。

## Global Constraints

- 基线 master `834a21d`；实施树 `C:/Users/89400/.codex/worktrees/ba08/Momoka`，分支 `codex/p2-browse-image-cost`。
- 不改变页面可见布局、Busy／IsReady、图片方向／sRGB／裁切语义、转场及网络并发策略。
- 匿名计时只记录固定阶段名、数量和线程，不包含来源键、条目名、地址或凭据；关闭时不做统计遍历。
- 恢复和发布跨度可能嵌套，不相加冒充总时间；Library 当前 NewCard 与 Add 合计，不为计时改顺序。
- 回调保存本次页面的 run，Release 清除；真实库仅导航读取，不播放或写入。桌面与诊断锁串行。
- 本片不增加无证据的全局图像缓存；若选择缓存，必须另补字节预算、来源失效和迟到填充测试。

## Review Focus

- 暖恢复命中、未命中、异常和计时关闭均保留原控制流。
- 事件回调不能把上一页工作记给下一次点击；布局时间与首 Rendering 不冒充可见转场。
- 图片取消、旧访问和异常不能发布迟到图，也不能吞掉取消。
- 图像处理保留 EXIF、sRGB 和源像素不变；XAML 图像创建仍在 UI 上下文。
- 对比保留相同运行条件及原始失败；不得将 managed 测试或离线图片声称为真实库全部性能。

### Task 1: 补齐阶段计时并复测

**Files:** HomeViewModel、LibraryViewModel、HomePage/Rows、LibraryPage、HeroPictureLoader、现有导航／主页／媒体库测试、performance 探针。

**Interfaces:** 沿用 NavigationTimingRun.Mark(stage, count)，不改变事件格式。

- [x] 核实树、主树和桌面占用；当前 Core／主测试 Release 编译 0 警告错误，基线主测试 3664/3664。
- [x] 在真实 VM 暖恢复用例先断言恢复整体、卡片批次和货架发布标记；观察缺标记失败。
- [x] 加恢复／发布、主动布局和 hero copy／decode／blur／encode／XAML 交接的配对计时。
- [x] 运行真实 VM 专项和实际 performance 检查；故意缺阶段的校验输入应被拒绝。
- [x] 同一路径得到详情24次、媒体库24次、主页23次；原始首访与每次事件保留，正式比较应另拆首访。
- [x] 按测量选择详情背景有限复用；实际结果和边界如下。

### Task 2: 实施选中的最小优化

**Interfaces:** 沿用 Task 1 的同一测量阶段，比较同一工作而非改日志定义。

- [x] 写覆盖瓶颈和边界的失败回归，确认未改产品代码时失败。
- [x] 仅消除已确认的重复工作或移出明确不依赖 UI 的计算。
- [x] 回归通过后，在相同条件复测；保留原始报告及截图，量化收益和未解决范围。
- [x] 独立审查整片差异，无P1/P2阻断；提交产品与测试。

### Task 3: 整合与交付

- [x] 分支当前主测试及适用专项、运行检查通过，更新方案及 PROGRESS。
- [ ] 核实主树无他人写入后合入，按最终整合代码重验。
- [ ] 主树发布到 artifacts/publish/win-x64，核对快捷方式与哈希，不上传远端。
- [ ] 核对独有内容、证据保留后清理分支；本聊天主检出受应用保护，保留干净目录，不绕过删除。

## 执行记录

- 2026-11-04：基线 restore、Core/Tests build、主测试完成。证据在 `work/p2-browse-image-cost/`。规则和方案已重读；原方案是路线，具体选择由本轮测量决定。
- Ruling: 先测恢复批次合计及详情后台边界，不为了隔离纯构造耗时重新排列 NewCard/Add。这样不扰动原始性能路径，但不能拆出每个 Add 的绑定成本。
- Task 1：主页18/19、媒体库37/38因缺恢复标记失败，补后19/19、38/38。计时守卫15/15、基础计时9/9，HomeFold56/56、SearchLifetime42/42。Shell当前编译0警告错误。performance首次6/7因探针未关闭写入句柄再File.ReadAllLines而失败，修正后7/7退出0（`shell-probe-20261104-081953702-6316`），新warm检查8跨度／2次快照命中／旧页不借新run。
- 实测：同名PRI的本地P2Measure宿主调用实际Program.Run，持普通实例及诊断锁、SelfCheckRun设置副本；1422×800物理窗口、96DPI，无录屏、UIA标题祖先Invoke。原设置与备份退出后哈希不变。前两次宿主资源解析失败和一次循环标题未实现失败单列保留；24/24/23是NDJSON实际完整导航数。此宿主与普通Momoka.exe入口有差异，不能直接与P0绝对值相减。
- 基线中位：Home恢复0.36ms、Library恢复0.17ms；Home混排第二次UpdateLayout总计约0ms。详情背景stream0.22ms、decode4.51ms、像素复制0.93ms、render副本0.49ms、BMP编码3.76ms、XAML SetSource至完成197.82ms。最后一项含等待与UI续体调度，不是UI CPU；24次同图均重新处理。全体首Rendering中位详情171.28ms／库443.35ms／主页275.98ms，离群与首访均保留，不能声称现阶段已提速。
- Ruling: Task 2选详情背景原像素＋最终位图的有限复用，24MiB共享预算，每张原图只保留最近半径结果；来源登录owner、原图片owner/type/tag、解码宽度和半径精确匹配。来源退出主动剔除、手动清除代次拒迟到写回，结果发布继续守访问和取消。不做整页缓存，也不为了近零耗时删除第二次布局。代价是增加最多24MiB缓存成本；暂时分配和XAML内部表面不在该记账内。
- 证据：`work/p2-browse-image-cost/baseline-summary.json`及`baseline-live-data-3/logs/navigation-timing-20261104-082104868-34248/navigation-timing.ndjson`；Shell SHA256 `8DFFF5F1E24700C28A35E428747D469940AE70B58529AA7D1F22842F545035C7`。重叠或不完整跨度拒绝统计；不报告全站内存命中率。

- Task2运行：performance7/7退出0（shell-probe-20261104-085959315-34084）；30次真实Frame同图29次复用，7,372,800字节保持，标签换图、实际设置Clear及新半径重糊拒回填通过。ImagePerformance48/48+原22/22、PageContract116/116、SettingsLifetime55/55。
- 回归额外发现共享坏缓存恢复：首个恢复等待取消，普通miss加入者成功解码却未存盘；控制前置后稳定红（总GET 3，应2）。用弱引用标记未持久化恢复字节，任一成功解码者走原受守卫的回填路径，最终48/48。假HTTP同时修为每次新建response，不复用已Dispose的Content；此前强制双坏decode会缩窄覆盖的尝试已撤回。
- 最终独立审查无P1/P2。P3保留：多个普通miss加入坏图恢复且都成功解码时，原guard允许同字节重复写盘/计数；不覆盖不同的新缓存、不保存未验证数据，未测到本轮正常暖访问瓶颈，避免继续扩大本片。
- 最后编译0警告0错误，图片专项48/48+原22/22；主测试串行3665/3665。最终格式文件检查通过，format工具退出0但报告工作区加载warning。同条件23次warm背景中位279.483→63.004ms、23次全命中；首Rendering只171.205→161.128ms且p95更差，保持限定结论。
