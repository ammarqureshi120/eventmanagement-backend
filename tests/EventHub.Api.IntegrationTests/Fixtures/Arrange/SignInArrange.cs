#if EVENTHUB_SEED_TOOLS
using System.Net;
using System.Net.Http.Json;
using EventHub.Domain.Users;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.Data.SqlClient;

namespace EventHub.Api.IntegrationTests.Fixtures.Arrange;

/// <summary>
/// AD-31 arrange helpers for sign-in tests: System Administrators seeded through configuration, the one with a
/// password via the Development-only seed password (seed tools exist only outside Release).
/// </summary>
public static class SignInArrange
{
    public const string Password = "Correct-Horse-9";

    public const string FirstName = "Sara";

    public const string LastName = "Khan";

    public static string UniqueEmail(string name) => $"{name}-{Guid.NewGuid():N}@example.test";

    /// <summary>Entry 0: <paramref name="email"/> with names and, when given, a Development seed password; entry 1 (optional): a password-less SysAdmin.</summary>
    public static Dictionary<string, string> Seed(string email, string? password = Password, string? passwordlessEmail = null)
    {
        var settings = new Dictionary<string, string>
        {
            ["EventHub:Seed:SystemAdministrators:0:Email"] = email,
            ["EventHub:Seed:SystemAdministrators:0:FirstName"] = FirstName,
            ["EventHub:Seed:SystemAdministrators:0:LastName"] = LastName,
        };
        if (password is not null)
        {
            settings["EventHub:Seed:SystemAdministrators:0:DevPassword"] = password;
        }

        if (passwordlessEmail is not null)
        {
            settings["EventHub:Seed:SystemAdministrators:1"] = passwordlessEmail;
        }

        return settings;
    }

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string? email, string? password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password }, TestContext.Current.CancellationToken);

    public static async Task SignInAsync(HttpClient client, string email, string password = Password)
    {
        var response = await LoginAsync(client, email, password);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Login failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    public static async Task<Guid> UserIdAsync(LocalSqlFixture sql, string email) =>
        await sql.ScalarAsync<Guid>(
            DataScope.Identity, "SELECT Id FROM dbo.Users WHERE NormalizedEmail = @email", ("@email", User.NormalizeEmail(email)));

    public static async Task<string?> SecurityStampAsync(LocalSqlFixture sql, Guid userId)
    {
        await using var connection = await sql.OpenAsync(DataScope.Identity, TestContext.Current.CancellationToken);
        await using var command = new SqlCommand("SELECT SecurityStamp FROM dbo.Users WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@id", userId);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) as string;
    }

    public static Task<int> AuditCountAsync(LocalSqlFixture sql, Guid userId, string action) =>
        sql.ScalarAsync<int>(
            DataScope.System,
            "SELECT COUNT(*) FROM dbo.AuditEntries WHERE EntityId = @id AND Action = @action",
            ("@id", userId), ("@action", action));

    /// <summary>The <c>Set-Cookie</c> header of the session cookie, or null.</summary>
    public static string? SessionSetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(value => value.StartsWith(".EventHub.Session=", StringComparison.Ordinal))
            : null;

    /// <summary>A request to <paramref name="path"/> carrying exactly <paramref name="cookie"/> (<c>name=value</c>).</summary>
    public static async Task<HttpResponseMessage> GetWithCookieAsync(EventHubApiFactory factory, string path, string cookie)
    {
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
#endif
