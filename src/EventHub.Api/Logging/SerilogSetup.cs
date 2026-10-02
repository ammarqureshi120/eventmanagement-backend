using Serilog;
using Serilog.Sinks.OpenTelemetry;

namespace EventHub.Api.Logging;

public static class SerilogSetup
{
    /// <summary>
    /// AD-22: Serilog structured logging, redaction enricher, console + OTLP (endpoint from configuration only).
    /// Request/response bodies are never logged.
    /// </summary>
    public static WebApplicationBuilder AddEventHubSerilog(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;

        // preserveStaticLogger keeps the bootstrap logger untouched so several hosts can run in one process (tests).
        builder.Services.AddSerilog(
            (services, logger) =>
            {
                logger
                    .ReadFrom.Configuration(configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .Enrich.With<SensitiveDataRedactionEnricher>()
                    .WriteTo.Console();

                var endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
                if (!string.IsNullOrWhiteSpace(endpoint))
                {
                    logger.WriteTo.OpenTelemetry(options =>
                    {
                        var httpProtobuf = string.Equals(
                            configuration["OTEL_EXPORTER_OTLP_PROTOCOL"], "http/protobuf", StringComparison.OrdinalIgnoreCase);
                        options.Protocol = httpProtobuf ? OtlpProtocol.HttpProtobuf : OtlpProtocol.Grpc;
                        options.Endpoint = httpProtobuf ? $"{endpoint.TrimEnd('/')}/v1/logs" : endpoint;
                        options.Headers = ParseHeaders(configuration["OTEL_EXPORTER_OTLP_HEADERS"]);
                        options.ResourceAttributes = new Dictionary<string, object>
                        {
                            ["service.name"] = configuration["OTEL_SERVICE_NAME"] ?? builder.Environment.ApplicationName,
                        };
                    });
                }
            },
            preserveStaticLogger: true);

        return builder;
    }

    /// <summary>
    /// Parses <c>OTEL_EXPORTER_OTLP_HEADERS</c> (<c>key1=value1,key2=value2</c>, W3C-baggage style):
    /// splits on the first <c>=</c>, skips malformed pairs, percent-decodes keys and values per the OTel spec.
    /// </summary>
    internal static Dictionary<string, string> ParseHeaders(string? raw)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return headers;
        }

        foreach (var pair in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..separator].Trim());
            if (key.Length == 0)
            {
                continue;
            }

            headers[key] = Uri.UnescapeDataString(pair[(separator + 1)..].Trim());
        }

        return headers;
    }
}
