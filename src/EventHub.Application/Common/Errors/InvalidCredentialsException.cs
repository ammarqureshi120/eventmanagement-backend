namespace EventHub.Application.Common.Errors;

/// <summary>
/// Sign-in failed: wrong password, unknown email or no password set. Always the same 401
/// <c>invalid_credentials</c>, so the response never reveals whether the email exists (AD-16, AD-17, NFR4).
/// The message is never returned to the client.
/// </summary>
public sealed class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException()
        : base("The email or password is incorrect.")
    {
    }

    public InvalidCredentialsException(string message)
        : base(message)
    {
    }

    public InvalidCredentialsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
