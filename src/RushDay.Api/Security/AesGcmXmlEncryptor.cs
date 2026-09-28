using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace RushDay.Api.Security;

/// <summary>
/// Encrypts each Data Protection key's secret with AES-256-GCM under the KEK before it reaches
/// <c>data_protection_keys</c> (D31, 02-api.md section 2.1), so a stolen database dump cannot mint a session cookie.
/// Output: <c>&lt;rushdayEncryptedKey keyId="…" nonce="…" tag="…"&gt;base64&lt;/rushdayEncryptedKey&gt;</c> with a
/// random 12-byte nonce and a 16-byte tag.
/// </summary>
public sealed class AesGcmXmlEncryptor(DataProtectionKeyEncryptionKey kek) : IXmlEncryptor
{
    public const string ElementName = "rushdayEncryptedKey";
    public const int NonceLength = 12;
    public const int TagLength = 16;

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var tag = new byte[TagLength];
        var ciphertext = new byte[plaintext.Length];

        using (var aes = new AesGcm(kek.Key, TagLength))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(kek.KeyId));
        }

        CryptographicOperations.ZeroMemory(plaintext);

        var element = new XElement(
            ElementName,
            new XAttribute("keyId", kek.KeyId),
            new XAttribute("nonce", Convert.ToBase64String(nonce)),
            new XAttribute("tag", Convert.ToBase64String(tag)),
            Convert.ToBase64String(ciphertext));

        return new EncryptedXmlInfo(element, typeof(AesGcmXmlDecryptor));
    }

    /// <summary>Binds the ciphertext to the KEK id, so the id attribute cannot be swapped without detection.</summary>
    internal static byte[] AssociatedData(string keyId) =>
        Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"rushday.dp-key:{keyId}"));
}
