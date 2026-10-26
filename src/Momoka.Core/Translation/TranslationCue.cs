namespace Momoka.Translation;

/// <summary>按文件代次、句代次和原句严格匹配，不能只比较译文或文本哈希。</summary>
public readonly record struct TranslationCue(long FileGeneration, long Revision, string Text);
