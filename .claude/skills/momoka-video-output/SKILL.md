---
name: "momoka-video-output"
description: "Momoka 视频输出三卡到 mpv 的选项落地、HDR/杜比语义、管线优先级、profile 恢复与运行期着色器切换。用于审查或修改 VideoSettings、MpvOutputOptions、HdrOptions、MpvProfiles、LibMpvPipelinePolicy、ShaderSwitch、画面菜单与视频设置；具体着色器门控和档位表另见 mpv-shader-quality。"
---

# Momoka — 视频输出与 HDR/杜比设置

规则归 [CLAUDE.md](../../../CLAUDE.md)，命令归 [开发与验证](../../../docs/开发与验证.md)。本技能说明设置落地与可验证边界，不另立授权或交付豁免。链的资源、许可、组合门控见 [着色器说明](../../../assets/shaders/README.md) 与 [mpv-shader-quality](../mpv-shader-quality/SKILL.md)。

## 优先级与显示事实

`PlaybackPlanner.BuildPlayerOptions` 顺序是 `MpvBaseline` → `MpvOutputOptions`（profile、视频/HDR、音频、字幕）→ 着色器前置参数；内置后端最后应用 `LibMpvPipelinePolicy`。后写覆盖前写，列表操作不能压成普通字典。

- 两条内置管线都锁 `vo=gpu-next`，Required 失败中止，不回退其他路径。图形接口 2026-10-11 起分家：集成管线锁 `gpu-api=d3d11`（合成交换链只存在于 D3D11 路径），独占管线跟随设置页「图形接口」（默认 `vulkan`，契约配 `gpu-context=winvk`；空 = 自动；d3d11 专属选项只在该路径钉出）。换片签名吃进这项设置，改了走重启。
- 设置页「渲染管线」行在视频输出「基础输出」卡最上面，仅内置后端显示；外部后端显示「视频渲染」。播放器分类为「播放器」卡（自定义 mpv.conf 随独占模式进出为第二张）；同日用户令「两个功能合在一起，播放后端为内置时候隐藏外置相关」，mpv.exe 路径与 IPC 通道两行并回播放器卡、仅外部后端构造，隐藏不清空偏好；再令「截图分区移到播放行为里」，播放行为分类分为「播放行为／资源筛选／截图」三张分区卡（媒体源排序与视频文件名筛选凑成资源筛选），左栏排序同步重排。用户于 2026-10-11 明确要求集成模式保留「图形接口」，但不要下拉框：用 `SettingFactRow` 只读显示 D3D11；独占与外部后端恢复可编辑入口。此为用户指定的展示例外，不扩展到其他无效设置。不能把强制有效值写回保存的接口偏好。
- 基础输出通过 after 回调重建卡片以更新从属行：音频同步隐藏插值；未启用插值隐藏算法；只有有效显示同步才显示高帧率回退，回退关闭隐藏阈值；关闭抖动隐藏位深；去色带关闭或继承预设隐藏强度。内置 `fast` 的 `dither=no`，抖动继承该预设时也隐藏位深，画质预设切换须联动基础卡；外部预设内容未知，不套用此推断。隐藏不清空偏好。`ShellNavigationProbe.Settings.cs` 覆盖真实控件切换、只读展示及宽窄布局。
- `MpvRenderCheck` 按真正会跑的值问：集成按契约强制值（pipelineOwned），独占与外部按设置存值。不建议用户修改集成模式固定的接口。hwdec 不受管线锁定；出厂默认为 `auto-copy-safe`。硬解目录合并了空值「不指定」和 `no`「关闭」；Normalize 将旧空值/未知值归一为 `no`，保持软解行为，其余合法选择保留。随包内核中 `auto-safe` 是 `auto` 的别名，`auto-copy-safe` 是 `auto-copy` 的别名，目录分别只列一个；原生与复制模式有实际差异，不合并。
- `SettingsMigration.Choice` 大小写不敏感地匹配，但必须返回目录的规范拼法。随包内核拒绝 `YES`、`Spline` 等大小写变体，原样保留会导致起播拒绝或条件规则失效。
- 视频三卡统一为“下次播放生效”。本次着色器偏好由 `ShaderAutomationSettings.Snapshot` 固定；候选源共用快照，换屏只重算尺寸，不顺带采用后来修改的设置。

## profile 与切链恢复

`profile=fast/high-quality` 只是一个名字。恢复必须读运行内核的 `profile-list`，由 `MpvProfiles.Expand` 展开真实选项；缺 profile、循环或无效 JSON 不可当空配置。`default` 不额外套预设。

`PlaybackService.SetShaderGroupAsync` 的完整边界：

1. 固定句柄与播放代次，串行处理本服务的切链。
2. 检查目标文件；读取预设与必要 `option-info/<名>/default-value`，不把读取失败替成常量。
3. 收齐切换前状态。`glsl-shaders` 必须经 `GetStringListAsync` 读无损列表：内置 mpv_node 数组、外部 IPC JSON。普通格式化文本会丢分号转义，不能原样回写。
4. 前置选项先设，`glsl-shaders` 最后设；核对每条返回。失败尝试恢复切换前状态，代次变化后停止写入及回滚。
5. 回滚失败置 `ShaderStateKnown=false`，菜单/信息页显示未知，不继续把旧档位当已确认。下一次完整应用可恢复已知状态。

文件列表以 Windows 分号分隔，普通反斜线是路径字符，不能像逗号列表一样全部加倍。空列表及 `set glsl-shaders ""` 读回的单个空项均不表示读取失败；`null` 才是不可读。

UI 的切链与画面菜单按提交次序共用任务队列，不静默丢掉切换期间的操作；执行和提交时核对播放代次。“恢复自动”是请求模式，出队时读取当前方案；恢复写入中途换屏则成功后补交最新方案，失败保留原手动状态。启动后的当前档位及菜单源轴从服务最终候选的 `LaunchShaderDecision`/`PlayingSource` 采纳，不能继续沿用失败源。集成菜单静态部分可缓存，动态着色器子树每次打开重建；每棵单选组隔离，不能与其他菜单共用默认组。

**成功只表示配置完整下发，不表示 GLSL 已编译或执行。** 文件缺失预检、原生日志、`vo-passes` 分别覆盖不同问题。同句柄换片还要核对后端实际执行点的代次与 `InlineSwitch` 错误传播；服务 await 前后检查不等于原生命令具备原子代次保证。

## HDR 与杜比

- HDR 源且模式为 tonemap/passthrough 时接管目标空间，启动选项不让自动 ICC 覆盖它。
- 映射到 SDR：`target-trc=bt.1886`、`target-prim=bt.709`，不套用 HDR `target-peak`。
- HDR 输出：hint=auto、hint-mode=target，按显示器能力映射；不强制 `tone-mapping=clip`。显示设备是否真正收到 HDR，不能靠选项接受证明。
- 四个独立亮度：target-peak、hdr-reference-white、sub-hdr-peak、image-subs-hdr-peak。内核只接受10–10000的**整数 nits**，包括带 `.0` 的文本也拒绝；`ClampNits` 在存储、控件与下发端统一四舍五入。`hdr-contrast-recovery` 是0–2小数，不能沿用整数规则。图形字幕的自动默认当前为1000；参考白可能覆盖 SDR 输出目标峰值，不等于显示器背光。
- 杜比/HDR10+ 关闭项组成单条带标签 `vf=@momoka-hdr:format=...`。关 RPU 不等于 HDR10 回退；Profile 5 可能偏色；增强层开关不是全部 Profile 7 FEL 已支持的证明。
- 临时菜单“输出模式”成套切换目标曲线、色域、hint、峰值和 ICC，不再用单个 cycle 冒充 HDR 直通。切模式将峰值恢复自动并关闭 ICC，提示必须说明；不改全局设置，也不改参考白/字幕亮度/映射算法。

## 其他容易冲突的选项

- 抖动算法与 dither-depth 独立；继承 profile 可能得到 dither=no，不等于强制关闭全部抖动。
- hdeband 由高度≤576的老片源轴决定，要求 `deband=no`。运行期菜单同样要尊重 `ShaderChainRules.ConflictsWith`，不能让开关打破互斥。
- 插值和同步由 `ResolveSync` 一处决定；音频同步下显式抑制插值，高帧率/高刷新率回退要用实际起播输入。
- 独占先 Detach 播放页，显示器尺寸/刷新率委托仍属于宿主窗口，不能随页面一起清空。外部独立视频窗口跨显示器的读数覆盖需另验，主窗显示器不是原生视频窗的实测值。

## 五层验证

1. **规则**：当前编译的 `VideoOutputTests`、`ShaderTransactionTests`、`PlaybackTests`、`SettingsTests`、`InlineSwitchTests`、`MpvUiTests`。覆盖先红拒绝/异常、快照、profile、列表、门控和代次，不只正常返回。
2. **随包内核**：无媒体句柄隔离 config/scripts/audio/video，问默认值、候选和范围；进一步用真实 `LibMpvHandle` + 服务核对列表、切链和恢复。退出0必须配断言结果。
3. **真实控件离线宿主**：临时默认数据，不走普通 App 启动/DI/登录；需要会话时使用拒绝 HTTP 的处理器及禁止播放的服务。检查三卡、自动/手动输入、失焦/清空、保存失败重试、后端切换、独占 Detach 和实际候选接线。忽略的 `work/` 宿主是本机证据，不是干净检出的产品入口。
4. **实际画面与操作**：官方 Computer Use 语义定位。宽窄真实控件；本地已核实位深/色域/帧率的素材，固定输出尺寸、时间点，记录 `vo-passes`、日志和截图。命令接受、pass存在、像素变化、耗时不是同一种结论；不代替真实 HDR 屏/杜比素材验收。
5. **项目门禁**：按 CLAUDE 构建、格式、测试、发布与适用自检。发布对 GLSL/HOOK/许可文本逐文件哈希；`tools/test-shader-publish.ps1` 验缺失/损坏/多余。普通自检可能登录读库，未跑照实列未跑；隔离发布不刷新主树快捷方式。

`--show-settings 视频输出` 是普通启动，**不是离线入口**；`--dump-ui` 仅自检消费。不能操作真实设置后整份恢复来假装隔离。三条播放探针固定集成，不覆盖独占/uosc或外部真实进程。

## 来源

选项语义先问随包内核；版本、上游配置和其他播放器参考的边界见 [references/sources.md](references/sources.md)。当随包 DLL 版本变化，重新核对钉版文档、profile 与所有新增候选值，不把本地 mpv.conf 等同于上游当前配置。
