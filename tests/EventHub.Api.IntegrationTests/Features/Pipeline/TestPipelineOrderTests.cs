using EventHub.Api.Hosting;
using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Api.IntegrationTests.TestApi;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Api.IntegrationTests.Features.Pipeline;

/// <summary>
/// AD-5: the test host's generated Mediator resolves the same behaviors, in the same order, as the Api declares,
/// so the test-only messages prove the real pipeline (the Api side is pinned by PipelineOrderTests).
/// </summary>
public sealed class TestPipelineOrderTests
{
    [Fact]
    public async Task TestMediator_WhenResolvingBehaviors_UsesTheApiOrder()
    {
        Assert.Equal(ApplicationServices.PipelineBehaviorOrder, TestPipeline.PipelineBehaviors);

        await using var factory = new EventHubApiFactory(connectionString: null, testApi: true);
        await using var scope = factory.Services.CreateAsyncScope();

        foreach (var resolved in new[]
                 {
                     Resolve<ValidateItemsCommand, TestOutcome>(scope.ServiceProvider),
                     Resolve<MissingThingQuery, TestOutcome>(scope.ServiceProvider),
                 })
        {
            Assert.Equal(ApplicationServices.PipelineBehaviorOrder, resolved);
        }
    }

    private static List<Type> Resolve<TMessage, TResponse>(IServiceProvider services)
        where TMessage : notnull, IMessage =>
        services.GetServices<IPipelineBehavior<TMessage, TResponse>>()
            .Select(behavior => behavior.GetType().GetGenericTypeDefinition())
            .ToList();
}
