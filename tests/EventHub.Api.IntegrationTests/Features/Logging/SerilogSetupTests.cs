using System.Reflection;
using EventHub.Api.Logging;

namespace EventHub.Api.IntegrationTests.Features.Logging;

/// <summary>OTEL_EXPORTER_OTLP_HEADERS parsing (OTel spec: comma-separated key=value, percent-encoded).</summary>
public sealed class SerilogSetupTests
{
    private static readonly MethodInfo ParseHeadersMethod =
        typeof(SerilogSetup).GetMethod("ParseHeaders", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void ParseHeaders_WhenWellFormed_ReturnsEachPair()
    {
        var headers = ParseHeaders("x-otlp-api-key=abc, tenant = t1");

        Assert.Equal("abc", headers["x-otlp-api-key"]);
        Assert.Equal("t1", headers["tenant"]);
    }

    [Fact]
    public void ParseHeaders_WhenPairsMalformed_SkipsThem()
    {
        var headers = ParseHeaders("novalue,=orphan,,good=1");

        Assert.Equal(["good"], headers.Keys);
    }

    [Fact]
    public void ParseHeaders_WhenValueContainsEquals_SplitsOnFirstOnly()
    {
        var headers = ParseHeaders("authorization=Basic dXNlcjpwYXNz==");

        Assert.Equal("Basic dXNlcjpwYXNz==", headers["authorization"]);
    }

    [Fact]
    public void ParseHeaders_WhenPercentEncoded_UnescapesKeyAndValue()
    {
        var headers = ParseHeaders("my%20key=a%2Cb%3Dc");

        Assert.Equal("a,b=c", headers["my key"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseHeaders_WhenEmpty_ReturnsNoHeaders(string? raw)
    {
        Assert.Empty(ParseHeaders(raw));
    }

    // The Api keeps no InternalsVisibleTo (its generated Mediator is internal), so call the internal parser by reflection.
    private static Dictionary<string, string> ParseHeaders(string? raw) =>
        (Dictionary<string, string>)ParseHeadersMethod.Invoke(null, [raw])!;
}
