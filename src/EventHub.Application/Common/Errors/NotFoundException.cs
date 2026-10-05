namespace EventHub.Application.Common.Errors;

/// <summary>
/// The resource does not exist or is outside the caller's tenant, assignment or route parent: 404
/// <c>not_found</c> (AD-8, AD-17, NFR-2). The message is never returned to the client.
/// </summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException()
        : base("The requested resource was not found.")
    {
    }

    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
