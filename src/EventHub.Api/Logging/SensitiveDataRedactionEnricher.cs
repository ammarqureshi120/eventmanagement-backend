using Serilog.Core;
using Serilog.Events;

namespace EventHub.Api.Logging;

/// <summary>
/// AD-22: masks any log property (at any depth) whose name contains a secret-like fragment
/// (token, password, secret, authorization, cookie, apikey, api_key, connectionstring).
/// </summary>
public sealed class SensitiveDataRedactionEnricher : ILogEventEnricher
{
    public const string Mask = "***";

    private static readonly string[] SensitiveFragments =
        ["token", "password", "secret", "authorization", "cookie", "apikey", "api_key", "connectionstring"];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties.ToArray())
        {
            var redacted = IsSensitive(name) ? new ScalarValue(Mask) : Redact(value);
            if (!ReferenceEquals(redacted, value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, redacted));
            }
        }
    }

    public static bool IsSensitive(string name) =>
        SensitiveFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static LogEventPropertyValue Redact(LogEventPropertyValue value)
    {
        switch (value)
        {
            case StructureValue structure:
            {
                var changed = false;
                var properties = structure.Properties.Select(p =>
                {
                    var next = IsSensitive(p.Name) ? new ScalarValue(Mask) : Redact(p.Value);
                    changed |= !ReferenceEquals(next, p.Value);
                    return new LogEventProperty(p.Name, next);
                }).ToList();
                return changed ? new StructureValue(properties, structure.TypeTag) : value;
            }

            case DictionaryValue dictionary:
            {
                var changed = false;
                var elements = dictionary.Elements.Select(kv =>
                {
                    var key = kv.Key.Value?.ToString() ?? string.Empty;
                    var next = IsSensitive(key) ? new ScalarValue(Mask) : Redact(kv.Value);
                    changed |= !ReferenceEquals(next, kv.Value);
                    return new KeyValuePair<ScalarValue, LogEventPropertyValue>(kv.Key, next);
                }).ToList();
                return changed ? new DictionaryValue(elements) : value;
            }

            case SequenceValue sequence:
            {
                var changed = false;
                var elements = sequence.Elements.Select(e =>
                {
                    var next = Redact(e);
                    changed |= !ReferenceEquals(next, e);
                    return next;
                }).ToList();
                return changed ? new SequenceValue(elements) : value;
            }

            default:
                return value;
        }
    }
}
