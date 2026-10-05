using EventHub.Application.Common.Ports;
using Microsoft.AspNetCore.Http;

namespace EventHub.Api.IntegrationTests.Fixtures;

/// <summary>
/// Test-only <see cref="ICurrentUser"/>: actor and data scope come from the <c>X-Test-Actor</c> header
/// (<c>sysadmin</c>, <c>orgadmin:&lt;orgId&gt;</c>, <c>eventmanager:&lt;orgId&gt;</c>, <c>system</c>; absent = anonymous).
/// The scope then follows from the production <c>CurrentUserTenantContext</c>.
/// </summary>
public sealed class TestActor(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string Header = "X-Test-Actor";

    public static readonly Guid SysAdminId = Guid.Parse("0f0e0d0c-0b0a-0908-0706-050403020101");
    public static readonly Guid OrgAdminId = Guid.Parse("0f0e0d0c-0b0a-0908-0706-050403020102");
    public static readonly Guid EventManagerId = Guid.Parse("0f0e0d0c-0b0a-0908-0706-050403020103");

    private (ActorKind Kind, Guid? UserId, string Name, Guid? OrganizationId)? _parsed;

    public ActorKind Kind => Parsed.Kind;

    public Guid? UserId => Parsed.UserId;

    public string DisplayName => Parsed.Name;

    public Guid? OrganizationId => Parsed.OrganizationId;

    public static string SysAdmin => "sysadmin";

    public static string OrgAdmin(Guid organizationId) => $"orgadmin:{organizationId}";

    public static string EventManager(Guid organizationId) => $"eventmanager:{organizationId}";

    private (ActorKind Kind, Guid? UserId, string Name, Guid? OrganizationId) Parsed => _parsed ??= Parse();

    private (ActorKind, Guid?, string, Guid?) Parse()
    {
        var raw = accessor.HttpContext?.Request.Headers[Header].ToString() ?? string.Empty;
        var parts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
        Guid? org = parts.Length == 2 && Guid.TryParse(parts[1], out var parsed) ? parsed : null;
        return parts[0].ToLowerInvariant() switch
        {
            "sysadmin" => (ActorKind.SystemAdministrator, SysAdminId, "Test SysAdmin", null),
            "orgadmin" when org is not null => (ActorKind.OrgAdministrator, OrgAdminId, "Test OrgAdmin", org),
            "eventmanager" when org is not null => (ActorKind.EventManager, EventManagerId, "Test EventManager", org),
            "system" => (ActorKind.System, null, "System", null),
            _ => (ActorKind.Anonymous, null, string.Empty, null),
        };
    }
}
