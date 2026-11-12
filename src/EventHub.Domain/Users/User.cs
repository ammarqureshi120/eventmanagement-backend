using EventHub.Domain.Common;

namespace EventHub.Domain.Users;

/// <summary>
/// The one User aggregate (AD-26): profile, role, status and the V1 single-Organization link.
/// Credentials (password hash, security stamp, lockout) are not part of the Domain: they are EF shadow
/// properties on the same <c>Users</c> row, touched only by the Identity stores behind <c>IIdentityAccount</c>.
/// </summary>
public sealed class User : IHasDomainEvents, ITimestamped, IVersioned
{
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 256;
    public const int PhoneMaxLength = 32;

    private readonly List<IDomainEvent> _domainEvents = [];

    private User()
    {
        // EF Core materialization.
    }

    public Guid Id { get; private set; }

    /// <summary>Null only for a System Administrator (DB CHECK <c>CK_Users_Role_OrganizationId</c>).</summary>
    public Guid? OrganizationId { get; private set; }

    public UserRole Role { get; private set; }

    public UserStatus Status { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    /// <summary>The sign-in identity; not editable in V1 (AD-16).</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>Platform-wide unique (<c>UX_Users_NormalizedEmail</c>, AD-19).</summary>
    public string NormalizedEmail { get; private set; } = string.Empty;

    /// <summary>App-managed optimistic concurrency version (AD-24).</summary>
    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    /// <summary>
    /// A System Administrator provisioned from configuration (FR37): Active, no Organization, no password.
    /// The id comes from <c>IIdGenerator</c> (AD-13). Missing names become empty strings.
    /// </summary>
    public static User CreateSystemAdministrator(Guid id, string email, string? firstName, string? lastName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A User id is required.", nameof(id));
        }

        var trimmedEmail = RequireEmail(email);
        return new User
        {
            Id = id,
            OrganizationId = null,
            Role = UserRole.SystemAdministrator,
            Status = UserStatus.Active,
            FirstName = Name(firstName, nameof(firstName)),
            LastName = Name(lastName, nameof(lastName)),
            Email = trimmedEmail,
            NormalizedEmail = NormalizeEmail(trimmedEmail),
            Version = 1,
        };
    }

    /// <summary>
    /// Same normalization as ASP.NET Identity's <c>UpperInvariantLookupNormalizer</c> (Unicode NFC, then upper
    /// invariant), after trimming, so domain and Identity lookups agree.
    /// </summary>
    public static string NormalizeEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().Normalize().ToUpperInvariant();
    }

    /// <summary>True when <paramref name="email"/> passes the minimal shape check the factories apply.</summary>
    public static bool IsValidEmail(string? email)
    {
        var trimmed = email?.Trim() ?? string.Empty;
        var at = trimmed.IndexOf('@', StringComparison.Ordinal);
        return trimmed.Length > 0
               && trimmed.Length <= EmailMaxLength
               && at > 0
               && at == trimmed.LastIndexOf('@')
               && at < trimmed.Length - 1
               && !trimmed.Any(char.IsWhiteSpace);
    }

    /// <summary>
    /// FR2: an Active or Invited user becomes Inactive and raises <see cref="UserDeactivated"/>. Already Inactive:
    /// nothing changes and no event is raised. Ending the user's sessions (security-stamp rotation) is the
    /// caller's job, after the command commits (AD-16).
    /// </summary>
    public void Deactivate()
    {
        if (Status == UserStatus.Inactive)
        {
            return;
        }

        Status = UserStatus.Inactive;
        _domainEvents.Add(new UserDeactivated(Id, Role, OrganizationId));
    }

    /// <summary>
    /// FR36: edits the user's own first name, last name and phone, nothing else (email, role, status and Organization
    /// never change here). Names are trimmed, required and at most <see cref="NameMaxLength"/> characters; a blank
    /// phone becomes null, otherwise it is trimmed and must pass <see cref="IsValidPhone"/>. Raises
    /// <see cref="UserProfileEdited"/> and returns true only when a value actually changed; the persistence
    /// interceptor bumps <see cref="Version"/> on save (AD-24).
    /// </summary>
    public bool EditProfile(string? firstName, string? lastName, string? phone)
    {
        var newFirstName = RequiredName(firstName, nameof(firstName));
        var newLastName = RequiredName(lastName, nameof(lastName));
        var newPhone = NormalizePhone(phone);
        if (newPhone is not null && !IsValidPhone(newPhone))
        {
            throw new ArgumentException("A phone number has only digits, spaces and + ( ) - . characters.", nameof(phone));
        }

        if (string.Equals(FirstName, newFirstName, StringComparison.Ordinal)
            && string.Equals(LastName, newLastName, StringComparison.Ordinal)
            && string.Equals(Phone, newPhone, StringComparison.Ordinal))
        {
            return false;
        }

        FirstName = newFirstName;
        LastName = newLastName;
        Phone = newPhone;
        _domainEvents.Add(new UserProfileEdited(Id, Role, OrganizationId));
        return true;
    }

    /// <summary>A trimmed phone, or null when it is null or blank.</summary>
    public static string? NormalizePhone(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

    /// <summary>
    /// The phone shape (FR36): at most <see cref="PhoneMaxLength"/> characters of ASCII digits, spaces and
    /// <c>+ ( ) - .</c>, with at least one digit. Pass the normalized (trimmed) value.
    /// </summary>
    public static bool IsValidPhone(string? phone) =>
        phone is { Length: > 0 and <= PhoneMaxLength }
        && phone.All(c => char.IsAsciiDigit(c) || c is ' ' or '+' or '(' or ')' or '-' or '.')
        && phone.Any(char.IsAsciiDigit);

    public void ClearDomainEvents() => _domainEvents.Clear();

    private static string RequireEmail(string email) =>
        IsValidEmail(email)
            ? email.Trim()
            : throw new ArgumentException("A valid email address is required.", nameof(email));

    private static string RequiredName(string? value, string parameterName)
    {
        var trimmed = Name(value, parameterName);
        return trimmed.Length > 0 ? trimmed : throw new ArgumentException("A name is required.", parameterName);
    }

    private static string Name(string? value, string parameterName)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= NameMaxLength
            ? trimmed
            : throw new ArgumentException($"Names are at most {NameMaxLength} characters.", parameterName);
    }
}
