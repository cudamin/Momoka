namespace Momoka.Danmaku;

/// <summary>一次请求具体走哪一个弹幕服务；合并模式会分别发起这两种请求。</summary>
public enum DanmakuSource
{
    DandanPlay = 0,
    DanmuApi = 1
}
