using EventHub.Application.Auth;
using EventHub.Application.Common.Behaviors;
using EventHub.Application.Common.Validation;
using EventHub.Application.Me.ChangeMyPassword;
using EventHub.Application.Me.UpdateMyProfile;
using Mediator;

namespace EventHub.Application.Tests.Me;

/// <summary>Story 1.7a: the profile and change-password rules, with camelCase keys and the agreed messages (AD-17, NFR23).</summary>
public sealed class MyAccountRuleTests
{
    [Fact]
    public async Task UpdateMyProfile_WhenEveryFieldIsInvalid_ReportsFirstNameLastNameAndPhone()
    {
        var errors = await ValidateAsync(new UpdateMyProfileCommand("   ", new string('b', 101), "12ab", 1));

        Assert.Equal(["firstName", "lastName", "phone"], errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal([UpdateMyProfileCommandValidator.FirstNameRequired], errors["firstName"]);
        Assert.Equal([UpdateMyProfileCommandValidator.NameTooLong], errors["lastName"]);
        Assert.Equal([UpdateMyProfileCommandValidator.PhoneInvalid], errors["phone"]);
    }

    [Fact]
    public async Task UpdateMyProfile_WhenNamesAreMissing_UsesTheRequiredMessages()
    {
        var errors = await ValidateAsync(new UpdateMyProfileCommand(null, "", null, 1));

        Assert.Equal("Enter your first name.", Assert.Single(errors["firstName"]));
        Assert.Equal("Enter your last name.", Assert.Single(errors["lastName"]));
        Assert.False(errors.ContainsKey("phone"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" +31 (0)6-1234.5678 ")]
    public async Task UpdateMyProfile_WhenValid_PassesWithTrimmedNamesAndAnOptionalPhone(string? phone)
    {
        var behavior = new ValidationBehavior<UpdateMyProfileCommand, Unit>([new UpdateMyProfileCommandValidator()]);

        await behavior.Handle(
            new UpdateMyProfileCommand(" Sara ", new string('k', 100) + "  ", phone, 1),
            (_, _) => ValueTask.FromResult(Unit.Value),
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task UpdateMyProfile_WhenVersionIsMissingOrNotPositive_ReportsVersion(int? version)
    {
        var errors = await ValidateAsync(new UpdateMyProfileCommand("Sara", "Khan", null, version));

        Assert.Equal(["version"], errors.Keys);
        Assert.Equal([UpdateMyProfileCommandValidator.VersionRequired], errors["version"]);
    }

    [Fact]
    public async Task UpdateMyProfile_WhenThePhoneIsLongerThan32_ReportsTheLength()
    {
        var errors = await ValidateAsync(new UpdateMyProfileCommand("Sara", "Khan", new string('1', 33), 1));

        Assert.Equal([UpdateMyProfileCommandValidator.PhoneTooLong], errors["phone"]);
    }

    [Fact]
    public async Task ChangeMyPassword_WhenEmptyAndWeak_ReportsCurrentPasswordAndEveryBrokenPolicyRule()
    {
        var errors = await ValidateAsync(new ChangeMyPasswordCommand("", "abc"));

        Assert.Equal([ChangeMyPasswordCommandValidator.CurrentPasswordRequired], errors["currentPassword"]);
        Assert.Equal([PasswordPolicy.TooShort, PasswordPolicy.NeedsNumber], errors["newPassword"]);
    }

    [Fact]
    public async Task ChangeMyPassword_WhenBothAreGiven_Passes()
    {
        var behavior = new ValidationBehavior<ChangeMyPasswordCommand, Unit>([new ChangeMyPasswordCommandValidator()]);

        await behavior.Handle(
            new ChangeMyPasswordCommand("anything", "Correct-Horse-10"),
            (_, _) => ValueTask.FromResult(Unit.Value),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public void ChangeMyPasswordCommand_WhenPrinted_NeverShowsThePasswords()
    {
        var text = new ChangeMyPasswordCommand("Secret-Current-1", "Secret-New-2").ToString();

        Assert.DoesNotContain("Secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", new EventHub.Contracts.Me.ChangeMyPasswordRequest("Secret-Current-1", "Secret-New-2").ToString(), StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyDictionary<string, string[]>> ValidateAsync(UpdateMyProfileCommand command)
    {
        var behavior = new ValidationBehavior<UpdateMyProfileCommand, Unit>([new UpdateMyProfileCommandValidator()]);
        var thrown = await Assert.ThrowsAsync<RequestValidationException>(async () =>
            await behavior.Handle(command, (_, _) => ValueTask.FromResult(Unit.Value), TestContext.Current.CancellationToken));
        return thrown.Errors;
    }

    private static async Task<IReadOnlyDictionary<string, string[]>> ValidateAsync(ChangeMyPasswordCommand command)
    {
        var behavior = new ValidationBehavior<ChangeMyPasswordCommand, Unit>([new ChangeMyPasswordCommandValidator()]);
        var thrown = await Assert.ThrowsAsync<RequestValidationException>(async () =>
            await behavior.Handle(command, (_, _) => ValueTask.FromResult(Unit.Value), TestContext.Current.CancellationToken));
        return thrown.Errors;
    }
}
