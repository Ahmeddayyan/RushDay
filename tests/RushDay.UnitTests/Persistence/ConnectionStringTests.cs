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
        Assert.Equal(5, built.Timeout);
        Assert.Equal(10, built.CommandTimeout);
        Assert.Equal(60, built.ConnectionIdleLifetime);
        Assert.Equal(10, built.ConnectionPruningInterval);
        Assert.Equal("rushday-api", built.ApplicationName);
        Assert.False(built.IncludeErrorDetail);
        Assert.Equal("localhost", built.Host);
        Assert.Equal("rushday", built.Database);
    }

    /// <summary>
    /// 04-performance-and-ops.md section 5: no minimum pool and no keepalive, or two always-open connections
    /// pinging every 30 s would keep Neon's compute from auto-suspending.
    /// </summary>
    [Fact]
    public void Leaves_the_minimum_pool_at_zero_and_sets_no_keepalive()
    {
        var built = new NpgsqlConnectionStringBuilder(DependencyInjection.BuildConnectionString(Minimal, 20));

        Assert.Equal(0, built.MinPoolSize);
        Assert.Equal(0, built.KeepAlive);
        Assert.False(built.ShouldSerialize("Minimum Pool Size"));
        Assert.False(built.ShouldSerialize("Keepalive"));
    }

    [Fact]
    public void Keeps_an_explicit_minimum_pool_and_keepalive()
    {
        var incoming = Minimal + ";Minimum Pool Size=2;Keepalive=30";

        var built = new NpgsqlConnectionStringBuilder(DependencyInjection.BuildConnectionString(incoming, 20));

        Assert.Equal(2, built.MinPoolSize);
        Assert.Equal(30, built.KeepAlive);
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

        var built = new NpgsqlConnectionStringBuilder(DependencyInjection.BuildConnectionString(incoming, 20, allowErrorDetail: true));

        Assert.Equal(7, built.MaxPoolSize);
        Assert.Equal(42, built.CommandTimeout);
        Assert.True(built.IncludeErrorDetail);
        Assert.Equal("custom", built.ApplicationName);
        Assert.Equal(5, built.Timeout);
        Assert.Equal(0, built.MinPoolSize);
    }

    [Fact]
    public void Recognises_keyword_aliases_as_already_set()
    {
        var incoming = Minimal + ";MaxPoolSize=9;CommandTimeout=33";

        var built = new NpgsqlConnectionStringBuilder(DependencyInjection.BuildConnectionString(incoming, 20));

        Assert.Equal(9, built.MaxPoolSize);
        Assert.Equal(33, built.CommandTimeout);
    }

    /// <summary>
    /// Every setting the builder applies must survive an operator override spelled with any of Npgsql's keyword
    /// aliases and in any case (04-performance-and-ops.md section 5: "only when the incoming string does not
    /// already set them"). Npgsql's aliases are the canonical display name and the property name; SqlClient's
    /// "Max Pool Size" / "Min Pool Size" are not Npgsql keywords and are rejected by Npgsql itself before the
    /// builder sees them, so they are not cases here.
    /// </summary>
    [Theory]
    [InlineData("Maximum Pool Size=7", nameof(NpgsqlConnectionStringBuilder.MaxPoolSize), 7)]
    [InlineData("MaxPoolSize=7", nameof(NpgsqlConnectionStringBuilder.MaxPoolSize), 7)]
    [InlineData("maximum pool size=7", nameof(NpgsqlConnectionStringBuilder.MaxPoolSize), 7)]
    [InlineData("Minimum Pool Size=3", nameof(NpgsqlConnectionStringBuilder.MinPoolSize), 3)]
    [InlineData("MinPoolSize=3", nameof(NpgsqlConnectionStringBuilder.MinPoolSize), 3)]
    [InlineData("Timeout=9", nameof(NpgsqlConnectionStringBuilder.Timeout), 9)]
    [InlineData("timeout=9", nameof(NpgsqlConnectionStringBuilder.Timeout), 9)]
    [InlineData("Command Timeout=42", nameof(NpgsqlConnectionStringBuilder.CommandTimeout), 42)]
    [InlineData("CommandTimeout=42", nameof(NpgsqlConnectionStringBuilder.CommandTimeout), 42)]
    [InlineData("Connection Idle Lifetime=15", nameof(NpgsqlConnectionStringBuilder.ConnectionIdleLifetime), 15)]
    [InlineData("ConnectionIdleLifetime=15", nameof(NpgsqlConnectionStringBuilder.ConnectionIdleLifetime), 15)]
    [InlineData("Connection Pruning Interval=4", nameof(NpgsqlConnectionStringBuilder.ConnectionPruningInterval), 4)]
    [InlineData("ConnectionPruningInterval=4", nameof(NpgsqlConnectionStringBuilder.ConnectionPruningInterval), 4)]
    [InlineData("Keepalive=7", nameof(NpgsqlConnectionStringBuilder.KeepAlive), 7)]
    [InlineData("KeepAlive=7", nameof(NpgsqlConnectionStringBuilder.KeepAlive), 7)]
    public void Keeps_every_numeric_override_whatever_alias_spells_it(string fragment, string property, int expected)
    {
        var built = new NpgsqlConnectionStringBuilder(DependencyInjection.BuildConnectionString(Minimal + ";" + fragment, 20));

        var actual = typeof(NpgsqlConnectionStringBuilder).GetProperty(property)!.GetValue(built);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Max Pool Size=7")]
    [InlineData("Min Pool Size=3")]
    public void Rejects_keywords_npgsql_does_not_know_before_applying_defaults(string fragment)
    {
        Assert.Throws<ArgumentException>(() => DependencyInjection.BuildConnectionString(Minimal + ";" + fragment, 20));
    }

    [Theory]
    [InlineData("Application Name=custom", nameof(NpgsqlConnectionStringBuilder.ApplicationName), "custom")]
    [InlineData("ApplicationName=custom", nameof(NpgsqlConnectionStringBuilder.ApplicationName), "custom")]
    [InlineData("Include Error Detail=true", nameof(NpgsqlConnectionStringBuilder.IncludeErrorDetail), true)]
    [InlineData("IncludeErrorDetail=true", nameof(NpgsqlConnectionStringBuilder.IncludeErrorDetail), true)]
    public void Keeps_every_text_and_boolean_override_whatever_alias_spells_it(string fragment, string property, object expected)
    {
        var built = new NpgsqlConnectionStringBuilder(DependencyInjection.BuildConnectionString(Minimal + ";" + fragment, 20, allowErrorDetail: true));

        var actual = typeof(NpgsqlConnectionStringBuilder).GetProperty(property)!.GetValue(built);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Outside Development (the default) the one setting an operator cannot switch on: the error detail carries row
    /// values, marks among them, into exceptions and logs (03-security.md T10).
    /// </summary>
    [Theory]
    [InlineData("Include Error Detail=true")]
    [InlineData("IncludeErrorDetail=true")]
    [InlineData("include error detail=True")]
    public void Include_error_detail_is_forced_off_unless_allowed(string fragment)
    {
        var built = new NpgsqlConnectionStringBuilder(DependencyInjection.BuildConnectionString(Minimal + ";" + fragment, 20));

        Assert.False(built.IncludeErrorDetail);
    }

    [Fact]
    public void Passes_through_unrelated_settings_such_as_neon_tls_options()
    {
        var incoming = Minimal + ";SSL Mode=Require;Channel Binding=Require;Pooling=false";

        var built = new NpgsqlConnectionStringBuilder(DependencyInjection.BuildConnectionString(incoming, 20));

        Assert.Equal(SslMode.Require, built.SslMode);
        Assert.Equal(ChannelBinding.Require, built.ChannelBinding);
        Assert.False(built.Pooling);
    }

    [Fact]
    public void Rejects_blank_input()
    {
        Assert.Throws<ArgumentException>(() => DependencyInjection.BuildConnectionString(" ", 20));
    }
}
