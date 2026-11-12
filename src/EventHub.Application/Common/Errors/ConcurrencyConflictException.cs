namespace EventHub.Application.Common.Errors;

/// <summary>
/// The caller saved with a stale <c>version</c>: someone else changed the record first (AD-24). 409
/// <c>concurrency_conflict</c>; nothing is saved. The message is never returned to the client.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
        : base("The record was changed by someone else.")
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
