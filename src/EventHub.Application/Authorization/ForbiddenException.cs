namespace EventHub.Application.Authorization;

/// <summary>The caller may see the resource but the matrix does not grant the action: 403 <c>forbidden</c> (AD-8, AD-17).</summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException()
        : base("The caller is not allowed to perform this action.")
    {
    }

    public ForbiddenException(string message)
        : base(message)
    {
    }

    public ForbiddenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
