namespace EventHub.Contracts.Me;

/// <summary>
/// <c>PUT /api/me/profile</c> body (FR36, AD-24): the caller's new first name, last name and optional phone, plus the
/// <c>version</c> they were loaded at (from <c>GET /api/me</c>). Anything else in the body (email, role, status,
/// Organization) is ignored. A missing <c>version</c> is 400 <c>validation</c>; a stale one is 409 <c>concurrency_conflict</c>.
/// </summary>
public sealed record UpdateMyProfileRequest(string FirstName, string LastName, string? Phone, int? Version);
