using EventHub.Application.Common.Validation;
using FluentValidation;
using Mediator;

namespace EventHub.Application.Common.Behaviors;

/// <summary>
/// Pipeline step 3 (AD-5): runs every FluentValidation validator of the message; any failure becomes 400
/// <c>validation</c> with camelCase dot-path keys (AD-17). The server is the authority (NFR-23).
/// </summary>
public sealed class ValidationBehavior<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    /// <summary>Error key for object-level failures (no property path).</summary>
    public const string RootKey = "root";

    public async ValueTask<TResponse> Handle(
        TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(message, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
        {
            var errors = failures
                .GroupBy(failure => CamelCasePathResolver.ToPath(failure.PropertyName) is { Length: > 0 } path ? path : RootKey, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(failure => failure.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal);
            throw new RequestValidationException(errors);
        }

        return await next(message, cancellationToken);
    }
}
