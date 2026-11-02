using EventHub.Application.Common.Ports;

namespace EventHub.Api.IntegrationTests.Fixtures.Arrange;

/// <summary>
/// AD-31: lets an Arrange hook dispatch real commands through the production Mediator as the System actor, outside
/// any HTTP request. Inside <see cref="AsSystem"/> (an async-local flag on the test's own flow) every
/// <see cref="ICurrentUser"/> resolved by the host is System; HTTP requests never see the flag.
/// </summary>
public static class ArrangeActor
{
    private static readonly AsyncLocal<bool> System = new();

    public static bool IsSystem => System.Value;

    /// <summary>Acts as System until the returned scope is disposed.</summary>
    public static IDisposable AsSystem()
    {
        System.Value = true;
        return new Reset();
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose() => System.Value = false;
    }
}

/// <summary>The host's <see cref="ICurrentUser"/>, except System while <see cref="ArrangeActor.AsSystem"/> is active.</summary>
public sealed class ArrangeAwareCurrentUser(ICurrentUser inner) : ICurrentUser
{
    public ActorKind Kind => ArrangeActor.IsSystem ? ActorKind.System : inner.Kind;

    public Guid? UserId => ArrangeActor.IsSystem ? null : inner.UserId;

    public string DisplayName => ArrangeActor.IsSystem ? "System" : inner.DisplayName;

    public Guid? OrganizationId => ArrangeActor.IsSystem ? null : inner.OrganizationId;

    public DateTime? SessionIssuedAtUtc => ArrangeActor.IsSystem ? null : inner.SessionIssuedAtUtc;
}
