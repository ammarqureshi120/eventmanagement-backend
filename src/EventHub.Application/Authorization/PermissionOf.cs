using System.Reflection;

namespace EventHub.Application.Authorization;

/// <summary>
/// Reads the static <see cref="IRequirePermission.Permission"/> of a message type once per type. Returns
/// null for a type that does not implement <see cref="IRequirePermission"/>, which the authorization
/// behavior treats as forbidden (fail closed).
/// </summary>
public static class PermissionOf<TMessage>
{
    public static Permission? Value { get; } = Resolve();

    private static Permission? Resolve()
    {
        if (!typeof(IRequirePermission).IsAssignableFrom(typeof(TMessage)))
        {
            return null;
        }

        var read = typeof(PermissionOf<TMessage>)
            .GetMethod(nameof(Read), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(TMessage));
        return (Permission)read.Invoke(null, null)!;
    }

    private static Permission Read<T>()
        where T : IRequirePermission => T.Permission;
}
