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
    private const string DevPassword = "DevPassword";

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

    /// <summary>
    /// Story 1.4: the Development-only seed password (<c>DevPassword</c>) binding and the code that applies it exist
    /// only outside Release: no member, constant or string literal named after it, and no call to the seed setter.
    /// </summary>
    [Fact]
    public void ReleaseInfrastructure_WhenInspected_HasNoDevPasswordBindingOrApplyPath()
    {
        var path = ReleaseAssemblyPath("EventHub.Infrastructure");
        if (!File.Exists(path))
        {
            var reason = $"Release build of EventHub.Infrastructure not found at {path}; run 'dotnet build -c Release' first.";
            if (Environment.GetEnvironmentVariable("EVENTHUB_REQUIRE_RELEASE_BUILD") == "1")
            {
                Assert.Fail(reason);
            }

            Assert.Skip(reason);
        }

        using var module = ModuleDefinition.ReadModule(path);
        var types = module.GetTypes().ToList();
        var members = types.SelectMany(type =>
                type.Methods.Select(m => $"{type.FullName}.{m.Name}")
                    .Concat(type.Properties.Select(p => $"{type.FullName}.{p.Name}"))
                    .Concat(type.Fields.Select(f => $"{type.FullName}.{f.Name}")))
            .Where(name => name.Contains(DevPassword, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var literals = types.SelectMany(type => type.Methods)
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Where(instruction => instruction.Operand is string text && text.Contains(DevPassword, StringComparison.OrdinalIgnoreCase))
            .Select(instruction => (string)instruction.Operand)
            .ToList();
        var seedCalls = types.SelectMany(type => type.Methods)
            .Where(method => method.HasBody && method.Body.Instructions.Any(i => i.Operand is MethodReference { Name: SeedMethod }))
            .Select(method => $"{method.DeclaringType.FullName}.{method.Name}")
            .ToList();

        Assert.Empty(members);
        Assert.Empty(literals);
        Assert.Empty(seedCalls);
    }

#if EVENTHUB_SEED_TOOLS
    [Fact]
    public void DebugAssembly_WhenInspected_HasTheSeedOnlyPasswordMethod()
    {
        // Proves the Release check looks for a name that really exists when the symbol is defined.
        Assert.NotNull(typeof(EventHub.Application.Common.Ports.IIdentityAccount).GetMethod(SeedMethod));
        Assert.NotNull(typeof(EventHub.Infrastructure.Identity.IdentityAccount).GetMethod(SeedMethod));
        Assert.NotNull(typeof(EventHub.Infrastructure.Identity.SystemAdministratorSeed).GetProperty(DevPassword));
        Assert.Equal(DevPassword, EventHub.Infrastructure.Identity.SeedOptions.DevPasswordKey);
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
