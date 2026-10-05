using EventHub.Domain.Common;
using EventHub.Infrastructure.Persistence;
using EventHub.Infrastructure.Persistence.Configurations;
using EventHub.Infrastructure.Persistence.Interceptors;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Api.IntegrationTests.Features.Persistence;

/// <summary>AD-7 layer 2: the named "Tenant" query filter and the session-context command (no database needed).</summary>
public sealed class TenantFilterTests
{
    [Fact]
    public void TenantFilter_WhenOrganizationInScope_ParameterizesItPerContext()
    {
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        using var first = new ProbeContext(DataScope.Tenant(orgA));
        using var second = new ProbeContext(DataScope.Tenant(orgB));

        var firstSql = first.Rows.ToQueryString();
        var secondSql = second.Rows.ToQueryString();

        Assert.Contains(orgA.ToString(), firstSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(orgB.ToString(), secondSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(orgA.ToString(), secondSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(TenantFilter.Name, first.Model.FindEntityType(typeof(ProbeRow))!.GetDeclaredQueryFilters().Select(f => f.Key));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("platform")]
    [InlineData("identity")]
    public void TenantFilter_WhenNoOrganizationInScope_FailsClosed(string scope)
    {
        using var context = new ProbeContext(scope switch
        {
            "platform" => DataScope.Platform,
            "identity" => DataScope.Identity,
            _ => DataScope.None,
        });

        Assert.Throws<InvalidOperationException>(() => context.Rows.ToQueryString());
    }

    [Fact]
    public void TenantFilter_WhenSystemScope_PassesWithoutAnOrganization()
    {
        using var context = new ProbeContext(DataScope.System);

        var sql = context.Rows.ToQueryString();

        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppDbContext_WhenSystemScope_ExposesSystemAndDoesNotThrow()
    {
        var accessor = new ScopeAccessor();
        accessor.Pin(DataScope.System);
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=127.0.0.1,1;Database=none;Encrypt=False").Options,
            accessor);

        Assert.True(context.IsSystemScope);
        Assert.Equal(Guid.Empty, context.CurrentTenantOrganizationId);
    }

    [Fact]
    public void AppDbContext_WhenNoOrganizationInScope_ThrowsForTheTenantId()
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=127.0.0.1,1;Database=none;Encrypt=False").Options,
            new ScopeAccessor());

        Assert.Throws<InvalidOperationException>(() => context.CurrentTenantOrganizationId);
    }

    [Fact]
    public void SessionContextCommand_WhenNoScope_IsNotCreated()
    {
        using var connection = new SqlConnection();

        Assert.Null(RlsSessionContextInterceptor.CreateCommand(connection, DataScope.None));
    }

    [Fact]
    public void SessionContextCommand_WhenTenantScope_SetsLowercaseScopeAndOrganizationAsReadOnlyParameters()
    {
        using var connection = new SqlConnection();
        var organizationId = Guid.NewGuid();

        using var command = RlsSessionContextInterceptor.CreateCommand(connection, DataScope.Tenant(organizationId))!;

        Assert.Contains("@read_only = 1", command.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain(organizationId.ToString(), command.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("tenant", command.Parameters["@scope"].Value);
        Assert.Equal(organizationId, command.Parameters["@organizationId"].Value);
    }

    private sealed class ProbeRow : ITenantOwned
    {
        public Guid Id { get; set; }

        public Guid OrganizationId { get; set; }
    }

    /// <summary>Test-only context using the production filter extension; never connects.</summary>
    private sealed class ProbeContext(DataScope scope) : DbContext, ITenantFilterSource
    {
        public DbSet<ProbeRow> Rows => Set<ProbeRow>();

        public bool IsSystemScope => scope.Kind == EventHub.Application.Common.Ports.ScopeKind.System;

        public Guid CurrentTenantOrganizationId =>
            scope.OrganizationId ?? (IsSystemScope ? Guid.Empty : throw new InvalidOperationException("No Organization in scope."));

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseSqlServer("Server=127.0.0.1,1;Database=none;Encrypt=False");

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<ProbeRow>().HasTenantFilter(this);
    }
}
