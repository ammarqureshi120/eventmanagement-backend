using EventHub.Application.Common.Ports;
using EventHub.Domain.Common;
using EventHub.Infrastructure.Persistence.Conventions;
using EventHub.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Api.IntegrationTests.Features.Persistence;

/// <summary>AD-12 UTC converters, the AD-19 timestamp interceptor and the AD-24 version interceptor (no database needed).</summary>
public sealed class PersistenceConventionsTests
{
    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void UtcConverter_WhenWritingNonUtc_Throws(DateTimeKind kind)
    {
        var value = new DateTime(2027, 3, 15, 9, 0, 0, kind);

        Assert.Throws<InvalidOperationException>(() => new UtcDateTimeConverter().ConvertToProvider(value));
        Assert.Throws<InvalidOperationException>(() => new NullableUtcDateTimeConverter().ConvertToProvider(value));
    }

    [Fact]
    public void UtcConverter_WhenWritingUtc_PassesThrough()
    {
        var value = new DateTime(2027, 3, 15, 4, 0, 0, DateTimeKind.Utc);

        Assert.Equal(value, new UtcDateTimeConverter().ConvertToProvider(value));
        Assert.Null(new NullableUtcDateTimeConverter().ConvertToProvider(null));
    }

    [Fact]
    public void UtcConverter_WhenReading_ReturnsKindUtc()
    {
        var stored = new DateTime(2027, 3, 15, 4, 0, 0, DateTimeKind.Unspecified);

        var read = (DateTime)new UtcDateTimeConverter().ConvertFromProvider(stored)!;
        var readNullable = (DateTime?)new NullableUtcDateTimeConverter().ConvertFromProvider(stored);

        Assert.Equal(DateTimeKind.Utc, read.Kind);
        Assert.Equal(DateTimeKind.Utc, readNullable!.Value.Kind);
        Assert.Equal(stored.Ticks, read.Ticks);
    }

    [Fact]
    public void TimestampsInterceptor_WhenAdded_StampsCreatedAndUpdated()
    {
        var clock = new FixedClock(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        using var context = new WidgetContext();
        var widget = new Widget { Id = 1 };
        context.Add(widget);

        new TimestampsInterceptor(clock).Stamp(context);

        Assert.Equal(clock.UtcNow, widget.CreatedAtUtc);
        Assert.Equal(clock.UtcNow, widget.UpdatedAtUtc);
    }

    [Fact]
    public void TimestampsInterceptor_WhenModified_StampsUpdatedOnly()
    {
        var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = new FixedClock(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        using var context = new WidgetContext();
        var widget = new Widget { Id = 1, CreatedAtUtc = created, UpdatedAtUtc = created };
        context.Attach(widget).State = EntityState.Modified;

        new TimestampsInterceptor(clock).Stamp(context);

        Assert.Equal(created, widget.CreatedAtUtc);
        Assert.Equal(clock.UtcNow, widget.UpdatedAtUtc);
    }

    [Fact]
    public void VersionInterceptor_WhenModified_BumpsVersionOnce()
    {
        using var context = new WidgetContext();
        var gadget = new Gadget { Id = 1, Name = "a", Version = 4 };
        context.Attach(gadget);
        gadget.Name = "b";

        VersionInterceptor.Bump(context);

        Assert.Equal(5, gadget.Version);
        Assert.Equal(4, context.Entry(gadget).Property(g => g.Version).OriginalValue);
    }

    [Fact]
    public void VersionInterceptor_WhenVersionIsAlreadyModified_DoesNotBumpAgain()
    {
        using var context = new WidgetContext();
        var gadget = new Gadget { Id = 1, Name = "a", Version = 4 };
        context.Attach(gadget);
        gadget.Name = "b";
        context.Entry(gadget).Property(g => g.Version).CurrentValue++; // as UserStore.UpdateAsync does

        VersionInterceptor.Bump(context);

        Assert.Equal(5, gadget.Version);
    }

    [Fact]
    public void VersionInterceptor_WhenAddedOrUnchanged_LeavesVersionAlone()
    {
        using var context = new WidgetContext();
        var unchanged = new Gadget { Id = 1, Name = "a", Version = 4 };
        var added = new Gadget { Id = 2, Name = "n", Version = 1 };
        context.Attach(unchanged);
        context.Add(added);

        VersionInterceptor.Bump(context);

        Assert.Equal(4, unchanged.Version);
        Assert.Equal(1, added.Version);
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class Widget : ITimestamped
    {
        public int Id { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }
    }

    private sealed class Gadget : IVersioned
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Version { get; set; }
    }

    /// <summary>Change tracking only; never connects.</summary>
    private sealed class WidgetContext : DbContext
    {
        public DbSet<Widget> Widgets => Set<Widget>();

        public DbSet<Gadget> Gadgets => Set<Gadget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Gadget>().Property(gadget => gadget.Version).IsConcurrencyToken();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseSqlServer("Server=127.0.0.1,1;Database=none;Encrypt=False");
    }
}
