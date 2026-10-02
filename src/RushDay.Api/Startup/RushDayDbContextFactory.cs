using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Api.Startup;

/// <summary>
/// The singleton <see cref="IDbContextFactory{TContext}"/> for work that must not borrow a request's scoped context,
/// above all HybridCache factories: the first caller's request may end, and dispose its context, while other callers
/// still wait on the same fill (04-performance-and-ops.md section 4). Its options are built once exactly as EF builds
/// the scoped context's (every registered <c>IDbContextOptionsConfiguration</c>: the Npgsql connection, snake-case
/// names, the <c>DbCommandCounter</c> interceptor and anything a test adds with <c>ConfigureDbContext</c>), but on the
/// root provider, so a context it creates depends on no request scope.
/// </summary>
/// <remarks>
/// EF's own <c>AddDbContextFactory</c> cannot sit alongside <c>AddRushDayPersistence</c>'s <c>AddDbContext</c>: that
/// registers its options (and their configurations) per scope, which a singleton factory may not consume.
/// </remarks>
public sealed class RushDayDbContextFactory : IDbContextFactory<RushDayDbContext>
{
    private readonly Lazy<DbContextOptions<RushDayDbContext>> _options;

    public RushDayDbContextFactory(IServiceProvider root, IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(scopes);
        _options = new Lazy<DbContextOptions<RushDayDbContext>>(() => BuildOptions(root, scopes));
    }

    public RushDayDbContext CreateDbContext() => new(_options.Value);

    public Task<RushDayDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());

    private static DbContextOptions<RushDayDbContext> BuildOptions(IServiceProvider root, IServiceScopeFactory scopes)
    {
        var builder = new DbContextOptionsBuilder<RushDayDbContext>(new DbContextOptions<RushDayDbContext>());
        builder.UseApplicationServiceProvider(root);

        // The configurations are registered per scope but hold only their configure actions; each runs against the
        // root provider, which is all they resolve from (singletons such as DbCommandCounter).
        using var scope = scopes.CreateScope();
        foreach (var configuration in scope.ServiceProvider.GetServices<IDbContextOptionsConfiguration<RushDayDbContext>>())
        {
            configuration.Configure(root, builder);
        }

        return builder.Options;
    }
}
