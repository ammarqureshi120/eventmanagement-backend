using EventHub.Application.Common.Errors;
using FluentValidation;

namespace EventHub.Application.Auth.ResetPassword;

/// <summary>
/// An empty or oversized token is <c>token_invalid</c>, never <c>validation</c> (AD-17: token problems win). The
/// password policy is not checked here: the handler applies it only after the token is known to be good, so a bad link
/// never reports password rules.
/// </summary>
public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    /// <summary>A real token is 43 characters (32 bytes, base64url); anything far longer is refused before hashing.</summary>
    public const int TokenMaxLength = 128;

    public ResetPasswordCommandValidator()
    {
        RuleFor(command => command.Token)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(TokenInvalidException.ValidationErrorCode)
            .MaximumLength(TokenMaxLength).WithErrorCode(TokenInvalidException.ValidationErrorCode);
    }
}
