using EventHub.Application.Common.Ports;

namespace EventHub.Infrastructure.Ids;

/// <summary>
/// AD-13: EF Core <c>SequentialGuidValueGenerator</c> semantics. A random GUID whose last eight bytes (the ones
/// SQL Server compares first for <c>uniqueidentifier</c>) carry an increasing tick-seeded counter, so ids sort
/// in generation order in a clustered index while the remainder stays random. Thread-safe singleton.
/// </summary>
public sealed class SequentialGuidGenerator : IIdGenerator
{
    private long _counter = DateTime.UtcNow.Ticks;

    public Guid NewId()
    {
        Span<byte> guidBytes = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(guidBytes);

        Span<byte> counterBytes = stackalloc byte[8];
        BitConverter.TryWriteBytes(counterBytes, Interlocked.Increment(ref _counter));
        if (!BitConverter.IsLittleEndian)
        {
            counterBytes.Reverse();
        }

        guidBytes[08] = counterBytes[1];
        guidBytes[09] = counterBytes[0];
        guidBytes[10] = counterBytes[7];
        guidBytes[11] = counterBytes[6];
        guidBytes[12] = counterBytes[5];
        guidBytes[13] = counterBytes[4];
        guidBytes[14] = counterBytes[3];
        guidBytes[15] = counterBytes[2];

        return new Guid(guidBytes);
    }
}
