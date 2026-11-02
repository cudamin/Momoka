namespace Momoka.Configuration;

/// <summary>弹幕来源模式：只弹弹play、只用户自部署的 danmu_api，或两者合并。</summary>
public enum DanmakuSourceMode
{
    DandanPlay = 0,
    DanmuApi = 1,
    Both = 2
}
