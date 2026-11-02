namespace EventHub.Application.Common.Errors;

/// <summary>
/// The user is not Active (FR2): 401 <c>account_disabled</c>. Sign-in throws it only after a correct password, so
/// the disabled state is shown only to someone who knows the password (AD-16, AD-17). The message is never
/// returned to the client.
/// </summary>
public sealed class AccountDisabledException : Exception
{
    public AccountDisabledException()
        : base("This account is disabled.")
    {
    }

    public AccountDisabledException(string message)
        : base(message)
    {
    }

    public AccountDisabledException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
