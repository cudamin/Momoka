using Momoka.Shell.Services;

namespace Momoka.Shell.ViewModels;

public sealed partial class FnNasLibrariesViewModel
{
    internal FnNasLibraryRemovalService? RemovalService { get; private set; }
    internal void UseRemovalService(FnNasLibraryRemovalService service) => RemovalService = service;
    internal void ReportRemovalFailure(Exception error) => Report("打开已移除文件失败", error);
    internal (Momoka.Emby.EmbySession Session, string SourceKey, FnNasLibraryRemovalService Service)? RemovalContext()
    {
        if (!CanUse || Session is null || Scope is null || RemovalService is null) return null;
        return (Session, Scope.Key, RemovalService);
    }
}
