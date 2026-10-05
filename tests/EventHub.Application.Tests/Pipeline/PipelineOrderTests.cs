using EventHub.Api.Hosting;
using EventHub.Application.Common.Behaviors;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace EventHub.Application.Tests.Pipeline;

/// <summary>
/// AD-5: the Api registers the fixed pipeline logging → authorization → validation → transaction →
/// domain events + audit → handler. Three angles: the declared list, the literal array the source generator
/// reads in <c>AddEventHubApplication</c>, and the DI registrations it generates for every Application message.
/// </summary>
public sealed class PipelineOrderTests
{
    private static readonly Type[] Expected =
    [
        typeof(LoggingBehavior<,>),
        typeof(AuthorizationBehavior<,>),
        typeof(ValidationBehavior<,>),
        typeof(TransactionBehavior<,>),
        typeof(DomainEventDispatchBehavior<,>),
    ];

    [Fact]
    public void DeclaredOrder_WhenRead_IsTheFixedAd5Order()
    {
        Assert.Equal(Expected, ApplicationServices.PipelineBehaviorOrder);
    }

    [Fact]
    public void AddMediatorLiteral_WhenReadFromTheApiAssembly_ListsTheBehaviorsInTheFixedOrder()
    {
        using var module = ModuleDefinition.ReadModule(typeof(ApplicationServices).Assembly.Location);
        var configureLambdas = module.GetTypes()
            .SelectMany(type => type.Methods)
            .Where(method => method.HasBody && method.Name.Contains(nameof(ApplicationServices.AddEventHubApplication), StringComparison.Ordinal))
            .Where(method => method.Body.Instructions.Any(i => i.Operand is MethodReference { Name: "set_PipelineBehaviors" }))
            .ToList();

        var lambda = Assert.Single(configureLambdas);
        var tokens = lambda.Body.Instructions
            .Where(instruction => instruction.OpCode == OpCodes.Ldtoken && instruction.Operand is TypeReference)
            .Select(instruction => ((TypeReference)instruction.Operand).FullName)
            .Where(name => name.Contains("Behavior`2", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(Expected.Select(type => type.FullName), tokens);
    }

    [Fact]
    public void ApiRegistration_WhenBuilt_RegistersMediatorScopedAndEveryMessagesBehaviorsInOrder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventHubApplication();

        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == typeof(ISender)).Lifetime);

        var perMessage = services
            .Where(descriptor => descriptor.ServiceType.IsGenericType
                                 && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>))
            .GroupBy(descriptor => descriptor.ServiceType);
        foreach (var registrations in perMessage)
        {
            var order = registrations
                .Select(descriptor => descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType())
                .Select(type => type!.GetGenericTypeDefinition())
                .ToList();
            Assert.Equal(Expected, order);
        }
    }
}
