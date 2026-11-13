namespace Momoka.Emby;

/// <summary>远程图片筛选由服务器按条目的元数据语言和图片来源执行。</summary>
public sealed record RemoteImageSearch(string? ProviderName = null, bool IncludeAllLanguages = true);
