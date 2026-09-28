using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace RushDay.Api.Security;

/// <summary>
/// The counterpart of <see cref="AesGcmXmlEncryptor"/>. Data Protection creates it through its activator, which passes
/// the application's <see cref="IServiceProvider"/>; the KEK comes from there. An element encrypted under another KEK
/// is refused with an Error log line instead of a bare cryptographic failure.
/// </summary>
public sealed class AesGcmXmlDecryptor : IXmlDecryptor
{
    public const string WrongKeyMessage = "Data Protection key ring was encrypted under a different key";

    private readonly DataProtectionKeyEncryptionKey? _kek;
    private readonly ILogger _logger;

    public AesGcmXmlDecryptor(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _kek = services.GetService<DataProtectionKeyEncryptionKeyHolder>()?.Key;
        _logger = services.GetService<ILoggerFactory>()?.CreateLogger<AesGcmXmlDecryptor>() ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AesGcmXmlDecryptor>.Instance;
    }

    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        if (_kek is null)
        {
            _logger.LogError("Data Protection key ring is encrypted but DataProtection__KeyEncryptionKey is not configured");
            throw new CryptographicException("The Data Protection key ring is encrypted and no key-encryption key is configured.");
        }

        var keyId = (string?)encryptedElement.Attribute("keyId");
        if (!string.Equals(keyId, _kek.KeyId, StringComparison.Ordinal))
        {
            _logger.LogError(WrongKeyMessage);
            throw new CryptographicException(WrongKeyMessage + ".");
        }

        var nonce = Convert.FromBase64String((string?)encryptedElement.Attribute("nonce") ?? string.Empty);
        var tag = Convert.FromBase64String((string?)encryptedElement.Attribute("tag") ?? string.Empty);
        var ciphertext = Convert.FromBase64String(encryptedElement.Value);
        var plaintext = new byte[ciphertext.Length];

        using (var aes = new AesGcm(_kek.Key, AesGcmXmlEncryptor.TagLength))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AesGcmXmlEncryptor.AssociatedData(_kek.KeyId));
        }

        try
        {
            return XElement.Parse(Encoding.UTF8.GetString(plaintext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}

/// <summary>
/// The KEK as registered in DI: the holder always resolves, its <see cref="Key"/> is null in Development, where the key
/// ring stays in plain form (D31). Registering the holder rather than the key keeps resolution lazy and safe.
/// </summary>
public sealed record DataProtectionKeyEncryptionKeyHolder(DataProtectionKeyEncryptionKey? Key);
