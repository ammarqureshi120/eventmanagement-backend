namespace EventHub.Api.Hosting;

/// <summary>
/// A group of endpoints mapped onto the <c>/api</c> route group at startup (so the antiforgery filter covers them).
/// Production registers none; the integration test host registers its test-only module, so it never reaches
/// <c>openapi.json</c>.
/// </summary>
public interface IApiEndpointModule
{
    void MapEndpoints(RouteGroupBuilder api);
}
