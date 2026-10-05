using EventHub.Api.Hosting;
using Mediator;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EventHub.Api.IntegrationTests.TestApi;

/// <summary>Test-only <c>/api/test/*</c> endpoints: bind, dispatch, return. Registered only by the test host.</summary>
public sealed class TestEndpoints : IApiEndpointModule
{
    public void MapEndpoints(RouteGroupBuilder api)
    {
        var test = api.MapGroup("/test").WithTags("Test");

        test.MapPost("/items", (ValidateItemsCommand command, ISender sender, CancellationToken ct) => Send(sender, command, ct));
        test.MapPost("/ungranted", (UngrantedCommand command, ISender sender, CancellationToken ct) => Send(sender, command, ct));
        test.MapGet("/missing/{id:guid}", (Guid id, ISender sender, CancellationToken ct) => Send(sender, new MissingThingQuery(id), ct));
        test.MapPost("/crash", (CrashCommand command, ISender sender, CancellationToken ct) => Send(sender, command, ct));
        test.MapPost("/audited", (AuditedCommand command, ISender sender, CancellationToken ct) => Send(sender, command, ct));
        test.MapPost("/endless-events", (ISender sender, CancellationToken ct) => Send(sender, new EndlessEventsCommand(), ct));
        test.MapPost("/duplicate-user", (DuplicateUserCommand command, ISender sender, CancellationToken ct) => Send(sender, command, ct));
        test.MapPost("/echo", (EchoCommand command, ISender sender, CancellationToken ct) => Send(sender, command, ct));
        test.MapPost("/antiforgery", ValidateAntiforgery);
    }

    /// <summary>AD-21: proves an antiforgery token issued by one host validates on another (shared key ring).</summary>
    private static async Task<IResult> ValidateAntiforgery(HttpContext context, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return TypedResults.NoContent();
        }
        catch (AntiforgeryValidationException)
        {
            return TypedResults.BadRequest();
        }
    }

    private static async Task<IResult> Send(ISender sender, ICommand<TestOutcome> command, CancellationToken ct) =>
        TypedResults.Ok(await sender.Send(command, ct));

    private static async Task<IResult> Send(ISender sender, IQuery<TestOutcome> query, CancellationToken ct) =>
        TypedResults.Ok(await sender.Send(query, ct));
}
