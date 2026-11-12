namespace EventHub.Contracts.Auth;

/// <summary><c>POST /api/auth/forgot-password</c> body (FR4). The answer is always the same 202, whatever the account.</summary>
public sealed record ForgotPasswordRequest(string Email)
{
    /// <summary>Never prints the email (AD-22).</summary>
    public override string ToString() => nameof(ForgotPasswordRequest);
}
