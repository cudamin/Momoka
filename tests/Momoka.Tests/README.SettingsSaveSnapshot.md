# 设置保存快照与并发编辑

`SettingsSaveSnapshotTests` 直接执行实际 `SettingsStore`、`SettingsService`、`RecentSearch` 和原子文件写入。所有新增用例只使用匿名文档与本测试输出目录，凭据保护器若被调用立即失败，不登录、不联网、不读取真实设置。

当前 Release 测试与 Core 编译成功后运行：

```powershell
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- settings-storage-fixture
```

此入口执行完整 `SettingsTests`，包含本次新增五项；它们也包含在主套件中，不重复计数。另运行 `Momoka.SettingsPersistenceTests`，保持保存失败、共享欠项、通知排序和改密页本地重试覆盖。

- 保存只规范化独立的当前形状快照，不改变活设置的数值、对象和集合引用；磁盘仍是规范化后的文档。
- 快照直接反序列化当前形状，不调用 `FromJson` 的旧版本迁移。旧 Schema 标签不会清除当前窗口尺寸；只更新磁盘文档的版本。
- 一次性字典 comparer 在旧实现的 `Normalize` 写回查找点调用真实 `RecentSearch.Remember`，确定性模拟捕获旧 entries 后的 UI 编辑。旧实现随后覆盖新搜索；新实现不查询或写入活字典，不会调用该钩子。它只是测试边界，没有增加生产开关。
- 实际 `AtomicFile` 的现有提交锁只在测试中反射持有；观察到完整临时 JSON 后编辑搜索并发起后续保存，再释放锁。检查当前内存、最终主文件均是新搜索，备份为前一份完整旧快照。
- 真实文件锁造成提交失败时，旧文件、活编辑和欠项保留，后续保存恢复。所有等待均有超时，清理先验证路径属于本测试输出。

该修复消除存储层的活对象回写，不把所有设置编辑变成全局事务。捕获期间的集合结构并发修改仍可能令序列化失败，沿现有保存失败提示处理；后续保存会重新读取当前值。加载与显式导入/恢复仍就地规范化。未验证真实 UI、生产账号重认证或 DPAPI。
