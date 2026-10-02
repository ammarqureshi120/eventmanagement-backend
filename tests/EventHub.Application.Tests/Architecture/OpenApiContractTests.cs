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
