using EventHub.Domain;

namespace EventHub.Domain.Tests;

/// <summary>Baseline for the Domain unit suite; status-machine and policy tests join with their features.</summary>
public sealed class DomainConventionsTests
{
    [Fact]
    public void DomainTypes_WhenScanned_AllLiveUnderTheDomainNamespace()
    {
        var offenders = typeof(DomainAssembly).Assembly.GetTypes()
            .Where(t => t.Namespace is not null && !t.Namespace.StartsWith("EventHub.Domain", StringComparison.Ordinal))
            .Where(t => !t.Name.StartsWith('<'))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(offenders);
    }
}
