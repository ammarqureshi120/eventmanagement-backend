using EventHub.Application.Auth;
using EventHub.Application.Auth.ResetPassword;
using EventHub.Application.Common.Behaviors;
using EventHub.Application.Common.Errors;
using EventHub.Application.Common.Validation;
using EventHub.Infrastructure.Identity;
using FluentValidation;
using Mediator;

namespace EventHub.Application.Tests.Auth;

/// <summary>Story 1.6a: the shared password policy, the token-first decision order (AD-17) and the token format (AD-16).</summary>
public sealed class PasswordResetRuleTests
{
    [Theory]
    [InlineData("abcdefg1")]
    [InlineData("Correct-Horse-9")]
    [InlineData("пароль123")]
    public void PasswordPolicy_WhenEveryRuleIsMet_ReportsNothing(string password)
    {
        Assert.Empty(PasswordPolicy.Check(password));
    }

    [Theory]
    [InlineData(null, new[] { PasswordPolicy.TooShort, PasswordPolicy.NeedsLetter, PasswordPolicy.NeedsNumber })]
    [InlineData("", new[] { PasswordPolicy.TooShort, PasswordPolicy.NeedsLetter, PasswordPolicy.NeedsNumber })]
    [InlineData("abc", new[] { PasswordPolicy.TooShort, PasswordPolicy.NeedsNumber })]
    [InlineData("abcdefgh", new[] { PasswordPolicy.NeedsNumber })]
    [InlineData("12345678", new[] { PasswordPolicy.NeedsLetter })]
    [InlineData("a1", new[] { PasswordPolicy.TooShort })]
    public void PasswordPolicy_WhenRulesAreBroken_ReportsEachMissingRule(string? password, string[] expected)
    {
        Assert.Equal(expected, PasswordPolicy.Check(password));
    }

    [Fact]
    public void PasswordPolicy_WhenLongerThan128_ReportsTooLong()
    {
        Assert.Empty(PasswordPolicy.Check(new string('a', 127) + "1"));
        Assert.Equal([PasswordPolicy.TooLong], PasswordPolicy.Check(new string('a', 128) + "1"));
    }

    [Fact]
    public void PasswordPolicy_WithASupplementaryPlaneLetter_CountsItAsOneLetterCharacter()
    {
        const string scriptA = "𝒜"; // MATHEMATICAL SCRIPT CAPITAL A: one rune, two UTF-16 code units

        Assert.Empty(PasswordPolicy.Check(scriptA + "1234567"));
        Assert.Equal([PasswordPolicy.TooShort], PasswordPolicy.Check(scriptA + "123456"));
        Assert.Equal([PasswordPolicy.NeedsNumber], PasswordPolicy.Check(string.Concat(Enumerable.Repeat(scriptA, 8))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ValidationBehavior_WhenTheTokenIsMissing_ThrowsTokenInvalidNotValidation(string? token)
    {
        var behavior = new ValidationBehavior<ResetPasswordCommand, Unit>([new ResetPasswordCommandValidator()]);

        var thrown = await Assert.ThrowsAsync<TokenInvalidException>(async () =>
            await behavior.Handle(new ResetPasswordCommand(token, null), (_, _) => ValueTask.FromResult(Unit.Value), TestContext.Current.CancellationToken));

        Assert.Null(thrown.Reason);
    }

    [Fact]
    public async Task ValidationBehavior_WhenAnotherValidatorAlsoFails_StillThrowsTokenInvalid()
    {
        var fieldRule = new InlineValidator<ResetPasswordCommand>();
        fieldRule.RuleFor(command => command.NewPassword).NotEmpty();
        var behavior = new ValidationBehavior<ResetPasswordCommand, Unit>([fieldRule, new ResetPasswordCommandValidator()]);

        await Assert.ThrowsAsync<TokenInvalidException>(async () =>
            await behavior.Handle(new ResetPasswordCommand(" ", null), (_, _) => ValueTask.FromResult(Unit.Value), TestContext.Current.CancellationToken));

        var tokenOk = new ValidationBehavior<ResetPasswordCommand, Unit>([fieldRule, new ResetPasswordCommandValidator()]);
        await Assert.ThrowsAsync<RequestValidationException>(async () =>
            await tokenOk.Handle(new ResetPasswordCommand("abc", null), (_, _) => ValueTask.FromResult(Unit.Value), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void UserTokenCodec_WhenRoundTripped_HashesTheDecodedBytesAndRejectsMalformedTokens()
    {
        var (token, hash) = UserTokenCodec.NewToken();

        Assert.Equal(43, token.Length);
        Assert.Equal(hash, UserTokenCodec.HashOf(token));
        Assert.Null(UserTokenCodec.HashOf(null));
        Assert.Null(UserTokenCodec.HashOf(token[..^1]));
        Assert.Null(UserTokenCodec.HashOf(token + "A"));
        Assert.Null(UserTokenCodec.HashOf(new string('*', 43)));
        Assert.NotEqual(token, UserTokenCodec.NewToken().Token);
    }
}
