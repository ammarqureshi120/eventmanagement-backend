using System.Data.SqlTypes;
using EventHub.Infrastructure.Ids;

namespace EventHub.Application.Tests.Pipeline;

/// <summary>AD-13: ids sort in generation order under SQL Server's uniqueidentifier ordering.</summary>
public sealed class SequentialGuidGeneratorTests
{
    [Fact]
    public void NewId_WhenGeneratedInSequence_SortsBySqlGuidInGenerationOrder()
    {
        var generator = new SequentialGuidGenerator();
        var ids = Enumerable.Range(0, 2_000).Select(_ => generator.NewId()).ToList();

        var sqlOrder = ids.OrderBy(id => new SqlGuid(id)).ToList();

        Assert.Equal(ids, sqlOrder);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, ids);
    }

    [Fact]
    public void NewId_WhenGeneratedConcurrently_StaysUnique()
    {
        var generator = new SequentialGuidGenerator();
        var ids = new System.Collections.Concurrent.ConcurrentBag<Guid>();

        Parallel.For(0, 10_000, _ => ids.Add(generator.NewId()));

        Assert.Equal(10_000, ids.Distinct().Count());
    }
}
