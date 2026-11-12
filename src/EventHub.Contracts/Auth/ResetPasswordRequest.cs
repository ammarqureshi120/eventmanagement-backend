namespace EventHub.Contracts.Auth;

/// <summary>
/// <c>POST /api/auth/reset-password</c> body (FR4). The token from the emailed link travels only here, in a POST body,
/// never in a path or query string (AD-16).
/// </summary>
public sealed record ResetPasswordRequest(string Token, string NewPassword)
{
    /// <summary>Never prints the token or the password (NFR4, AD-22).</summary>
    public override string ToString() => nameof(ResetPasswordRequest);
}
