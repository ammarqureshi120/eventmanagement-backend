using System.Text.Json;

namespace EventHub.Application.Tests.Architecture;

/// <summary>
/// AD-4 / AD-29 rules over the committed contract (<c>backend/openapi/openapi.json</c>):
/// OpenAPI 3.0.x, unique operationIds, exactly one tag per operation.
/// </summary>
public sealed class OpenApiContractTests
{
    private static readonly string[] HttpMethods = ["get", "put", "post", "delete", "patch", "options", "head", "trace"];

    [Fact]
    public void OpenApiDocument_WhenRead_IsVersion3_0()
    {
        using var document = LoadDocument();
        var version = document.RootElement.GetProperty("openapi").GetString();

        Assert.NotNull(version);
        Assert.StartsWith("3.0.", version);
    }

    [Fact]
    public void Operations_WhenEnumerated_HaveUniqueOperationIds()
    {
        using var document = LoadDocument();
        var operations = Operations(document).ToList();

        var missing = operations.Where(o => string.IsNullOrWhiteSpace(o.OperationId)).Select(o => o.Route).ToList();
        Assert.True(missing.Count == 0, "Operations without operationId (.WithName): " + string.Join(", ", missing));

        var duplicates = operations.GroupBy(o => o.OperationId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, "Duplicate operationIds: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void Operations_WhenEnumerated_HaveExactlyOneTag()
    {
        using var document = LoadDocument();
        var offenders = Operations(document).Where(o => o.TagCount != 1).Select(o => o.Route).ToList();

        Assert.True(offenders.Count == 0, "Operations without exactly one tag: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// Story 1.4 (additive only): login and logout under tag Auth, me under tag Me; the 1.1 operation stays. Story 1.6
    /// adds forgot and reset password under tag Auth; Story 1.7 adds the profile edit and password change under tag Me.
    /// </summary>
    [Theory]
    [InlineData("GET /api/auth/antiforgery", "GetAntiforgeryToken", "Auth")]
    [InlineData("POST /api/auth/login", "Login", "Auth")]
    [InlineData("POST /api/auth/logout", "Logout", "Auth")]
    [InlineData("POST /api/auth/forgot-password", "ForgotPassword", "Auth")]
    [InlineData("POST /api/auth/reset-password", "ResetPassword", "Auth")]
    [InlineData("GET /api/me", "GetMe", "Me")]
    [InlineData("PUT /api/me/profile", "UpdateMyProfile", "Me")]
    [InlineData("POST /api/me/change-password", "ChangeMyPassword", "Me")]
    public void Operations_WhenRead_IncludeTheAuthAndMeOperations(string route, string operationId, string tag)
    {
        using var document = LoadDocument();
        var (method, path) = (route.Split(' ')[0].ToLowerInvariant(), route.Split(' ')[1]);

        var operation = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);

        Assert.Equal(operationId, operation.GetProperty("operationId").GetString());
        Assert.Equal(tag, Assert.Single(operation.GetProperty("tags").EnumerateArray()).GetString());
    }

    /// <summary>
    /// Story 1.6b: reset-password answers 200 with <c>ResetPasswordResponse { email }</c> instead of 204, so the SPA can
    /// prefill sign-in. This replaces the 204 (a user-approved contract change, not an additive one); the only client is
    /// the SPA, regenerated in the same story.
    /// </summary>
    [Fact]
    public void ResetPassword_WhenRead_Returns200WithTheResetPasswordResponseEmail()
    {
        using var document = LoadDocument();
        var responses = document.RootElement.GetProperty("paths").GetProperty("/api/auth/reset-password").GetProperty("post").GetProperty("responses");

        Assert.False(responses.TryGetProperty("204", out _));
        var schemaRef = responses.GetProperty("200").GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString();
        Assert.Equal("#/components/schemas/ResetPasswordResponse", schemaRef);

        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ResetPasswordResponse");
        Assert.Equal("string", schema.GetProperty("properties").GetProperty("email").GetProperty("type").GetString());
        Assert.Contains("email", schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
    }

    /// <summary>Story 1.7: both own-account commands answer 204 with no body.</summary>
    [Theory]
    [InlineData("/api/me/profile", "put")]
    [InlineData("/api/me/change-password", "post")]
    public void MyAccountOperations_WhenRead_Return204(string path, string method)
    {
        using var document = LoadDocument();
        var responses = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses");

        Assert.Equal(["204"], responses.EnumerateObject().Select(response => response.Name));
    }

    private static IEnumerable<(string Route, string? OperationId, int TagCount)> Operations(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("paths", out var paths))
        {
            yield break;
        }

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject().Where(p => HttpMethods.Contains(p.Name)))
            {
                var operationId = operation.Value.TryGetProperty("operationId", out var id) ? id.GetString() : null;
                var tagCount = operation.Value.TryGetProperty("tags", out var tags) ? tags.GetArrayLength() : 0;
                yield return ($"{operation.Name.ToUpperInvariant()} {path.Name}", operationId, tagCount);
            }
        }
    }

    private static JsonDocument LoadDocument()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EventHub.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, "openapi", "openapi.json");
        Assert.True(File.Exists(path), $"Committed contract not found at {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
