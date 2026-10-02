using System.Reflection;
using EventHub.Application;
using EventHub.Contracts;
using EventHub.Domain;
using EventHub.Infrastructure;
using NetArchTest.Rules;

namespace EventHub.Application.Tests.Architecture;

/// <summary>AD-1: dependency direction of the single hexagon.</summary>
public sealed class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(DomainAssembly).Assembly;
    private static readonly Assembly Contracts = typeof(ContractsAssembly).Assembly;
    private static readonly Assembly Application = typeof(ApplicationAssembly).Assembly;
    private static readonly Assembly Infrastructure = typeof(InfrastructureAssembly).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    /// <summary>Framework/adapter namespaces that may appear only in Infrastructure and Api.</summary>
    private static readonly string[] AdapterNamespaces =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.Identity",
        "MailKit",
        "MimeKit",
        "System.Data",
        "Microsoft.Data.SqlClient",
    ];

    [Fact]
    public void Domain_WhenInspected_ReferencesBclOnly()
    {
        Assert.Empty(NonBclReferences(Domain));
    }

    [Fact]
    public void Contracts_WhenInspected_ReferencesNothing()
    {
        Assert.Empty(NonBclReferences(Contracts));
    }

    [Fact]
    public void Application_WhenInspected_ReferencesOnlyDomainAndContractsAmongProjects()
    {
        var projectReferences = Application.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("EventHub.", StringComparison.Ordinal))
            .ToList();

        Assert.All(projectReferences, name => Assert.Contains(name, new[] { "EventHub.Domain", "EventHub.Contracts" }));
    }

    [Fact]
    public void Application_WhenInspected_HasNoAdapterFrameworkTypes()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny(AdapterNamespaces)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void DomainAndContracts_WhenInspected_HaveNoAdapterFrameworkTypes()
    {
        var result = Types.InAssemblies([Domain, Contracts])
            .ShouldNot()
            .HaveDependencyOnAny(AdapterNamespaces)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void CoreLayers_WhenInspected_DoNotDependOnInfrastructureOrApi()
    {
        var result = Types.InAssemblies([Domain, Contracts, Application])
            .ShouldNot()
            .HaveDependencyOnAny("EventHub.Infrastructure", "EventHub.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Infrastructure_WhenInspected_DoesNotDependOnApi()
    {
        var result = Types.InAssembly(Infrastructure)
            .ShouldNot()
            .HaveDependencyOn("EventHub.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Api_WhenInspected_IsTheOnlyAssemblyReferencingInfrastructure()
    {
        Assert.Contains(Api.GetReferencedAssemblies(), a => a.Name == "EventHub.Infrastructure");
        Assert.DoesNotContain(Application.GetReferencedAssemblies(), a => a.Name == "EventHub.Infrastructure");
    }

    private static List<string> NonBclReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !IsBcl(name))
            .ToList();

    private static bool IsBcl(string name) =>
        name is "netstandard" or "mscorlib"
        || name.Equals("System", StringComparison.Ordinal)
        || name.StartsWith("System.", StringComparison.Ordinal);

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : "Offending types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
