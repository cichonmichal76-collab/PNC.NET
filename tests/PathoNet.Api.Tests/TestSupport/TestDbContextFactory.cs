using Microsoft.EntityFrameworkCore;

namespace PathoNet.Api.Tests.TestSupport;

internal sealed class TestDbContextFactory<TContext>(DbContextOptions<TContext> options)
    : IDbContextFactory<TContext>
    where TContext : DbContext
{
    public TContext CreateDbContext() =>
        (TContext)Activator.CreateInstance(typeof(TContext), options)!;

    public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());
}
