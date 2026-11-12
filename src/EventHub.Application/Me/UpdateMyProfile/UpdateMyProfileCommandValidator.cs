using EventHub.Domain.Users;
using FluentValidation;

namespace EventHub.Application.Me.UpdateMyProfile;

/// <summary>
/// FR36 / NFR23 profile rules, checked on the trimmed values the aggregate stores: names required and at most 100
/// characters; the phone optional (blank means none), at most 32 characters of digits, spaces and <c>+ ( ) - .</c>
/// with at least one digit; the loaded <c>version</c> is required. Keys <c>firstName</c>, <c>lastName</c>, <c>phone</c>,
/// <c>version</c> (AD-17).
/// </summary>
public sealed class UpdateMyProfileCommandValidator : AbstractValidator<UpdateMyProfileCommand>
{
    public const string FirstNameRequired = "Enter your first name.";
    public const string LastNameRequired = "Enter your last name.";
    public const string NameTooLong = "Use 100 characters or fewer.";
    public const string PhoneInvalid = "Enter a phone number with digits, spaces and + ( ) - . only.";
    public const string PhoneTooLong = "Use 32 characters or fewer.";
    public const string VersionRequired = "Reload the page and try again.";

    public UpdateMyProfileCommandValidator()
    {
        RuleFor(command => Trim(command.FirstName))
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(FirstNameRequired)
            .MaximumLength(User.NameMaxLength).WithMessage(NameTooLong)
            .OverridePropertyName(nameof(UpdateMyProfileCommand.FirstName));

        RuleFor(command => Trim(command.LastName))
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(LastNameRequired)
            .MaximumLength(User.NameMaxLength).WithMessage(NameTooLong)
            .OverridePropertyName(nameof(UpdateMyProfileCommand.LastName));

        RuleFor(command => User.NormalizePhone(command.Phone))
            .Cascade(CascadeMode.Stop)
            .MaximumLength(User.PhoneMaxLength).WithMessage(PhoneTooLong)
            .Must(phone => phone is null || User.IsValidPhone(phone)).WithMessage(PhoneInvalid)
            .OverridePropertyName(nameof(UpdateMyProfileCommand.Phone));

        RuleFor(command => command.Version)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(VersionRequired)
            .GreaterThan(0).WithMessage(VersionRequired);
    }

    private static string? Trim(string? value) => value?.Trim();
}
