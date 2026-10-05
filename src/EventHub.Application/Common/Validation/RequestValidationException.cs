namespace EventHub.Application.Common.Validation;

/// <summary>
/// Thrown by the validation behavior: 400 <c>validation</c> with <see cref="Errors"/> keyed by camelCase
/// dot paths (AD-17).
/// </summary>
public sealed class RequestValidationException : Exception
{
    public RequestValidationException()
        : this(new Dictionary<string, string[]>())
    {
    }

    public RequestValidationException(string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]>();
    }

    public RequestValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = new Dictionary<string, string[]>();
    }

    public RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more fields are invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
