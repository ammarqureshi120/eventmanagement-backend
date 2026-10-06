using System.Text.Json.Serialization;

namespace EventHub.Contracts.Users;

/// <summary>A user's role on the wire (camelCase). Open enum: clients render unknown values with a neutral fallback.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UserRole>))]
public enum UserRole
{
    [JsonStringEnumMemberName("systemAdministrator")]
    SystemAdministrator,

    [JsonStringEnumMemberName("orgAdministrator")]
    OrgAdministrator,

    [JsonStringEnumMemberName("eventManager")]
    EventManager,
}
