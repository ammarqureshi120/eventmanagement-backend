namespace EventHub.Domain.Common;

/// <summary>
/// Aggregates with an app-managed optimistic concurrency <c>Version</c> (AD-24). The persistence SaveChanges
/// interceptor bumps it on every update and EF uses it as the concurrency token, so a stale write fails with
/// <c>concurrency_conflict</c>. Feature code never sets it.
/// </summary>
public interface IVersioned
{
    int Version { get; }
}
