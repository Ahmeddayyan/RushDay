using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.KeyManagement.Internal;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using RushDay.Api.Security;
using RushDay.Infrastructure.Persistence;

namespace RushDay.IntegrationTests.Security;

/// <summary>The key ring is encrypted at rest under the KEK outside Development (D31, T18).</summary>
[Collection(ApiCollection.Name)]
public sealed class DataProtectionTests(RushDayApiFactory factory)
{
    [Fact]
    public async Task Key_xml_is_not_plaintext()
    {
        await using var production = factory.Production();
        var keyManager = production.Services.GetRequiredService<IKeyManager>();

        // Activation far ahead, so the new key never becomes any test host's default.
        var now = DateTimeOffset.UtcNow;
        var key = keyManager.CreateNewKey(now.AddYears(5), now.AddYears(6));

        await using var scope = production.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
        var row = await db.DataProtectionKeys.AsNoTracking().SingleAsync(k => k.FriendlyName == "key-" + key.KeyId.ToString());
        var expectedKeyId = new DataProtectionKeyEncryptionKey(Convert.FromBase64String(RushDayApiFactory.TestKeyEncryptionKey)).KeyId;

        Assert.Contains("<rushdayEncryptedKey", row.Xml, StringComparison.Ordinal);
        Assert.Contains($"keyId=\"{expectedKeyId}\"", row.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<masterKey", row.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<value>", row.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("unencrypted", row.Xml, StringComparison.OrdinalIgnoreCase);

        // And it reads back: the descriptor decrypts under the same KEK.
        var reloaded = Assert.Single(keyManager.GetAllKeys(), k => k.KeyId == key.KeyId);
        Assert.NotNull(reloaded.Descriptor);
    }

    /// <summary>
    /// A key written in clear (by a Development host on the same database, or before the KEK was set) must not stay
    /// usable once a Production host starts: it is revoked, logged as an Error, and an encrypted key becomes the default.
    /// </summary>
    [Fact]
    public async Task Plaintext_keys_are_revoked_when_a_production_host_starts()
    {
        var plaintext = PlaintextKeyElement(RushDayApiFactory.ClockStart.AddDays(-1), DateTimeOffset.UtcNow.AddDays(90));
        var plaintextId = Guid.Parse((string)plaintext.Attribute("id")!);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
            db.DataProtectionKeys.Add(new DataProtectionKey { FriendlyName = "key-" + plaintextId, Xml = plaintext.ToString(SaveOptions.DisableFormatting) });
            await db.SaveChangesAsync();
        }

        await using var production = factory.Production(b => b.ConfigureTestServices(s => s.AddFakeLogging()));
        var keyManager = production.Services.GetRequiredService<IKeyManager>();

        Assert.True(Assert.Single(keyManager.GetAllKeys(), k => k.KeyId == plaintextId).IsRevoked);
        var defaultKey = production.Services.GetRequiredService<IKeyRingProvider>().GetCurrentKeyRing().DefaultKeyId;
        Assert.NotEqual(plaintextId, defaultKey);
        await using (var scope = production.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RushDayDbContext>();
            var row = await db.DataProtectionKeys.AsNoTracking().SingleAsync(k => k.FriendlyName == "key-" + defaultKey);
            Assert.Contains("<rushdayEncryptedKey", row.Xml, StringComparison.Ordinal);
        }

        var error = Assert.Single(production.Services.GetFakeLogCollector().GetSnapshot(), r => r.Level == LogLevel.Error && r.Message.Contains(plaintextId.ToString(), StringComparison.Ordinal));
        Assert.Equal($"Plaintext Data Protection key {plaintextId} revoked", error.Message);

        // The new (encrypted) default key protects and unprotects.
        var protector = production.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("rushday.test");
        Assert.Equal("payload", protector.Unprotect(protector.Protect("payload")));
    }

    [Fact]
    public void Encryptor_round_trips_and_refuses_another_kek()
    {
        var kek = new DataProtectionKeyEncryptionKey(RandomNumberGenerator.GetBytes(32));
        var other = new DataProtectionKeyEncryptionKey(RandomNumberGenerator.GetBytes(32));
        var secret = new XElement("masterKey", new XElement("value", Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))));

        var encrypted = new AesGcmXmlEncryptor(kek).Encrypt(secret);

        Assert.Equal(typeof(AesGcmXmlDecryptor), encrypted.DecryptorType);
        Assert.DoesNotContain(secret.Element("value")!.Value, encrypted.EncryptedElement.ToString(), StringComparison.Ordinal);
        Assert.Equal(secret.ToString(), Decryptor(kek, out _).Decrypt(encrypted.EncryptedElement).ToString());

        var refusing = Decryptor(other, out var logs);
        Assert.ThrowsAny<CryptographicException>(() => refusing.Decrypt(encrypted.EncryptedElement));
        var error = Assert.Single(logs.GetSnapshot(), r => r.Level == LogLevel.Error);
        Assert.Equal(AesGcmXmlDecryptor.WrongKeyMessage, error.Message);
    }

    [Fact]
    public void Tampered_ciphertext_is_refused()
    {
        var kek = new DataProtectionKeyEncryptionKey(RandomNumberGenerator.GetBytes(32));
        var encrypted = new AesGcmXmlEncryptor(kek).Encrypt(new XElement("masterKey", "secret")).EncryptedElement;
        var bytes = Convert.FromBase64String(encrypted.Value);
        bytes[0] ^= 0xFF;
        encrypted.Value = Convert.ToBase64String(bytes);

        Assert.ThrowsAny<CryptographicException>(() => Decryptor(kek, out _).Decrypt(encrypted));
    }

    [Fact]
    public void Development_keeps_plain_keys()
    {
        var holder = factory.Services.GetRequiredService<DataProtectionKeyEncryptionKeyHolder>();
        var options = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<KeyManagementOptions>>().Value;

        Assert.Null(holder.Key);
        Assert.Null(options.XmlEncryptor);
    }

    /// <summary>A key element as a host without an XML encryptor writes it (the master key in clear).</summary>
    private static XElement PlaintextKeyElement(DateTimeOffset activation, DateTimeOffset expiration)
    {
        var repository = new MemoryXmlRepository();
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName("RushDay");
        services.Configure<KeyManagementOptions>(o =>
        {
            o.XmlRepository = repository;
            o.XmlEncryptor = null;
        });
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IKeyManager>().CreateNewKey(activation, expiration);
        return Assert.Single(repository.GetAllElements());
    }

    private static AesGcmXmlDecryptor Decryptor(DataProtectionKeyEncryptionKey kek, out FakeLogCollector logs)
    {
        var services = new ServiceCollection()
            .AddSingleton(new DataProtectionKeyEncryptionKeyHolder(kek))
            .AddFakeLogging()
            .BuildServiceProvider();
        logs = services.GetFakeLogCollector();
        return new AesGcmXmlDecryptor(services);
    }

    private sealed class MemoryXmlRepository : IXmlRepository
    {
        private readonly List<XElement> _elements = [];

        public IReadOnlyCollection<XElement> GetAllElements() => [.. _elements.Select(e => new XElement(e))];

        public void StoreElement(XElement element, string friendlyName) => _elements.Add(new XElement(element));
    }
}
