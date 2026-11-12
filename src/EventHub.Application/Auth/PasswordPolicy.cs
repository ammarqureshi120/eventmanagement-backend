using System.Text;
using FluentValidation;

namespace EventHub.Application.Auth;

/// <summary>
/// The one password policy (FR4, NFR23): at least 8 characters, at least one letter, at least one number, at most 128
/// characters (counted in Unicode scalar values). Reset (Story 1.6) and change password (Story 1.7) both apply it; the SPA mirrors it as a live checklist,
/// but the server is the authority. A letter is any Unicode letter and a number any Unicode decimal digit (JS
/// <c>\p{L}</c> / <c>\p{Nd}</c>). Every failing rule is reported, so the client can show which ones are missing.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public const string TooShort = "Use at least 8 characters.";
    public const string NeedsLetter = "Include at least one letter.";
    public const string NeedsNumber = "Include at least one number.";
    public const string TooLong = "Use at most 128 characters.";

    /// <summary>The shared FluentValidation rule: <c>RuleFor(x =&gt; x.NewPassword).MeetsPasswordPolicy()</c>.</summary>
    public static IRuleBuilderOptions<T, string?> MeetsPasswordPolicy<T>(this IRuleBuilder<T, string?> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule
            .Must(password => Length(password) >= MinLength).WithMessage(TooShort)
            .Must(password => Length(password) <= MaxLength).WithMessage(TooLong)
            .Must(password => password is not null && password.EnumerateRunes().Any(Rune.IsLetter)).WithMessage(NeedsLetter)
            .Must(password => password is not null && password.EnumerateRunes().Any(Rune.IsDigit)).WithMessage(NeedsNumber);
    }

    /// <summary>The messages of every rule <paramref name="password"/> breaks; empty when it meets the policy.</summary>
    public static IReadOnlyList<string> Check(string? password) =>
        CandidateValidator.Instance.Validate(new Candidate(password)).Errors
            .Select(failure => failure.ErrorMessage)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Length in Unicode scalar values (runes), not UTF-16 code units, as JS <c>[...s].length</c> counts.</summary>
    private static int Length(string? password) => password is null ? 0 : password.EnumerateRunes().Count();

    private sealed record Candidate(string? Password)
    {
        /// <summary>Never prints the password (AD-22).</summary>
        public override string ToString() => nameof(Candidate);
    }

    private sealed class CandidateValidator : AbstractValidator<Candidate>
    {
        public static readonly CandidateValidator Instance = new();

        public CandidateValidator() => RuleFor(candidate => candidate.Password).MeetsPasswordPolicy();
    }
}
