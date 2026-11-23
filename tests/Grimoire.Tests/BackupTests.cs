using System.Security.Cryptography;
using System.Text;
using Grimoire.Api.Backups;
using Grimoire.Infrastructure.Backups;
using Microsoft.Extensions.Configuration;

namespace Grimoire.Tests;

public class BackupCipherTests
{
    private const string Key = "backup-key-for-unit-tests-32-chars!!";

    [Fact]
    public void Encrypt_ThenDecrypt_RoundTrips()
    {
        var data = Encoding.UTF8.GetBytes(
            string.Concat(Enumerable.Repeat("SQLite format 3 ", 500))
        );
        var cipher = new BackupCipher(Key);

        var encrypted = cipher.Encrypt(data);

        Assert.Equal(data, cipher.Decrypt(encrypted));
        Assert.True(encrypted.Length < data.Length, "payload should be gzip-compressed");
        Assert.Equal("GRIMBAK\0"u8.ToArray(), encrypted[..8]);
    }

    [Fact]
    public void Encrypt_UsesFreshNonce_EachTime()
    {
        var cipher = new BackupCipher(Key);
        var data = "same"u8.ToArray();

        Assert.NotEqual(cipher.Encrypt(data), cipher.Encrypt(data));
    }

    [Fact]
    public void Decrypt_WithWrongKey_Throws()
    {
        var encrypted = new BackupCipher(Key).Encrypt("secret"u8);

        Assert.Throws<AuthenticationTagMismatchException>(() =>
            new BackupCipher("another-backup-key-that-is-32-chars!").Decrypt(encrypted)
        );
    }

    [Theory]
    [InlineData(8)] // format version byte (authenticated header)
    [InlineData(40)] // ciphertext
    public void Decrypt_TamperedFile_Throws(int offset)
    {
        var cipher = new BackupCipher(Key);
        var encrypted = cipher.Encrypt(Encoding.UTF8.GetBytes(new string('x', 200)));
        encrypted[offset] ^= 0x01;

        Assert.ThrowsAny<Exception>(() => cipher.Decrypt(encrypted));
    }

    [Fact]
    public void Decrypt_NotABackup_ThrowsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() =>
            new BackupCipher(Key).Decrypt(
                Encoding.UTF8.GetBytes("SQLite format 3\0 and more bytes here")
            )
        );
    }

    [Fact]
    public void BackupKey_IsNotTheSecretsKey()
    {
        // Same passphrase used for both must still yield different keys (different HKDF info).
        var secrets = new Grimoire.Infrastructure.Services.AesGcmEncryptionService(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Encryption:MasterKey"] = Key }
                )
                .Build()
        );
        var backup = new BackupCipher(Key).Encrypt("hello"u8);
        // Re-shape the backup payload as a secret envelope (nonce|tag|ct): must not decrypt.
        var envelope = Convert.ToBase64String(backup[9..]);

        Assert.ThrowsAny<CryptographicException>(() => secrets.Decrypt(envelope));
    }
}

public class BackupNamingTests
{
    [Fact]
    public void For_ThenTryParse_RoundTrips()
    {
        var at = new DateTimeOffset(2026, 11, 19, 3, 0, 5, 123, TimeSpan.Zero);
        var name = BackupNaming.For(at);

        Assert.Equal("grimoire-20261119T030005123Z.gbak", name);
        Assert.True(BackupNaming.TryParse(name, out var parsed));
        Assert.Equal(at, parsed);
    }

    [Fact]
    public void For_NormalizesToUtc()
    {
        var at = new DateTimeOffset(2026, 11, 19, 4, 0, 0, TimeSpan.FromHours(1));

        Assert.Equal("grimoire-20261119T030000000Z.gbak", BackupNaming.For(at));
    }

    [Theory]
    [InlineData("grimoire.db")]
    [InlineData("grimoire-20261119T030005123Z.gbak.tmp")]
    [InlineData("../grimoire-20261119T030005123Z.gbak")]
    [InlineData("grimoire-20261399T030005123Z.gbak")]
    [InlineData("notes.txt")]
    public void IsBackupName_RejectsOtherFiles(string name) =>
        Assert.False(BackupNaming.IsBackupName(name));
}

public class BackupOptionsTests
{
    private static BackupOptions Valid() =>
        new()
        {
            Enabled = true,
            Key = "backup-key-for-unit-tests-32-chars!!",
            Local = new LocalTargetOptions { Enabled = true, Directory = "/tmp/x" },
        };

    [Fact]
    public void Disabled_IsAlwaysValid() => Assert.Empty(new BackupOptions().Validate());

    [Fact]
    public void ValidConfiguration_HasNoErrors() => Assert.Empty(Valid().Validate());

    [Theory]
    [InlineData(null)]
    [InlineData("too-short")]
    [InlineData("change-me-but-it-is-long-enough-now")]
    public void WeakKey_IsRejected(string? key)
    {
        var options = Valid();
        options.Key = key;

        Assert.Contains(options.Validate(), e => e.Contains("Backup:Key"));
    }

    [Fact]
    public void NoTarget_IsRejected()
    {
        var options = Valid();
        options.Local.Enabled = false;

        Assert.Contains(options.Validate(), e => e.Contains("target"));
    }

    [Fact]
    public void S3_RequiresEndpointBucketAndCredentials()
    {
        var options = Valid();
        options.S3 = new S3TargetOptions { Enabled = true };

        var errors = options.Validate();

        Assert.Contains(errors, e => e.Contains("ServiceUrl"));
        Assert.Contains(errors, e => e.Contains("Bucket"));
        Assert.Contains(errors, e => e.Contains("AccessKey"));
    }

    [Fact]
    public void Retention_MustBePositive()
    {
        var options = Valid();
        options.RetentionCount = 0;

        Assert.Contains(options.Validate(), e => e.Contains("RetentionCount"));
    }
}

public class BackupCommandLineTests
{
    [Fact]
    public void NoBackupFlags_LeavesArgsUntouched()
    {
        var (command, remaining) = BackupCommandLine.Parse(["--urls", "http://+:5080"]);

        Assert.Null(command);
        Assert.Equal(["--urls", "http://+:5080"], remaining);
    }

    [Fact]
    public void Restore_WithForce()
    {
        var (command, remaining) = BackupCommandLine.Parse([
            "--restore",
            "/backups/a.gbak",
            "--force",
            "--environment",
            "Production",
        ]);

        Assert.Equal(
            new BackupCommand(BackupCommandKind.Restore, "/backups/a.gbak", true),
            command
        );
        Assert.Equal(["--environment", "Production"], remaining);
    }

    [Fact]
    public void Verify_Latest()
    {
        var (command, _) = BackupCommandLine.Parse(["--verify-backup", "latest"]);

        Assert.Equal(new BackupCommand(BackupCommandKind.Verify, "latest", false), command);
    }

    [Fact]
    public void Backup_TakesNoValue()
    {
        var (command, remaining) = BackupCommandLine.Parse(["--backup"]);

        Assert.Equal(BackupCommandKind.Backup, command!.Kind);
        Assert.Empty(remaining);
    }

    [Theory]
    [InlineData("--restore")]
    [InlineData("--verify-backup")]
    public void MissingSource_Throws(string flag)
    {
        Assert.Throws<ArgumentException>(() => BackupCommandLine.Parse([flag]));
        Assert.Throws<ArgumentException>(() => BackupCommandLine.Parse([flag, "--force"]));
    }
}

public class LocalDirectoryBackupTargetTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        $"grimoire-local-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task Write_List_Read_Delete()
    {
        var target = new LocalDirectoryBackupTarget(_dir);
        var name = BackupNaming.For(DateTimeOffset.UtcNow);

        await target.WriteAsync(name, [1, 2, 3], CancellationToken.None);
        File.WriteAllText(Path.Combine(_dir, "unrelated.txt"), "keep me");

        var listed = Assert.Single(await target.ListAsync(CancellationToken.None));
        Assert.Equal(new BackupObject(name, 3), listed);
        Assert.Equal([1, 2, 3], await target.ReadAsync(name, CancellationToken.None));

        await target.DeleteAsync(name, CancellationToken.None);
        Assert.Empty(await target.ListAsync(CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(_dir, "unrelated.txt")));
    }

    [Fact]
    public async Task CreatesDirectory_OwnerOnly()
    {
        var target = new LocalDirectoryBackupTarget(_dir);
        var name = BackupNaming.For(DateTimeOffset.UtcNow);

        await target.WriteAsync(name, [1], CancellationToken.None);

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(_dir)
            );
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(Path.Combine(_dir, name))
            );
        }
    }

    [Fact]
    public async Task CancelledWrite_LeavesNoTemporaryFiles()
    {
        var target = new LocalDirectoryBackupTarget(_dir);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            target.WriteAsync(BackupNaming.For(DateTimeOffset.UtcNow), new byte[1024], cts.Token)
        );

        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task RejectsNonBackupNames()
    {
        var target = new LocalDirectoryBackupTarget(_dir);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            target.WriteAsync("../escape.gbak", [1], CancellationToken.None)
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }
}
