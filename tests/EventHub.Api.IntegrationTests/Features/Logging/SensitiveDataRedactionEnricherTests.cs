using EventHub.Api.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace EventHub.Api.IntegrationTests.Features.Logging;

/// <summary>AD-22: secret-like values never reach a sink.</summary>
public sealed class SensitiveDataRedactionEnricherTests
{
    [Fact]
    public void Enrich_WhenTopLevelPropertyIsSensitive_MasksIt()
    {
        var events = Capture(log => log.Information("Reset {ResetToken} for {UserId}", "abc123", 42));

        var logEvent = Assert.Single(events);
        Assert.Equal("\"***\"", logEvent.Properties["ResetToken"].ToString());
        Assert.Equal("42", logEvent.Properties["UserId"].ToString());
    }

    [Theory]
    [InlineData("ClientSecret")]
    [InlineData("Authorization")]
    [InlineData("SetCookie")]
    [InlineData("ApiKey")]
    [InlineData("api_key")]
    [InlineData("ConnectionString")]
    public void Enrich_WhenNameIsSecretLike_MasksIt(string propertyName)
    {
        var events = Capture(log => log.ForContext(propertyName, "sensitive-value").Information("Configured"));

        Assert.Equal("\"***\"", Assert.Single(events).Properties[propertyName].ToString());
    }

    [Fact]
    public void Enrich_WhenNestedPropertyIsSensitive_MasksIt()
    {
        var events = Capture(log => log.Information("Login {@Request}", new { Email = "a@b.c", Password = "secret" }));

        var rendered = Assert.Single(events).Properties["Request"].ToString();
        Assert.DoesNotContain("\"secret\"", rendered, StringComparison.Ordinal);
        Assert.Contains("***", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Enrich_WhenDictionaryHasPasswordKey_MasksThatValueOnly()
    {
        var bag = new Dictionary<string, string> { ["password"] = "hunter2", ["user"] = "ali" };

        var events = Capture(log => log.Information("Form {Fields}", bag));

        var rendered = Assert.Single(events).Properties["Fields"].ToString();
        Assert.DoesNotContain("hunter2", rendered, StringComparison.Ordinal);
        Assert.Contains("ali", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Enrich_WhenSequenceOfStructuresHasPassword_MasksEachOne()
    {
        var items = new[] { new { Name = "a", Password = "p1" }, new { Name = "b", Password = "p2" } };

        var events = Capture(log => log.Information("Batch {@Items}", items));

        var rendered = Assert.Single(events).Properties["Items"].ToString();
        Assert.DoesNotContain("p1", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("p2", rendered, StringComparison.Ordinal);
        Assert.Contains("\"a\"", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddEventHubSerilog_WhenLoggingATokenProperty_MasksIt()
    {
        var sink = new ListSink();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<ILogEventSink>(sink); // picked up by ReadFrom.Services
        builder.AddEventHubSerilog();
        await using var app = builder.Build();

        app.Services.GetRequiredService<ILogger<SensitiveDataRedactionEnricherTests>>()
            .LogInformation("Issued {InviteToken} for {UserId}", "raw-token-value", 7);

        var logEvent = Assert.Single(sink.Events, e => e.Properties.ContainsKey("InviteToken"));
        Assert.Equal("\"***\"", logEvent.Properties["InviteToken"].ToString());
    }

    private static List<LogEvent> Capture(Action<Serilog.ILogger> write)
    {
        var sink = new ListSink();
        using var logger = new LoggerConfiguration()
            .Enrich.With<SensitiveDataRedactionEnricher>()
            .WriteTo.Sink(sink)
            .CreateLogger();
        write(logger);
        return sink.Events;
    }

    private sealed class ListSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            lock (Events)
            {
                Events.Add(logEvent);
            }
        }
    }
}
