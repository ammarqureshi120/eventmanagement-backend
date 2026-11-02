namespace EventHub.Application.Auth;

/// <summary>
/// AD-16 session lifetimes (FR1): a sliding idle window renewed on every authenticated request, and an absolute
/// limit counted from sign-in (the <c>iat</c> claim), whatever the activity.
/// </summary>
public static class SessionPolicy
{
    /// <summary>No authenticated request for this long ends the session.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>A session never outlives sign-in by more than this.</summary>
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(12);

    /// <summary>When a session signed in at <paramref name="issuedAtUtc"/> ends at the latest.</summary>
    public static DateTime ExpiresAt(DateTime issuedAtUtc) =>
        DateTime.SpecifyKind(issuedAtUtc, DateTimeKind.Utc) + AbsoluteLifetime;
}
