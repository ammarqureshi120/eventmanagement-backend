using EventHub.Application.Audit;
using EventHub.Application.Authorization;
using EventHub.Application.Common.Errors;
using EventHub.Application.Common.Ports;
using EventHub.Contracts.Audit;
using EventHub.Domain.Users;
using FluentValidation;
using Mediator;

namespace EventHub.Api.IntegrationTests.TestApi;

public sealed record TestOutcome(string Outcome);

public sealed record TestItem(string? Email);

// Validation: a granted command with a nested list.
public sealed record ValidateItemsCommand(string? Name, IReadOnlyList<TestItem>? Items) : ICommand<TestOutcome>, IRequirePermission
{
    public static Permission Permission => TestPermissions.Run;
}

public sealed class ValidateItemsCommandValidator : AbstractValidator<ValidateItemsCommand>
{
    public ValidateItemsCommandValidator(TestProbe probe)
    {
        RuleFor(command => command.Name).Must(_ =>
        {
            probe.ValidatorRan();
            return true;
        });
        RuleFor(command => command.Name).NotEmpty();
        RuleForEach(command => command.Items).ChildRules(item =>
            item.RuleFor(i => i.Email).NotEmpty().EmailAddress());
    }
}

public sealed class ValidateItemsCommandHandler(TestProbe probe) : ICommandHandler<ValidateItemsCommand, TestOutcome>
{
    public ValueTask<TestOutcome> Handle(ValidateItemsCommand command, CancellationToken cancellationToken)
    {
        probe.HandlerRan();
        return ValueTask.FromResult(new TestOutcome("ok"));
    }
}

// Forbidden: nobody holds the permission; the validator and handler must never run.
public sealed record UngrantedCommand(string? Name) : ICommand<TestOutcome>, IRequirePermission
{
    public static Permission Permission => TestPermissions.Ungranted;
}

public sealed class UngrantedCommandValidator : AbstractValidator<UngrantedCommand>
{
    public UngrantedCommandValidator(TestProbe probe)
    {
        RuleFor(command => command.Name).Must(_ =>
        {
            probe.ValidatorRan();
            return true;
        }).NotEmpty();
    }
}

public sealed class UngrantedCommandHandler(TestProbe probe) : ICommandHandler<UngrantedCommand, TestOutcome>
{
    public ValueTask<TestOutcome> Handle(UngrantedCommand command, CancellationToken cancellationToken)
    {
        probe.HandlerRan();
        return ValueTask.FromResult(new TestOutcome("should not run"));
    }
}

// Not found: a query whose handler throws.
public sealed record MissingThingQuery(Guid Id) : IQuery<TestOutcome>, IRequirePermission
{
    public static Permission Permission => TestPermissions.Run;
}

public sealed class MissingThingQueryHandler : IQueryHandler<MissingThingQuery, TestOutcome>
{
    public ValueTask<TestOutcome> Handle(MissingThingQuery query, CancellationToken cancellationToken) =>
        throw new NotFoundException($"Thing {query.Id} does not exist.");
}

// Crash: a handler failing with secret text in the exception message.
public sealed record CrashCommand(string Secret) : ICommand<TestOutcome>, IRequirePermission
{
    public static Permission Permission => TestPermissions.Run;
}

public sealed class CrashCommandHandler : ICommandHandler<CrashCommand, TestOutcome>
{
    public ValueTask<TestOutcome> Handle(CrashCommand command, CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"Database password is {command.Secret}");
}

// Audit: raises an auditable application event, then optionally fails.
public sealed record TestOrganizationCreated(Guid EntityId) : IAuditableEvent
{
    public AuditAction Action => AuditAction.OrganizationCreated;

    public string EntityType => "Organization";

    public Guid? OrganizationId => EntityId;
}

public sealed record AuditedCommand(Guid EntityId, bool FailAfterRaise) : ICommand<TestOutcome>, IRequirePermission
{
    public static Permission Permission => TestPermissions.Run;
}

public sealed class AuditedCommandHandler(IApplicationEvents events) : ICommandHandler<AuditedCommand, TestOutcome>
{
    public ValueTask<TestOutcome> Handle(AuditedCommand command, CancellationToken cancellationToken)
    {
        events.Raise(new TestOrganizationCreated(command.EntityId));
        if (command.FailAfterRaise)
        {
            throw new InvalidOperationException("Handler failed after raising its event.");
        }

        return ValueTask.FromResult(new TestOutcome("audited"));
    }
}

// Dispatch depth: every handled event raises another one.
public sealed record PingEvent(int Depth) : INotification;

public sealed class PingEventHandler(IApplicationEvents events) : INotificationHandler<PingEvent>
{
    public ValueTask Handle(PingEvent notification, CancellationToken cancellationToken)
    {
        events.Raise(new PingEvent(notification.Depth + 1));
        return ValueTask.CompletedTask;
    }
}

public sealed record EndlessEventsCommand : ICommand<TestOutcome>, IRequirePermission
{
    public static Permission Permission => TestPermissions.Run;
}

public sealed class EndlessEventsCommandHandler(IApplicationEvents events) : ICommandHandler<EndlessEventsCommand, TestOutcome>
{
    public ValueTask<TestOutcome> Handle(EndlessEventsCommand command, CancellationToken cancellationToken)
    {
        events.Raise(new PingEvent(1));
        return ValueTask.FromResult(new TestOutcome("never settles"));
    }
}

// Logging: a payload with secrets that must never reach a log sink.
public sealed record EchoCommand(string? Password, string? ResetToken) : ICommand<TestOutcome>, IRequirePermission
{
    public static Permission Permission => TestPermissions.Run;
}

public sealed class EchoCommandHandler : ICommandHandler<EchoCommand, TestOutcome>
{
    public ValueTask<TestOutcome> Handle(EchoCommand command, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new TestOutcome("echoed"));
}

// Database failure: two users with the same email in one save trip the unique index.
public sealed record DuplicateUserCommand(string Email) : ICommand<TestOutcome>, IRequirePermission
{
    public static Permission Permission => TestPermissions.Run;
}

public sealed class DuplicateUserCommandHandler(IAppDbContext db, IIdGenerator ids) : ICommandHandler<DuplicateUserCommand, TestOutcome>
{
    public ValueTask<TestOutcome> Handle(DuplicateUserCommand command, CancellationToken cancellationToken)
    {
        db.Add(User.CreateSystemAdministrator(ids.NewId(), command.Email, null, null));
        db.Add(User.CreateSystemAdministrator(ids.NewId(), command.Email.ToUpperInvariant(), null, null));
        return ValueTask.FromResult(new TestOutcome("saved by the transaction behavior"));
    }
}
