using EventHub.Api.Hosting;
using EventHub.Api.IntegrationTests.TestApi;
using EventHub.Application;
using EventHub.Application.Authorization;
using EventHub.Application.Common.Behaviors;
using EventHub.Application.Common.Ports;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventHub.Api.IntegrationTests.Fixtures;

/// <summary>
/// Swaps the Api's generated Mediator for one generated in this assembly, so test-only messages run through
/// the real pipeline (the source generator only sees assemblies at compile time). The behavior array must
/// stay identical to the Api's; <c>PipelineOrderTests</c> and <c>TestPipelineOrderTests</c> pin both.
/// </summary>
public static class TestPipeline
{
    public static readonly IReadOnlyList<Type> PipelineBehaviors =
    [
        typeof(LoggingBehavior<,>),
        typeof(AuthorizationBehavior<,>),
        typeof(ValidationBehavior<,>),
        typeof(TransactionBehavior<,>),
        typeof(DomainEventDispatchBehavior<,>),
    ];

    public static void AddTo(IServiceCollection services, FakeClock clock)
    {
        // Drop every registration made by the Api's generated AddMediator.
        var mediatorDescriptors = services
            .Where(descriptor => IsMediatorType(descriptor.ServiceType) || IsMediatorType(descriptor.ImplementationType))
            .ToList();
        foreach (var descriptor in mediatorDescriptors)
        {
            services.Remove(descriptor);
        }

        services.AddMediator(options =>
        {
            options.Namespace = "EventHub.Api.IntegrationTests.TestMediator";
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.Assemblies = [typeof(ApplicationAssembly), typeof(TestPipeline)];
            options.PipelineBehaviors =
            [
                typeof(LoggingBehavior<,>),
                typeof(AuthorizationBehavior<,>),
                typeof(ValidationBehavior<,>),
                typeof(TransactionBehavior<,>),
                typeof(DomainEventDispatchBehavior<,>),
            ];
        });

        services.AddHttpContextAccessor();
        services.RemoveAll<ICurrentUser>();
        services.AddScoped<ICurrentUser, TestActor>();

        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(clock);

        services.AddSingleton<IPermissionGrantSource, TestGrants>();
        services.AddSingleton<TestProbe>();
        services.AddValidatorsFromAssemblyContaining<TestEndpoints>(ServiceLifetime.Scoped, includeInternalTypes: true);
        services.AddSingleton<IApiEndpointModule, TestEndpoints>();
    }

    private static bool IsMediatorType(Type? type) =>
        type?.Namespace is { } ns && (ns == "Mediator" || ns.StartsWith("Mediator.", StringComparison.Ordinal));
}
