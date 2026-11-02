using EventHub.Application.Auth.Login;
using EventHub.Domain.Users;
using FluentValidation;

namespace EventHub.Application.Auth.ForgotPassword;

/// <summary>A missing or malformed email is 400 <c>validation</c> on <c>email</c> (NFR23); same rules as sign-in.</summary>
public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(command => command.Email == null ? null : command.Email.Trim())
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(LoginCommandValidator.EmailRequired)
            .MaximumLength(User.EmailMaxLength).WithMessage(LoginCommandValidator.EmailTooLong)
            .Must(User.IsValidEmail).WithMessage(LoginCommandValidator.EmailInvalid)
            .OverridePropertyName(nameof(ForgotPasswordCommand.Email));
    }
}
