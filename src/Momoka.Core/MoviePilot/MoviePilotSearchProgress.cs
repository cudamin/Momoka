namespace Momoka.MoviePilot;

/// <summary>当前搜索流里的服务端进度；未知比例用 null，不以经过时间推算。</summary>
public sealed record MoviePilotSearchProgress(double? Value, string Text);
