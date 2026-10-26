using System.Security.Cryptography;

namespace Momoka.AtomicFileWriteTests;

internal sealed class ContractFailure(string message, Exception? inner = null) : Exception(message, inner);

internal static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition) throw new ContractFailure(message);
    }

    public static void Equal<T>(T expected, T actual, string message) =>
        That(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}；期望={expected}，实际={actual}");

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static void Bytes(byte[] expected, byte[] actual, string message)
    {
        if (!expected.AsSpan().SequenceEqual(actual))
            throw new ContractFailure($"{message}；期望 {expected.Length} bytes/{Hash(expected)}，" +
                $"实际 {actual.Length} bytes/{Hash(actual)}");
    }

    public static byte[] CompleteCandidate(IReadOnlyList<byte[]> candidates, byte[] actual, string message)
    {
        foreach (var candidate in candidates)
            if (candidate.AsSpan().SequenceEqual(actual)) return candidate;
        throw new ContractFailure($"{message}；不是任一完整候选，实际 {actual.Length} bytes/{Hash(actual)}");
    }

    // The public API promises IO/access failures, not native Win32Exception or localized wording.
    public static Exception IoFailure(Action action, string message)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return error; }
        catch (Exception error)
        {
            throw new ContractFailure($"{message}；异常类型不是 public IO/access 契约：" +
                $"{error.GetType().FullName}，HResult=0x{error.HResult:X8}", error);
        }
        throw new ContractFailure($"{message}；未抛错，不得放宽文件共享/ReadOnly 约束");
    }

    public static T Failure<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        catch (Exception error) { throw new ContractFailure($"{message}；实际异常 {error.GetType().FullName}", error); }
        throw new ContractFailure($"{message}；未抛出 {typeof(T).Name}");
    }

    public static void Preserved(FileState expected, string path, string message)
    {
        var actual = FileState.Capture(path);
        Equal(expected.Exists, actual.Exists, message + "存在性");
        Equal(expected.Attributes, actual.Attributes, message + "属性");
        Equal(expected.Length, actual.Length, message + "长度");
        Equal(expected.Sha256, actual.Sha256, message + "完整内容");
    }

    public static void DirectoryFiles(string root, params string[] expected)
    {
        var actual = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var ordered = expected.Select(Path.GetFullPath).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        That(ordered.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase),
            "目录文件集合不符（可能泄漏临时文件或丢失目标/备份）；期望=" +
            string.Join(",", ordered) + "；实际=" + string.Join(",", actual));
    }
}

internal sealed record FileState(bool Exists, FileAttributes? Attributes, long? Length, string? Sha256)
{
    public static FileState Capture(string path)
    {
        if (!File.Exists(path)) return new(false, null, null, null);
        var attributes = File.GetAttributes(path);
        var bytes = FileIo.ReadComplete(path);
        return new(true, attributes, bytes.LongLength, Check.Hash(bytes));
    }
}

internal static class FileIo
{
    public const FileShare SharedDelete = FileShare.ReadWrite | FileShare.Delete;

    public static FileStream OpenReader(string path, FileShare share = SharedDelete) =>
        new(path, FileMode.Open, FileAccess.Read, share, bufferSize: 1, FileOptions.SequentialScan);

    public static byte[] ReadComplete(string path)
    {
        using var reader = OpenReader(path);
        return ReadRemainder(reader);
    }

    public static byte[] ReadRemainder(Stream reader, byte[]? prefix = null)
    {
        using var output = new MemoryStream();
        if (prefix is not null) output.Write(prefix);
        var buffer = new byte[997];
        int count;
        while ((count = reader.Read(buffer)) != 0) output.Write(buffer, 0, count);
        return output.ToArray();
    }
}
