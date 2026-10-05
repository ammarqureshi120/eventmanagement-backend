using EventHub.Application.Common.Ports;

namespace EventHub.Api.Hosting;

/// <summary>
/// The HTTP caller (AD-26). Always anonymous in Story 1.3: cookie sign-in and the claims it reads
/// (<c>sub</c>, <c>role</c>, <c>org_id</c>) arrive in Story 1.4. Anonymous callers are denied by the matrix.
/// </summary>
public sealed class HttpCurrentUser : ICurrentUser
{
    public ActorKind Kind => ActorKind.Anonymous;

    public Guid? UserId => null;

    public string DisplayName => string.Empty;

    public Guid? OrganizationId => null;
}
