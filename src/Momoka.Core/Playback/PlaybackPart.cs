namespace Momoka.Playback;

/// <summary>One complete file within a continuous media timeline, without credentials.</summary>
public sealed record PlaybackPart(Uri MediaUrl, long DurationTicks);
