namespace EventHub.Contracts.Auth;

/// <summary>
/// <c>POST /api/auth/login</c> body (FR1). Both fields are required, non-null strings in the contract; a missing or
/// null value still binds (System.Text.Json does not enforce it) and becomes a field-level <c>validation</c> error.
/// </summary>
public sealed record LoginRequest(string Email, string Password)
{
    /// <summary>Never prints the password or the email (NFR4, AD-22).</summary>
    public override string ToString() => nameof(LoginRequest);
}
