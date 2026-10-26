// The extracted page resolves only the preinstalled anonymous session. No container is constructed.
namespace Microsoft.Extensions.DependencyInjection;

internal static class PermissionServiceExtensions
{
    internal static T GetRequiredService<T>(this IServiceProvider services) where T : class =>
        services.GetService(typeof(T)) as T ?? throw new InvalidOperationException("Missing anonymous fixture service");
}
