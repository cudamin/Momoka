# Emby 元数据编辑与媒体库弹窗回归

实现与运行命令见 [开发与验证](../../docs/开发与验证.md#emby-编辑元数据与媒体库弹窗2026-10-28)。

- `EmbyMetadataEditorTests` 执行实际 Core 编辑模型、固定来源保存服务及 EmbyClient，只替代匿名 HTTP／设置与凭据边界，不连接真实服务器。
- `DialogChromeTests` 检查媒体库具体弹窗类型、正文局部底色、库筛选／设置面板和库确认入口；保留 MP/default 基础样式。旧“全部派生类型都只能接同一基础样式”的合同因用户明确区分库与 MP 改为双范围合同，未删除旧覆盖。
- 专项参数 `emby-metadata-fixture` 登记以上测试，均属于完整 Core 套件子集；当前测试与 Core 编译成功后才能 `--no-build`。
- 原生 `metadata` 分区驱动实际 TextBox、CalendarDatePicker、ListView、Flyout、锁开关及保存／取消按钮；验证五主题圆角和正文／按钮区背景，不用属性断言替代截图。
- 列表 Add／Edit／Remove 及键盘上下移动均保持集合身份。原生拖拽入口已配置，运行探针执行上下移动，不冒称实鼠拖动／读屏／高对比覆盖。
- `Momoka.DetailDeleteCommandTests` 源链接实际 ConfirmDialog 与库删除操作，边界只增加 Style／资源取值，用现有匿名删除夹具核验安全结果；不是实际删除共享媒体。

未覆盖真实 Emby 的目录返回／保存／插件提供者、真人账号权限、真实共享元数据变化、外部网页启动与播放。不得为了验收自动拿用户媒体库当可写夹具。
