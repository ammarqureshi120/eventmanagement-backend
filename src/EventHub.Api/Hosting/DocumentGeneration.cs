using System.Reflection;

namespace EventHub.Api.Hosting;

/// <summary>
/// AD-4: <c>Microsoft.Extensions.ApiDescription.Server</c> boots <c>Program</c> through the
/// <c>GetDocument.Insider</c> tool at build time. Database, migration and seeding work must be skipped then.
/// </summary>
public static class DocumentGeneration
{
    public static bool IsRunning { get; } =
        string.Equals(Assembly.GetEntryAssembly()?.GetName().Name, "GetDocument.Insider", StringComparison.Ordinal);
}
