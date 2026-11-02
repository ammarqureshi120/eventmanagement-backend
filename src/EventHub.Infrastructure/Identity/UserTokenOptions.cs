using System.Buffers.Text;
using System.Security.Cryptography;

namespace EventHub.Infrastructure.Identity;

/// <summary><c>EventHub:Auth:*</c> token lifetimes (<c>EventHub__Auth__ResetTokenHours</c>, AD-16).</summary>
public sealed class UserTokenOptions
{
    public const string SectionName = "EventHub:Auth";

    /// <summary>How long a password-reset token works, counted by <c>IClock</c>. Default 1 hour (FR4).</summary>
    public int ResetTokenHours { get; set; } = 1;
}

/// <summary>
/// The raw-token format (AD-16): 32 random bytes, base64url without padding in the link and the POST body; only the
/// SHA-256 of the bytes is stored.
/// </summary>
public static class UserTokenCodec
{
    /// <summary>A fresh raw token and its hash.</summary>
    public static (string Token, byte[] Hash) NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(UserToken.TokenBytes);
        try
        {
            return (Base64Url.EncodeToString(bytes), SHA256.HashData(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>The stored hash of <paramref name="token"/>, or null when it is not a well-formed raw token.</summary>
    public static byte[]? HashOf(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length != Base64Url.GetEncodedLength(UserToken.TokenBytes))
        {
            return null;
        }

        var bytes = new byte[UserToken.TokenBytes];
        try
        {
            // OperationStatus, not TryDecode: malformed input is a normal "invalid token", never an exception.
            var status = Base64Url.DecodeFromChars(token, bytes, out var consumed, out var written);
            return status == System.Buffers.OperationStatus.Done && consumed == token.Length && written == UserToken.TokenBytes
                ? SHA256.HashData(bytes)
                : null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
