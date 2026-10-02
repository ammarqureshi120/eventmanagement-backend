using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EventHub.Infrastructure.Persistence.Conventions;

/// <summary>
/// AD-12: rejects non-UTC writes and materializes every value with <see cref="DateTimeKind.Utc"/>.
/// </summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => UtcDateTime.EnsureUtc(value),
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

/// <summary>Nullable variant of <see cref="UtcDateTimeConverter"/>.</summary>
public sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    value => value.HasValue ? UtcDateTime.EnsureUtc(value.Value) : value,
    value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value);

public static class UtcDateTime
{
    public static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw new InvalidOperationException(
                $"Only UTC DateTime values may be persisted (got Kind={value.Kind}). Use IClock.UtcNow.");
}
