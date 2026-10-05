using EventHub.Application.Audit;
using EventHub.Application.Common.Behaviors;
using EventHub.Application.Common.Events;
using EventHub.Application.Common.Ports;
using EventHub.Application.Common.Validation;
using EventHub.Contracts.Audit;
using EventHub.Domain.Common;
using FluentValidation;
using Mediator;

namespace EventHub.Application.Tests.Pipeline;

/// <summary>AD-5 / AD-6 / AD-14: queries never mutate or audit; object-level validation errors use the <c>root</c> key.</summary>
public sealed class BehaviorTests
{
    [Fact]
    public async Task Dispatch_WhenAQueryRaisesAnAuditableEvent_Throws()
    {
        var events = new ApplicationEventQueue();
        var behavior = new DomainEventDispatchBehavior<SampleQuery, int>(new FakeDb(), events, new FakePublisher());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(new SampleQuery(), (_, _) =>
            {
                events.Raise(new Audited());
                return ValueTask.FromResult(1);
            }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Dispatch_WhenACommandRaisesAnAuditableEvent_PublishesIt()
    {
        var events = new ApplicationEventQueue();
        var publisher = new FakePublisher();
        var behavior = new DomainEventDispatchBehavior<SampleCommand, int>(new FakeDb(), events, publisher);

        await behavior.Handle(new SampleCommand(), (_, _) =>
        {
            events.Raise(new Audited());
            return ValueTask.FromResult(1);
        }, TestContext.Current.CancellationToken);

        Assert.IsType<Audited>(Assert.Single(publisher.Published));
    }

    [Fact]
    public async Task Transaction_WhenAQueryLeavesTrackedChanges_Throws()
    {
        var behavior = new TransactionBehavior<SampleQuery, int>(new FakeDb { HasPendingChanges = true });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(new SampleQuery(), (_, _) => ValueTask.FromResult(1), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Transaction_WhenAQueryChangesNothing_ReturnsItsResult()
    {
        var behavior = new TransactionBehavior<SampleQuery, int>(new FakeDb());

        Assert.Equal(7, await behavior.Handle(new SampleQuery(), (_, _) => ValueTask.FromResult(7), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Validation_WhenAnObjectLevelRuleFails_UsesTheRootKey()
    {
        var behavior = new ValidationBehavior<SampleCommand, int>([new ObjectLevelValidator()]);

        var failure = await Assert.ThrowsAsync<RequestValidationException>(async () =>
            await behavior.Handle(new SampleCommand(), (_, _) => ValueTask.FromResult(1), TestContext.Current.CancellationToken));

        Assert.Equal([ValidationBehavior<SampleCommand, int>.RootKey], failure.Errors.Keys);
    }

    public sealed record SampleQuery : IQuery<int>;

    public sealed record SampleCommand : ICommand<int>;

    private sealed record Audited : IAuditableEvent
    {
        public AuditAction Action => AuditAction.OrganizationCreated;

        public string EntityType => "Organization";

        public Guid EntityId => Guid.Empty;

        public Guid? OrganizationId => null;
    }

    private sealed class ObjectLevelValidator : AbstractValidator<SampleCommand>
    {
        public ObjectLevelValidator() => RuleFor(command => command).Must(_ => false).WithMessage("Not allowed.").OverridePropertyName(string.Empty);
    }

    private sealed class FakeDb : IAppDbContext
    {
        public bool HasActiveTransaction => false;

        public bool HasPendingChanges { get; init; }

        public void Add<TEntity>(TEntity entity)
            where TEntity : class => throw new NotSupportedException();

        public IReadOnlyList<IHasDomainEvents> GetAggregatesWithEvents() => [];

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<IAppTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IAppTransaction>(new NoTransaction());
    }

    private sealed class NoTransaction : IAppTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakePublisher : IPublisher
    {
        public List<object> Published { get; } = [];

        public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            Published.Add(notification);
            return ValueTask.CompletedTask;
        }

        public ValueTask Publish(object notification, CancellationToken cancellationToken = default)
        {
            Published.Add(notification);
            return ValueTask.CompletedTask;
        }
    }
}
