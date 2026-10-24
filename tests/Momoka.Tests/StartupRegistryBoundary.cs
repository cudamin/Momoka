using Microsoft.Win32;
using Momoka.Configuration;

namespace Momoka.Tests;

/// <summary>
/// Compile-time endpoints for the entire linked StartupRegistration source. No Microsoft.Win32.Registry
/// or WinRT method can be reached through the test-only namespace endpoints; all names/paths are checked before use.
/// Injected failures are contract inputs, not evidence of an ACL/deployment problem on this computer.
/// </summary>
internal static class StartupRegistryBoundary
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ApprovalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    internal static MemoryStartupRegistry CurrentUser { get; private set; } = new();
    internal static bool Packaged { get; set; }

    internal static MemoryStartupRegistry Reset()
    {
        Packaged = false;
        return CurrentUser = new MemoryStartupRegistry();
    }
}

internal sealed class MemoryStartupRegistry
{
    internal object? RunValue { get; set; }
    internal object? ApprovalValue { get; set; }
    internal bool RunExists { get; set; } = true;
    internal bool ApprovalExists { get; set; } = true;
    internal Exception? RunFailure { get; set; }
    internal Exception? ApprovalFailure { get; set; }
    internal Func<string, Exception?>? WriteFailure { get; set; }
    internal Action? AfterWriteAttempt { get; set; }
    internal int RunReads { get; private set; }
    internal int ApprovalReads { get; private set; }
    internal List<string> Writes { get; } = [];

    internal MemoryStartupKey CreateSubKey(string path)
    {
        RequireKey(path);
        Assert.Equal(StartupRegistryBoundary.RunKey, path, "production creates only its own Run key");
        RunExists = true;
        return new MemoryStartupKey(this, path);
    }

    internal MemoryStartupKey? OpenSubKey(string path, bool writable = false)
    {
        RequireKey(path);
        if (!writable)
        {
            if (path == StartupRegistryBoundary.RunKey)
            {
                RunReads++;
                if (RunFailure is { } error) throw error;
            }
            else
            {
                ApprovalReads++;
                if (ApprovalFailure is { } error) throw error;
            }
        }
        return (path == StartupRegistryBoundary.RunKey ? RunExists : ApprovalExists)
            ? new MemoryStartupKey(this, path)
            : null;
    }

    internal object? Read(string path, string name)
    {
        RequireValue(name);
        return path == StartupRegistryBoundary.RunKey ? RunValue : ApprovalValue;
    }

    internal void Write(string path, string name, object? value)
    {
        RequireValue(name);
        Writes.Add(path);
        AfterWriteAttempt?.Invoke();
        if (WriteFailure?.Invoke(path) is { } error) throw error;
        if (path == StartupRegistryBoundary.RunKey) RunValue = value;
        else ApprovalValue = value;
    }

    private static void RequireKey(string path) => Assert.True(
        path is StartupRegistryBoundary.RunKey or StartupRegistryBoundary.ApprovalKey,
        "the anonymous boundary refuses every other registry path");

    private static void RequireValue(string name) => Assert.Equal(StartupEntry.ValueName, name,
        "the anonymous boundary refuses every other registry value");
}

internal sealed class MemoryStartupKey(MemoryStartupRegistry registry, string path) : IDisposable
{
    internal object? GetValue(string name) => registry.Read(path, name);
    internal void SetValue(string name, object value, RegistryValueKind kind)
    {
        Assert.Equal(RegistryValueKind.String, kind);
        registry.Write(path, name, value);
    }
    internal void DeleteValue(string name, bool throwOnMissingValue)
    {
        Assert.False(throwOnMissingValue, "production deletion must keep the missing-value no-op");
        registry.Write(path, name, null);
    }
    public void Dispose() { }
}
