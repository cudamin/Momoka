namespace Momoka.Configuration;

/// <summary>详情页正文卡片的持久化偏好，标题来自当前目录，不随存档复制。</summary>
public sealed class DetailCardSetting
{
    public string Key { get; set; } = "";

    public bool Visible { get; set; } = true;
}
