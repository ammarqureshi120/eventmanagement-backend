using System.Text.Json.Serialization;

namespace EventHub.Contracts.Me;

/// <summary>
/// An app-shell area the caller's role may use (AD-9). The UI renders navigation only for the listed areas and
/// never re-derives them. Open enum: clients render unknown values with a neutral fallback.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NavArea>))]
public enum NavArea
{
    /// <summary>The System Administrator's platform home (<c>/platform</c>).</summary>
    [JsonStringEnumMemberName("platform")]
    Platform,

    /// <summary>My account (Story 1.7 renders it; listed for every role).</summary>
    [JsonStringEnumMemberName("account")]
    Account,
}
