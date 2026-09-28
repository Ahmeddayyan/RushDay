using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RushDay.Infrastructure.Persistence;

/// <summary>
/// Used only by the dotnet-ef tooling. Adding migrations never opens a connection, so the default connection
/// string is a placeholder; commands that do connect (<c>database update</c>, <c>migrations remove</c>) target
/// whatever <c>ConnectionStrings__RushDay</c> names, which is how a rehearsal clone is pointed at.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RushDayDbContext>
{
    public const string ConnectionVariable = "ConnectionStrings__RushDay";
    public const string PlaceholderConnectionString = "Host=localhost;Database=rushday_design";

    public RushDayDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable) is { Length: > 0 } configured
            ? configured
            : PlaceholderConnectionString;

        var builder = new DbContextOptionsBuilder<RushDayDbContext>();
        DependencyInjection.ConfigureRushDay(builder, connectionString);
        return new RushDayDbContext(builder.Options);
    }
}
