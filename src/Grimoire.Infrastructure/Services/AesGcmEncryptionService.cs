using System.Security.Cryptography;
using System.Text;
using Grimoire.Core.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Grimoire.Infrastructure.Services;

public class AesGcmEncryptionService : IEncryptionService
{
    private readonly byte[] _key;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public AesGcmEncryptionService(IConfiguration configuration)
    {
        var masterKey =
            configuration["Encryption:MasterKey"]
            ?? throw new InvalidOperationException("Encryption:MasterKey is not configured.");
        var masterBytes = Encoding.UTF8.GetBytes(masterKey);
        // HKDF derive a 32-byte key
        _key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            masterBytes,
            32,
            salt: null,
            info: "grimoire-aes-key"u8.ToArray()
        );
    }

    public string Encrypt(string plaintext)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Layout: nonce (12) + tag (16) + ciphertext
        var combined = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(combined, 0);
        tag.CopyTo(combined, NonceSize);
        ciphertext.CopyTo(combined, NonceSize + TagSize);

        return Convert.ToBase64String(combined);
    }

    public string Decrypt(string ciphertext)
    {
        var combined = Convert.FromBase64String(ciphertext);
        var nonce = combined[..NonceSize];
        var tag = combined[NonceSize..(NonceSize + TagSize)];
        var encryptedData = combined[(NonceSize + TagSize)..];
        var plaintext = new byte[encryptedData.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, encryptedData, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}
