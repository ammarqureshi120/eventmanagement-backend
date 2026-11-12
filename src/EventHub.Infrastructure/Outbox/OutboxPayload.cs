using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace EventHub.Infrastructure.Outbox;

/// <summary>
/// AD-15: outbox payloads are the message record's JSON, encrypted with the Data Protection purpose <c>outbox</c> (the
/// key ring is shared by every replica, AD-21), so a raw token never rests in plain text.
/// </summary>
public static class OutboxPayload
{
    /// <summary>The Data Protection purpose of every outbox payload.</summary>
    public const string Purpose = "outbox";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IDataProtector Protector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.CreateProtector(Purpose);
    }

    public static string Protect<TMessage>(IDataProtector protector, TMessage message)
    {
        ArgumentNullException.ThrowIfNull(protector);
        return protector.Protect(JsonSerializer.Serialize(message, Json));
    }

    /// <summary>Decrypts and reads a payload; throws when it cannot be decrypted or is not a <typeparamref name="TMessage"/>.</summary>
    public static TMessage Unprotect<TMessage>(IDataProtector protector, string payload)
    {
        ArgumentNullException.ThrowIfNull(protector);
        return JsonSerializer.Deserialize<TMessage>(protector.Unprotect(payload), Json)
               ?? throw new InvalidDataException("The outbox payload is empty.");
    }
}
