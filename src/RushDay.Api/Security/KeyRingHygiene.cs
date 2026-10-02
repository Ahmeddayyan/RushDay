using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.KeyManagement.Internal;
using Microsoft.EntityFrameworkCore;
using RushDay.Infrastructure.Persistence;

namespace RushDay.Api.Security;

/// <summary>
/// The startup key-ring check outside Development (D31, T18). The <c>XmlEncryptor</c> only encrypts keys created from
/// now on; a key already stored in plaintext (created before the KEK was set, or by a Development host sharing the
/// database) would stay usable, and anyone with a database dump could mint sessions with it. Every such key is revoked
/// through <see cref="IKeyManager"/> (logged as an Error), and the key ring is then resolved so a fresh encrypted
/// default key exists before the first request.
/// </summary>
public static class KeyRingHygiene
{
    public const string RevokedMessage = "Plaintext Data Protection key {KeyId} revoked";

    public const string RevocationReason = "Stored without encryption; revoked at startup (03-security.md T18).";

    private static readonly XNamespace DataProtectionNamespace = "http://schemas.asp.net/2015/03/dataProtection";

    /// <summary>Revokes every stored key without an encrypted secret; returns how many were revoked by this call.</summary>
    public static async Task<int> RevokePlaintextKeysAsync(IServiceProvider services, RushDayDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        var rows = await db.DataProtectionKeys.AsNoTracking().Select(k => k.Xml).ToListAsync(cancellationToken);
        var plaintext = rows.Select(PlaintextKeyId).OfType<Guid>().ToHashSet();

        var keyManager = services.GetRequiredService<IKeyManager>();
        var revoked = 0;
        foreach (var key in keyManager.GetAllKeys().Where(k => plaintext.Contains(k.KeyId) && !k.IsRevoked))
        {
            keyManager.RevokeKey(key.KeyId, RevocationReason);
            logger.LogError(RevokedMessage, key.KeyId);
            revoked++;
        }

        // Resolving the ring makes the key manager create (and encrypt) a new default key when none is left.
        var ring = services.GetRequiredService<IKeyRingProvider>().GetCurrentKeyRing();
        if (plaintext.Contains(ring.DefaultKeyId))
        {
            throw new InvalidOperationException($"Data Protection default key {ring.DefaultKeyId} is stored in plaintext and could not be replaced.");
        }

        return revoked;
    }

    /// <summary>The id of a stored <c>&lt;key&gt;</c> element whose secret is not encrypted; null otherwise.</summary>
    public static Guid? PlaintextKeyId(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        XElement element;
        try
        {
            element = XElement.Parse(xml);
        }
        catch (XmlException)
        {
            return null;
        }

        // Revocation elements (<revocation>) also live in the table and carry a nested <key id>; only root keys count.
        if (element.Name.LocalName != "key" || !Guid.TryParse((string?)element.Attribute("id"), out var id))
        {
            return null;
        }

        var encrypted = element.Descendants(DataProtectionNamespace + "encryptedSecret").Any()
            || element.Descendants().Any(e => e.Name.LocalName == "encryptedSecret");
        return encrypted ? null : id;
    }
}
