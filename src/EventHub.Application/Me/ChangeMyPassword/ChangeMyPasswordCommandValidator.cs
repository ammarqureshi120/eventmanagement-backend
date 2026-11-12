using EventHub.Application.Auth;
using EventHub.Application.Auth.Login;
using FluentValidation;

namespace EventHub.Application.Me.ChangeMyPassword;

/// <summary>
/// The current password is required (and bounded, so a huge body is refused before any hashing); the new password
/// must meet the shared <see cref="PasswordPolicy"/>. Keys <c>currentPassword</c> and <c>newPassword</c> (AD-17). A
/// wrong current password is decided by the handler, after these checks.
/// </summary>
public sealed class ChangeMyPasswordCommandValidator : AbstractValidator<ChangeMyPasswordCommand>
{
    public const string CurrentPasswordRequired = "Enter your current password.";

    public ChangeMyPasswordCommandValidator()
    {
        RuleFor(command => command.CurrentPassword)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(CurrentPasswordRequired)
            .MaximumLength(LoginCommandValidator.PasswordMaxLength).WithMessage(LoginCommandValidator.PasswordTooLong);

        RuleFor(command => command.NewPassword).MeetsPasswordPolicy();
    }
}
