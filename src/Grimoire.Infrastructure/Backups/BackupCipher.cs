using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Grimoire.Infrastructure.Backups;

/// <summary>
/// Encrypted backup file format (<c>.gbak</c>):
/// <code>
/// magic "GRIMBAK\0" (8) | format version (1) | nonce (12) | tag (16) | AES-256-GCM(gzip(sqlite file))
/// </code>
/// The 9-byte header is authenticated as associated data. The key is derived from
/// <c>Backup:Key</c> with HKDF-SHA256 and a backup-specific info string, so it never equals
/// the key that protects secret values.
/// </summary>
public sealed class BackupCipher
{
    public const byte FormatVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Magic = "GRIMBAK\0"u8.ToArray();
    private static readonly int HeaderSize = Magic.Length + 1;

    private readonly byte[] _key;

    public BackupCipher(string passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
            throw new ArgumentException("A backup key is required.", nameof(passphrase));

        _key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(passphrase),
            32,
            salt: null,
            info: "grimoire-backup-key-v1"u8.ToArray()
        );
    }

    /// <summary>Compresses and encrypts <paramref name="plaintext"/> into the backup format.</summary>
    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        var compressed = Compress(plaintext);

        var output = new byte[HeaderSize + NonceSize + TagSize + compressed.Length];
        var header = output.AsSpan(0, HeaderSize);
        Magic.CopyTo(header);
        header[^1] = FormatVersion;

        var nonce = output.AsSpan(HeaderSize, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        var tag = output.AsSpan(HeaderSize + NonceSize, TagSize);
        var ciphertext = output.AsSpan(HeaderSize + NonceSize + TagSize);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, compressed, ciphertext, tag, header);
        CryptographicOperations.ZeroMemory(compressed);
        return output;
    }

    /// <summary>Decrypts and decompresses a backup file.</summary>
    /// <exception cref="InvalidDataException">Not a Grimoire backup, or an unknown format version.</exception>
    /// <exception cref="AuthenticationTagMismatchException">Wrong key, or the file was modified.</exception>
    public byte[] Decrypt(ReadOnlySpan<byte> backup)
    {
        if (
            backup.Length < HeaderSize + NonceSize + TagSize
            || !backup[..Magic.Length].SequenceEqual(Magic)
        )
            throw new InvalidDataException("Not a Grimoire backup file.");

        var header = backup[..HeaderSize];
        if (header[^1] != FormatVersion)
            throw new InvalidDataException($"Unsupported backup format version {header[^1]}.");

        var nonce = backup.Slice(HeaderSize, NonceSize);
        var tag = backup.Slice(HeaderSize + NonceSize, TagSize);
        var ciphertext = backup[(HeaderSize + NonceSize + TagSize)..];

        var compressed = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, compressed, header);

        try
        {
            return Decompress(compressed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(compressed);
        }
    }

    private static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(data);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
