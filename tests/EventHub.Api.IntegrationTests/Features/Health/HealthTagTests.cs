namespace EventHub.Api.IntegrationTests.Features.Health;

public class HealthTagTests
{
    // Infrastructure tags the DB check; ServiceDefaults maps /health/ready by tag. AD-1 forbids a shared
    // project reference, so this test keeps the two constants equal (otherwise readiness drops the DB check).
    [Fact]
    public void ReadyTag_WhenComparedAcrossLayers_IsIdentical()
    {
        Assert.Equal(Microsoft.Extensions.Hosting.Extensions.ReadyTag, EventHub.Infrastructure.DependencyInjection.ReadyTag);
    }
}
