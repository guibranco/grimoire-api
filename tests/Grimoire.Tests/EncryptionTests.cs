using Grimoire.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Grimoire.Tests;

public class EncryptionTests
{
    private static AesGcmEncryptionService CreateService() =>
        new(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Encryption:MasterKey"] = "test-master-key-32-chars-minimum!",
                    }
                )
                .Build()
        );

    [Fact]
    public void Encrypt_Decrypt_RoundTrip()
    {
        var svc = CreateService();
        var plaintext = "super-secret-password-123";

        var encrypted = svc.Encrypt(plaintext);
        var decrypted = svc.Decrypt(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertexts_EachTime()
    {
        var svc = CreateService();
        var plain = "same-value";

        var c1 = svc.Encrypt(plain);
        var c2 = svc.Encrypt(plain);

        Assert.NotEqual(c1, c2); // nonce randomness
    }

    [Fact]
    public void Encrypt_ReturnsBase64()
    {
        var svc = CreateService();
        var encrypted = svc.Encrypt("test");

        var bytes = Convert.FromBase64String(encrypted);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void Decrypt_ThrowsOnTamperedData()
    {
        var svc = CreateService();
        var encrypted = svc.Encrypt("test-value");
        var bytes = Convert.FromBase64String(encrypted);
        bytes[20] ^= 0xFF; // tamper with ciphertext
        var tampered = Convert.ToBase64String(bytes);

        Assert.ThrowsAny<Exception>(() => svc.Decrypt(tampered));
    }
}
