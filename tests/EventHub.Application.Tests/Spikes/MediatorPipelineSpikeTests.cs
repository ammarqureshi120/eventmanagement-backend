using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Application.Tests.Spikes;

/// <summary>
/// AD-5 spike: Mediator 3.0.2 (source-generated, Scoped) on net10 runs the handler and the
/// pipeline behaviors in registration order. Spike types live only in this test assembly.
/// </summary>
public sealed class MediatorPipelineSpikeTests
{
    [Fact]
    public async Task Send_WhenTwoBehaviorsRegistered_RunsThemInRegistrationOrderAroundTheHandler()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var result = await mediator.Send(new SpikeQuery("ping"), TestContext.Current.CancellationToken);

        Assert.Equal("pong:ping", result);
        Assert.Equal(
            ["first:before", "second:before", "handler", "second:after", "first:after"],
            scope.ServiceProvider.GetRequiredService<CallLog>().Entries);
    }

    [Fact]
    public async Task Mediator_WhenRegisteredScoped_ResolvesPerScopeAndNotFromRoot()
    {
        await using var provider = BuildProvider();

        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();

        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<IMediator>(),
            second.ServiceProvider.GetRequiredService<IMediator>());
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IMediator>());
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<CallLog>();
        services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.Assemblies = [typeof(SpikeQuery)];
            options.PipelineBehaviors = [typeof(FirstBehavior<,>), typeof(SecondBehavior<,>)];
        });

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}

public sealed record SpikeQuery(string Value) : IQuery<string>;

public sealed class SpikeQueryHandler(CallLog log) : IQueryHandler<SpikeQuery, string>
{
    public ValueTask<string> Handle(SpikeQuery query, CancellationToken cancellationToken)
    {
        log.Entries.Add("handler");
        return ValueTask.FromResult($"pong:{query.Value}");
    }
}

public sealed class CallLog
{
    public List<string> Entries { get; } = [];
}

public sealed class FirstBehavior<TMessage, TResponse>(CallLog log) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        log.Entries.Add("first:before");
        var response = await next(message, cancellationToken);
        log.Entries.Add("first:after");
        return response;
    }
}

public sealed class SecondBehavior<TMessage, TResponse>(CallLog log) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        log.Entries.Add("second:before");
        var response = await next(message, cancellationToken);
        log.Entries.Add("second:after");
        return response;
    }
}
