using Mono.Cecil;

namespace EventHub.Application.Tests.Pipeline;

/// <summary>
/// AD-31: the seed-only password setter exists in Debug builds (EVENTHUB_SEED_TOOLS) and is compiled out of the
/// Release assemblies. <c>ci.ps1</c> builds Release first and sets <c>EVENTHUB_REQUIRE_RELEASE_BUILD=1</c>, so a
/// missing Release build fails there instead of skipping.
/// </summary>
public sealed class SeedToolsReleaseTests
{
    private const string SeedMethod = "SetPasswordForSeedAsync";

    [Theory]
    [InlineData("EventHub.Infrastructure")]
    [InlineData("EventHub.Application")]
    public void ReleaseAssembly_WhenInspected_HasNoSeedOnlyPasswordMethod(string project)
    {
        var path = ReleaseAssemblyPath(project);
        if (!File.Exists(path))
        {
            var reason = $"Release build of {project} not found at {path}; run 'dotnet build -c Release' first.";
            if (Environment.GetEnvironmentVariable("EVENTHUB_REQUIRE_RELEASE_BUILD") == "1")
            {
                Assert.Fail(reason);
            }

            Assert.Skip(reason);
        }

        using var module = ModuleDefinition.ReadModule(path);
        var offenders = module.GetTypes()
            .SelectMany(type => type.Methods)
            .Where(method => method.Name.Contains(SeedMethod, StringComparison.Ordinal))
            .Select(method => $"{method.DeclaringType.FullName}.{method.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

#if EVENTHUB_SEED_TOOLS
    [Fact]
    public void DebugAssembly_WhenInspected_HasTheSeedOnlyPasswordMethod()
    {
        // Proves the Release check looks for a name that really exists when the symbol is defined.
        Assert.NotNull(typeof(EventHub.Application.Common.Ports.IIdentityAccount).GetMethod(SeedMethod));
        Assert.NotNull(typeof(EventHub.Infrastructure.Identity.IdentityAccount).GetMethod(SeedMethod));
    }
#endif

    private static string ReleaseAssemblyPath(string project)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EventHub.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", project, "bin", "Release", "net10.0", project + ".dll");
    }
}
