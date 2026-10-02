using EventHub.Api.Logging;

namespace EventHub.Api.IntegrationTests.Features.Logging;

/// <summary>OTEL_EXPORTER_OTLP_HEADERS parsing (OTel spec: comma-separated key=value, percent-encoded).</summary>
public sealed class SerilogSetupTests
{
    [Fact]
    public void ParseHeaders_WhenWellFormed_ReturnsEachPair()
    {
        var headers = SerilogSetup.ParseHeaders("x-otlp-api-key=abc, tenant = t1");

        Assert.Equal("abc", headers["x-otlp-api-key"]);
        Assert.Equal("t1", headers["tenant"]);
    }

    [Fact]
    public void ParseHeaders_WhenPairsMalformed_SkipsThem()
    {
        var headers = SerilogSetup.ParseHeaders("novalue,=orphan,,good=1");

        Assert.Equal(["good"], headers.Keys);
    }

    [Fact]
    public void ParseHeaders_WhenValueContainsEquals_SplitsOnFirstOnly()
    {
        var headers = SerilogSetup.ParseHeaders("authorization=Basic dXNlcjpwYXNz==");

        Assert.Equal("Basic dXNlcjpwYXNz==", headers["authorization"]);
    }

    [Fact]
    public void ParseHeaders_WhenPercentEncoded_UnescapesKeyAndValue()
    {
        var headers = SerilogSetup.ParseHeaders("my%20key=a%2Cb%3Dc");

        Assert.Equal("a,b=c", headers["my key"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseHeaders_WhenEmpty_ReturnsNoHeaders(string? raw)
    {
        Assert.Empty(SerilogSetup.ParseHeaders(raw));
    }
}
