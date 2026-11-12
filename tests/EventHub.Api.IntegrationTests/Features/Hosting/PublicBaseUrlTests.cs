using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Infrastructure;
using Microsoft.Extensions.Options;

namespace EventHub.Api.IntegrationTests.Features.Hosting;

/// <summary>
/// AD-15 (Story 1.6a): emailed links come only from <c>EventHub:App:PublicBaseUrl</c>, an absolute http(s) URL checked
/// when the host starts. Database-free.
/// </summary>
public sealed class PublicBaseUrlTests
{
    [Theory]
    [InlineData("")]
    [InlineData("/relative")]
    public async Task Start_WhenPublicBaseUrlIsNotAnAbsoluteHttpUrl_FailsWithOptionsValidation(string value)
    {
        await using var factory = new EventHubApiFactory(
            connectionString: null,
            settings: new Dictionary<string, string> { ["EventHub:App:PublicBaseUrl"] = value });

        var thrown = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.True(Unwrap(thrown).Any(exception => exception is OptionsValidationException), thrown.ToString());
    }

    [Theory]
    [InlineData("http://localhost:5173", true)]
    [InlineData("https://app.eventhub.test/", true)]
    [InlineData("https://app.eventhub.test/base", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("/relative", false)]
    [InlineData("ftp://app.eventhub.test", false)]
    [InlineData("https://app.eventhub.test/?q=1", false)]
    [InlineData("https://app.eventhub.test/#x", false)]
    public void IsValidPublicBaseUrl_ForAValue_AcceptsOnlyAbsoluteHttpUrlsWithoutQueryOrFragment(string? value, bool valid)
    {
        Assert.Equal(valid, AppOptions.IsValidPublicBaseUrl(value));
    }

    [Theory]
    [InlineData("https://app.eventhub.test", "reset-password?token=x", "https://app.eventhub.test/reset-password?token=x")]
    [InlineData("https://app.eventhub.test/", "reset-password?token=x", "https://app.eventhub.test/reset-password?token=x")]
    [InlineData("https://app.eventhub.test/", "/reset-password?token=x", "https://app.eventhub.test/reset-password?token=x")]
    [InlineData("https://app.eventhub.test/base/", "reset-password", "https://app.eventhub.test/base/reset-password")]
    public void Link_ForABaseAndPath_JoinsWithExactlyOneSlash(string baseUrl, string path, string expected)
    {
        Assert.Equal(expected, new AppOptions { PublicBaseUrl = baseUrl }.Link(path));
    }

    [Fact]
    public void Link_WhenTheBaseIsInvalid_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new AppOptions { PublicBaseUrl = "/relative" }.Link("reset-password"));
    }

    private static IEnumerable<Exception> Unwrap(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Unwrap))
                {
                    yield return inner;
                }
            }
        }
    }
}
