using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using RushDay.Api.Options;
using RushDay.Api.Security;

namespace RushDay.UnitTests.Security;

/// <summary>Address keys, host lists and the key-ring check (03-security.md sections 2, 3 and T18).</summary>
public sealed class SecurityPrimitivesTests
{
    [Theory]
    [InlineData("198.51.100.7", "198.51.100.7")]
    [InlineData("::ffff:198.51.100.7", "198.51.100.7")]
    [InlineData("2001:db8:1:1::1", "2001:db8:1:1::/64")]
    [InlineData("2001:db8:1:1:ffff:ffff:ffff:3", "2001:db8:1:1::/64")]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2::/64")]
    [InlineData("::1", "::/64")]
    public void Client_key_is_ipv4_or_the_ipv6_64(string address, string key)
    {
        Assert.Equal(key, RateLimitPolicies.ClientKey(IPAddress.Parse(address)));
    }

    [Fact]
    public void Client_key_without_an_address_is_unknown()
    {
        Assert.Equal("unknown", RateLimitPolicies.ClientKey((IPAddress?)null));
    }

    [Fact]
    public void Render_hostname_is_allowed_alongside_configured_hosts()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [HostFilteringSetup.RenderHostnameVariable] = "rushday-api.onrender.com" })
            .Build();
        var production = new HostingEnvironment { EnvironmentName = Environments.Production };

        Assert.Equal(
            ["portal.example.ac.uk", "rushday-api.onrender.com"],
            HostFilteringSetup.ResolveAllowedHosts(configuration, production, new SecurityOptions { AllowedHosts = "portal.example.ac.uk" }));
        Assert.Equal(
            ["RUSHDAY-API.onrender.com"],
            HostFilteringSetup.ResolveAllowedHosts(configuration, production, new SecurityOptions { AllowedHosts = "RUSHDAY-API.onrender.com" }));
        Assert.Equal(["*"], HostFilteringSetup.ResolveAllowedHosts(new ConfigurationBuilder().Build(), production, new SecurityOptions()));
    }

    [Fact]
    public void Plaintext_key_elements_are_recognised()
    {
        const string plain = """<key id="3b1a2c4d-0000-4000-8000-000000000001" version="1"><descriptor><descriptor><masterKey><value>AAAA</value></masterKey></descriptor></descriptor></key>""";
        const string encrypted = """<key id="3b1a2c4d-0000-4000-8000-000000000002" version="1"><descriptor><descriptor><encryptedSecret decryptorType="x" xmlns="http://schemas.asp.net/2015/03/dataProtection"><rushdayEncryptedKey keyId="ab">x</rushdayEncryptedKey></encryptedSecret></descriptor></descriptor></key>""";
        const string revocation = """<revocation version="1"><revocationDate>2026-09-27T12:00:00Z</revocationDate><key id="3b1a2c4d-0000-4000-8000-000000000001" /><reason>r</reason></revocation>""";

        Assert.Equal(Guid.Parse("3b1a2c4d-0000-4000-8000-000000000001"), KeyRingHygiene.PlaintextKeyId(plain));
        Assert.Null(KeyRingHygiene.PlaintextKeyId(encrypted));
        Assert.Null(KeyRingHygiene.PlaintextKeyId(revocation));
        Assert.Null(KeyRingHygiene.PlaintextKeyId("not xml"));
    }

    [Fact]
    public void Hsts_value_follows_the_options()
    {
        Assert.Equal("max-age=31536000", SecurityHeadersMiddleware.HstsValue(new Microsoft.AspNetCore.HttpsPolicy.HstsOptions { MaxAge = TimeSpan.FromDays(365) }));
        Assert.Equal(
            "max-age=31536000; includeSubDomains; preload",
            SecurityHeadersMiddleware.HstsValue(new Microsoft.AspNetCore.HttpsPolicy.HstsOptions { MaxAge = TimeSpan.FromDays(365), IncludeSubDomains = true, Preload = true }));
    }
}
