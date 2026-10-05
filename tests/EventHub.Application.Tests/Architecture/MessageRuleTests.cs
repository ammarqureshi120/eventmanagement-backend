using System.Reflection;
using EventHub.Application.Authorization;
using Mediator;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace EventHub.Application.Tests.Architecture;

/// <summary>
/// AD-5 / AD-7 / AD-8 / NFR1: every Mediator command and query in Application declares a static permission;
/// tenant query filters are never switched off in Application.
/// </summary>
public sealed class MessageRuleTests
{
    private static readonly Assembly Application = typeof(ApplicationAssembly).Assembly;

    /// <summary>Commands, queries and requests (and their stream forms); notifications are not dispatched through the pipeline.</summary>
    public static IEnumerable<Type> Messages() =>
        Application.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => typeof(IMessage).IsAssignableFrom(type) || typeof(IStreamMessage).IsAssignableFrom(type))
            .Where(type => !typeof(INotification).IsAssignableFrom(type));

    [Fact]
    public void MediatorMessages_WhenScanned_AllImplementIRequirePermission()
    {
        var offenders = Messages()
            .Where(type => !typeof(IRequirePermission).IsAssignableFrom(type))
            .Select(type => type.FullName)
            .ToList();

        Assert.True(offenders.Count == 0, "Missing IRequirePermission: " + string.Join(", ", offenders));
    }

    [Fact]
    public void MediatorMessages_WhenScanned_DeclareANamedStaticPermission()
    {
        var offenders = Messages()
            .Where(type => typeof(IRequirePermission).IsAssignableFrom(type))
            .Where(type => string.IsNullOrWhiteSpace(ReadPermission(type)?.Name))
            .Select(type => type.FullName)
            .ToList();

        Assert.True(offenders.Count == 0, "Static Permission missing or empty: " + string.Join(", ", offenders));
    }

    [Fact]
    public void PermissionOf_WhenTypeLacksIRequirePermission_IsNullSoAuthorizationFailsClosed()
    {
        Assert.Null(PermissionOf<string>.Value);
        Assert.Equal(new Permission("Rule.Sample"), PermissionOf<SampleMessage>.Value);
    }

    [Fact]
    public void Application_WhenScanned_NeverCallsIgnoreQueryFilters()
    {
        using var module = ModuleDefinition.ReadModule(Application.Location);
        var offenders = module.GetTypes()
            .SelectMany(type => type.Methods)
            .Where(method => method.HasBody)
            .Where(method => method.Body.Instructions.Any(IsIgnoreQueryFiltersCall))
            .Select(method => $"{method.DeclaringType.FullName}.{method.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

    private static Permission? ReadPermission(Type type) =>
        (Permission?)typeof(PermissionOf<>).MakeGenericType(type)
            .GetProperty(nameof(PermissionOf<object>.Value), BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null);

    private static bool IsIgnoreQueryFiltersCall(Instruction instruction) =>
        (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
        && instruction.Operand is MethodReference { Name: "IgnoreQueryFilters" };

    private sealed record SampleMessage : IQuery<int>, IRequirePermission
    {
        public static Permission Permission => new("Rule.Sample");
    }
}
