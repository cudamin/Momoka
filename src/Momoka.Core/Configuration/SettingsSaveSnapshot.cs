using System.Text.Json;

namespace Momoka.Configuration;

/// <summary>UI 线程捕获的不可变 JSON 与其编辑代次，不保留活设置的引用。</summary>
internal sealed record SettingsSaveSnapshot(long Version, JsonElement Document);
