using Microsoft.Extensions.DependencyInjection;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    bool IShellActions.FnNasRemovalAvailable => _services?.GetService<Momoka.Shell.Services.FnNasLibraryRemovalService>() is not null;
    Task<Momoka.FnNas.FnNasRemovalReceipt> IShellActions.RemoveFnNasLibraryItemAsync(Momoka.Emby.EmbySessionScope scope, Momoka.Emby.EmbyItem item,
        IReadOnlyList<Momoka.FnNas.FnNasVersion> versions, string? mediaGuid, CancellationToken cancellationToken) =>
        (_services?.GetService<Momoka.Shell.Services.FnNasLibraryRemovalService>()
            ?? throw new InvalidOperationException("飞牛移出能力尚未附加")).RemoveAsync(scope, item, versions, mediaGuid, cancellationToken);
}
