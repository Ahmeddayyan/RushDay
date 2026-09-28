using Npgsql;
using RushDay.Infrastructure;

namespace RushDay.UnitTests.Persistence;

public sealed class ConnectionStringTests
{
    private const string Minimal = "Host=localhost;Port=5432;Database=rushday;Username=rushday;Password=rushday";

    [Fact]
    public void Applies_the_pool_and_timeout_settings_when_the_string_does_not_set_them()
    {
        var built = new NpgsqlConnectionStringBuilder(DependencyInjection.BuildConnectionString(Minimal, 20));

        Assert.Equal(20, built.MaxPoolSize);
        Assert.Equal(2, built.MinPoolSize);
        Assert.Equal(5, built.Timeout);
        Assert.Equal(10, built.CommandTimeout);
        Assert.Equal(60, built.ConnectionIdleLifetime);
        Assert.Equal(10, built.ConnectionPruningInterval);
        Assert.Equal(30, built.KeepAlive);
        Assert.Equal("rushday-api", built.ApplicationName);
        Assert.False(built.IncludeErrorDetail);
        Assert.Equal("localhost", built.Host);
        Assert.Equal("rushday", built.Database);
    }

    [Fact]
    public void Uses_the_configured_maximum_pool_size()
    {
        var built = new NpgsqlConnectionStringBuilder(DependencyInjection.BuildConnectionString(Minimal, 40));

        Assert.Equal(40, built.MaxPoolSize);
    }

    [Fact]
    public void Keeps_values_the_incoming_string_already_sets()
    {
        var incoming = Minimal + ";Maximum Pool Size=7;Command Timeout=42;Include Error Detail=true;Application Name=custom";

        var built = new NpgsqlConnectionStringBuilder(DependencyInjection.BuildConnectionString(incoming, 20));

        Assert.Equal(7, built.MaxPoolSize);
        Assert.Equal(42, built.CommandTimeout);
        Assert.True(built.IncludeErrorDetail);
        Assert.Equal("custom", built.ApplicationName);
        Assert.Equal(5, built.Timeout);
        Assert.Equal(2, built.MinPoolSize);
    }

    [Fact]
    public void Recognises_keyword_aliases_as_already_set()
    {
        var incoming = Minimal + ";MaxPoolSize=9;CommandTimeout=33";

        var built = new NpgsqlConnectionStringBuilder(DependencyInjection.BuildConnectionString(incoming, 20));

        Assert.Equal(9, built.MaxPoolSize);
        Assert.Equal(33, built.CommandTimeout);
    }

    [Fact]
    public void Rejects_blank_input()
    {
        Assert.Throws<ArgumentException>(() => DependencyInjection.BuildConnectionString(" ", 20));
    }
}
