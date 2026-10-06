using EventHub.Domain.Users;
using FluentValidation;

namespace EventHub.Application.Auth.Login;

/// <summary>Empty or malformed fields are 400 <c>validation</c> on <c>email</c> / <c>password</c> (NFR23, UX-DR15).</summary>
public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public const string EmailRequired = "Enter your email address.";
    public const string EmailInvalid = "Enter a valid email address.";
    public const string EmailTooLong = "Use an email address of at most 256 characters.";
    public const string PasswordRequired = "Enter your password.";
    public const string PasswordTooLong = "Use a password of at most 1024 characters.";

    /// <summary>Generous upper bound so a huge body is rejected before any hashing work.</summary>
    public const int PasswordMaxLength = 1024;

    public LoginCommandValidator()
    {
        // The trimmed email is what the account lookup uses, so it is what gets validated.
        RuleFor(command => command.Email == null ? null : command.Email.Trim())
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(EmailRequired)
            .MaximumLength(User.EmailMaxLength).WithMessage(EmailTooLong)
            .Must(User.IsValidEmail).WithMessage(EmailInvalid)
            .OverridePropertyName(nameof(LoginCommand.Email));

        RuleFor(command => command.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(PasswordRequired)
            .MaximumLength(PasswordMaxLength).WithMessage(PasswordTooLong);
    }
}
