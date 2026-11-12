namespace EventHub.Application.Common.Errors;

/// <summary>Why a reset or invite token was refused, when that is known (AD-17).</summary>
public enum TokenInvalidReason
{
    /// <summary>Older than its lifetime, or superseded by a newer token of the same purpose.</summary>
    Expired,

    /// <summary>Already used once.</summary>
    Used,
}

/// <summary>
/// 400 <c>token_invalid</c> (AD-17): an unknown, superseded, expired or used token, or one whose account is no longer
/// Active. It is decided before <c>validation</c>, so a bad link never reports field errors. <see cref="Reason"/> is
/// set only when known; it becomes the optional <c>reason</c> (<c>expired</c> | <c>used</c>). The message is never
/// returned to the client.
/// </summary>
public sealed class TokenInvalidException : Exception
{
    /// <summary>
    /// A FluentValidation error code: a failure carrying it turns the whole validation result into
    /// <see cref="TokenInvalidException"/> (token problems win over field problems, AD-17).
    /// </summary>
    public const string ValidationErrorCode = "token_invalid";

    public TokenInvalidException()
        : this(reason: null)
    {
    }

    public TokenInvalidException(TokenInvalidReason? reason)
        : base("The token is invalid.")
    {
        Reason = reason;
    }

    public TokenInvalidException(string message)
        : base(message)
    {
    }

    public TokenInvalidException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public TokenInvalidReason? Reason { get; }
}
