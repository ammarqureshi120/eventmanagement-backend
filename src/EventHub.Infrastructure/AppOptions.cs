namespace EventHub.Infrastructure;

/// <summary>
/// <c>EventHub:App:*</c> (<c>EventHub__App__*</c>). <see cref="PublicBaseUrl"/> is where the SPA is served; emailed
/// links are built only from it, never from the request host (AD-15). Validated on start: an absolute http(s) URL.
/// </summary>
public sealed class AppOptions
{
    public const string SectionName = "EventHub:App";

    public string? PublicBaseUrl { get; set; }

    /// <summary>True when <paramref name="value"/> is an absolute http or https URL without query or fragment.</summary>
    public static bool IsValidPublicBaseUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);

    /// <summary><c>{PublicBaseUrl}{path}</c> with exactly one slash between them.</summary>
    public string Link(string pathAndQuery)
    {
        ArgumentException.ThrowIfNullOrEmpty(pathAndQuery);
        if (!IsValidPublicBaseUrl(PublicBaseUrl))
        {
            throw new InvalidOperationException($"{SectionName}:PublicBaseUrl is not an absolute http(s) URL.");
        }

        return PublicBaseUrl!.TrimEnd('/') + "/" + pathAndQuery.TrimStart('/');
    }
}
