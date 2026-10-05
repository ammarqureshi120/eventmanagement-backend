using EventHub.Application;
using EventHub.Application.Authorization;
using EventHub.Application.Common.Behaviors;
using EventHub.Application.Common.Events;
using EventHub.Application.Common.Ports;
using EventHub.Application.Common.Tenancy;
using FluentValidation;

namespace EventHub.Api.Hosting;

/// <summary>
/// Composition of the Application layer: the source-generated Mediator (AD-5) with the fixed pipeline, the
/// permission matrix (AD-8), validators and the per-scope application-event queue.
/// </summary>
public static class ApplicationServices
{
    /// <summary>
    /// The fixed pipeline order (AD-5). The <c>AddMediator</c> call below must list the same types in the same
    /// order (the source generator needs a literal array); <c>PipelineOrderTests</c> pins both.
    /// </summary>
    public static readonly IReadOnlyList<Type> PipelineBehaviorOrder =
    [
        typeof(LoggingBehavior<,>),
        typeof(AuthorizationBehavior<,>),
        typeof(ValidationBehavior<,>),
        typeof(TransactionBehavior<,>),
        typeof(DomainEventDispatchBehavior<,>),
    ];

    public static IServiceCollection AddEventHubApplication(this IServiceCollection services)
    {
        services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.GenerateTypesAsInternal = true;
            options.Assemblies = [typeof(ApplicationAssembly)];
            options.PipelineBehaviors =
            [
                typeof(LoggingBehavior<,>),
                typeof(AuthorizationBehavior<,>),
                typeof(ValidationBehavior<,>),
                typeof(TransactionBehavior<,>),
                typeof(DomainEventDispatchBehavior<,>),
            ];
        });

        return services.AddEventHubApplicationCore();
    }

    /// <summary>Everything except Mediator itself, so a test host with its own generated mediator reuses it.</summary>
    public static IServiceCollection AddEventHubApplicationCore(this IServiceCollection services)
    {
        services.AddScoped<ITenantContext, CurrentUserTenantContext>();
        services.AddScoped<IApplicationEvents, ApplicationEventQueue>();
        services.AddSingleton<IPermissionGrantSource, EmptyPermissionGrantSource>();
        services.AddScoped<PermissionMatrix>();
        services.AddValidatorsFromAssembly(typeof(ApplicationAssembly).Assembly, ServiceLifetime.Scoped, includeInternalTypes: true);
        return services;
    }
}
