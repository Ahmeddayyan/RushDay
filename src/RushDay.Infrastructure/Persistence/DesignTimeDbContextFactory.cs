using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RushDay.Infrastructure.Persistence;

/// <summary>
/// Used only by the dotnet-ef tooling to build the model when adding migrations.
/// It never opens a connection, so the connection string is a placeholder.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RushDayDbContext>
{
    public RushDayDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<RushDayDbContext>();
        DependencyInjection.ConfigureRushDay(builder, "Host=localhost;Database=rushday_design");
        return new RushDayDbContext(builder.Options);
    }
}
