using EventHub.Application.Authorization;
using EventHub.Application.Common.Ports;

namespace EventHub.Api.IntegrationTests.TestApi;

/// <summary>Test-only permissions; never granted in the shipped app.</summary>
public static class TestPermissions
{
    public static readonly Permission Run = new("Test.Run");

    /// <summary>Granted to nobody.</summary>
    public static readonly Permission Ungranted = new("Test.Ungranted");
}

/// <summary>Grants <see cref="TestPermissions.Run"/> to System Administrators (Platform) and Org Administrators (Tenant).</summary>
public sealed class TestGrants : IPermissionGrantSource
{
    public IEnumerable<PermissionGrant> Grants =>
    [
        new(ActorKind.SystemAdministrator, TestPermissions.Run, ScopeKind.Platform),
        new(ActorKind.OrgAdministrator, TestPermissions.Run, ScopeKind.Tenant),
    ];
}

/// <summary>Records which pipeline stages ran, so tests can prove what did not run.</summary>
public sealed class TestProbe
{
    private int _validatorRuns;
    private int _handlerRuns;

    public int ValidatorRuns => Volatile.Read(ref _validatorRuns);

    public int HandlerRuns => Volatile.Read(ref _handlerRuns);

    public void ValidatorRan() => Interlocked.Increment(ref _validatorRuns);

    public void HandlerRan() => Interlocked.Increment(ref _handlerRuns);
}
