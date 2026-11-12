namespace EventHub.Contracts.Me;

/// <summary>
/// <c>POST /api/me/change-password</c> body (FR36): the caller's current password and the new one. The confirm field
/// is a client-side check only.
/// </summary>
public sealed record ChangeMyPasswordRequest(string CurrentPassword, string NewPassword)
{
    /// <summary>Never prints the passwords (NFR4, AD-22).</summary>
    public override string ToString() => nameof(ChangeMyPasswordRequest);
}
