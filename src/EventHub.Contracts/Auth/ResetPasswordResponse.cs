namespace EventHub.Contracts.Auth;

/// <summary>
/// <c>POST /api/auth/reset-password</c> 200 body (FR4, Story 1.6b): the email of the account whose password was reset,
/// returned only after a valid token, so the SPA can prefill the sign-in form.
/// </summary>
public sealed record ResetPasswordResponse(string Email)
{
    /// <summary>Never prints the email (AD-22).</summary>
    public override string ToString() => nameof(ResetPasswordResponse);
}
