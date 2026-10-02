using System.Reflection;
using EventHub.Application.Authorization;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace EventHub.Application.Tests.Architecture;

/// <summary>
/// Rules that pass on an empty set today and start biting as features arrive (AD-5, AD-7, AD-8).
/// </summary>
public sealed class PlaceholderRuleTests
{
    private static readonly Assembly Application = typeof(ApplicationAssembly).Assembly;

    [Fact]
    public void MediatorMessages_WhenScanned_AllImplementIRequirePermission()
    {
        // AD-5 / AD-8: every command and query declares its permission.
        var result = Types.InAssembly(Application)
            .That().ImplementInterface(typeof(Mediator.IMessage))
            .And().AreNotAbstract()
            .And().AreNotInterfaces()
            .Should().ImplementInterface(typeof(IRequirePermission))
            .GetResult();

        Assert.True(result.IsSuccessful, "Missing IRequirePermission: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_WhenScanned_NeverCallsIgnoreQueryFilters()
    {
        // AD-7: tenant query filters may never be switched off in Application.
        using var module = ModuleDefinition.ReadModule(Application.Location);
        var offenders = module.GetTypes()
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody)
            .Where(m => m.Body.Instructions.Any(IsIgnoreQueryFiltersCall))
            .Select(m => $"{m.DeclaringType.FullName}.{m.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

    private static bool IsIgnoreQueryFiltersCall(Instruction instruction) =>
        (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
        && instruction.Operand is MethodReference { Name: "IgnoreQueryFilters" };
}
