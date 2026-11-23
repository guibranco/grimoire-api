using System.Net;
using System.Reflection;
using Amazon.S3;
using Amazon.S3.Model;
using Grimoire.Api.Backups;
using Grimoire.Core.Backups;
using Grimoire.Core.Interfaces;
using Grimoire.Infrastructure.Backups;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Grimoire.Tests;

/// <summary>Records every request URL and answers with a fixed status.</summary>
internal sealed class RecordingHandler(HttpStatusCode status = HttpStatusCode.OK)
    : HttpMessageHandler
{
    public List<string> Urls { get; } = [];
    public Exception? Throw { get; init; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Urls.Add(request.RequestUri!.ToString());
        if (Throw is not null)
            throw Throw;
        return Task.FromResult(new HttpResponseMessage(status));
    }
}

/// <summary>In-memory target; can be told to fail writes.</summary>
internal sealed class MemoryTarget(string name, bool failWrites = false) : IBackupTarget
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
    public string Name => name;

    public Task WriteAsync(string fileName, byte[] content, CancellationToken ct)
    {
        if (failWrites)
            throw new IOException("disk full");
        Files[fileName] = content;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BackupObject>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BackupObject>>(
            Files.Select(f => new BackupObject(f.Key, f.Value.Length)).ToList()
        );

    public Task<byte[]> ReadAsync(string fileName, CancellationToken ct) =>
        Task.FromResult(Files[fileName]);

    public Task DeleteAsync(string fileName, CancellationToken ct)
    {
        Files.Remove(fileName);
        return Task.CompletedTask;
    }
}

/// <summary>A wall clock that can step backwards (FakeTimeProvider refuses to).</summary>
internal sealed class SettableClock : TimeProvider
{
    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class HealthcheckPingerTests
{
    [Fact]
    public async Task Success_PingsUrl_Failure_PingsFailSuffix()
    {
        var handler = new RecordingHandler();
        var pinger = new HealthcheckPinger(
            new HttpClient(handler),
            NullLogger<HealthcheckPinger>.Instance
        );

        await pinger.SuccessAsync("https://hc-ping.com/abc", CancellationToken.None);
        await pinger.FailureAsync("https://hc-ping.com/abc/", CancellationToken.None);

        Assert.Equal(["https://hc-ping.com/abc", "https://hc-ping.com/abc/fail"], handler.Urls);
    }

    [Fact]
    public async Task NoUrl_DoesNothing()
    {
        var handler = new RecordingHandler();
        var pinger = new HealthcheckPinger(
            new HttpClient(handler),
            NullLogger<HealthcheckPinger>.Instance
        );

        await pinger.SuccessAsync(null, CancellationToken.None);
        await pinger.FailureAsync(" ", CancellationToken.None);

        Assert.Empty(handler.Urls);
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("http://")]
    [InlineData("ftp://example.com/check")]
    public async Task InvalidUrl_IsSwallowed(string url)
    {
        var pinger = new HealthcheckPinger(
            new HttpClient(),
            NullLogger<HealthcheckPinger>.Instance
        );

        var error = await Record.ExceptionAsync(async () =>
        {
            await pinger.SuccessAsync(url, CancellationToken.None);
            await pinger.FailureAsync(url, CancellationToken.None);
        });

        Assert.Null(error);
    }

    [Fact]
    public async Task ErrorsAndBadStatus_AreSwallowed()
    {
        var failing = new HealthcheckPinger(
            new HttpClient(new RecordingHandler { Throw = new HttpRequestException("dns") }),
            NullLogger<HealthcheckPinger>.Instance
        );
        var notFound = new HealthcheckPinger(
            new HttpClient(new RecordingHandler(HttpStatusCode.NotFound)),
            NullLogger<HealthcheckPinger>.Instance
        );

        var error = await Record.ExceptionAsync(async () =>
        {
            await failing.SuccessAsync("https://hc-ping.com/x", CancellationToken.None);
            await notFound.SuccessAsync("https://hc-ping.com/x", CancellationToken.None);
        });

        Assert.Null(error);
    }
}

public sealed class BackupServiceTests : IDisposable
{
    private const string Key = "backup-key-for-unit-tests-32-chars!!";
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"grimoire-svc-{Guid.NewGuid():N}.db"
    );
    private readonly RecordingHandler _pings = new();

    public BackupServiceTests()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE T (V TEXT); INSERT INTO T VALUES ('hello');";
        command.ExecuteNonQuery();
    }

    private BackupService Create(
        IReadOnlyList<IBackupTarget> targets,
        TimeProvider? time = null,
        int retention = 14,
        bool enabled = true,
        string masterKey = "a-master-key-that-differs-from-the-backup-key"
    ) =>
        new(
            new BackupOptions
            {
                Enabled = enabled,
                Key = Key,
                RetentionCount = retention,
                HealthcheckUrl = "https://hc-ping.com/backup",
                Local = new LocalTargetOptions { Enabled = true, Directory = "/unused" },
            },
            targets,
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Default"] = $"Data Source={_dbPath};Pooling=False",
                        ["Encryption:MasterKey"] = masterKey,
                    }
                )
                .Build(),
            new HealthcheckPinger(new HttpClient(_pings), NullLogger<HealthcheckPinger>.Instance),
            NullLogger<BackupService>.Instance,
            time ?? TimeProvider.System
        );

    [Fact]
    public async Task CreateBackup_EncryptsADecryptableSqliteSnapshot_AndPings()
    {
        var target = new MemoryTarget("local");
        using var service = Create([target]);

        var info = await service.CreateBackupAsync(CancellationToken.None);

        var plaintext = new BackupCipher(Key).Decrypt(target.Files[info.Name]);
        Assert.Equal("SQLite format 3\0"u8.ToArray(), plaintext[..16]);
        Assert.Equal(["local"], info.Targets);
        Assert.Equal(["https://hc-ping.com/backup"], _pings.Urls);
    }

    [Fact]
    public async Task CreateBackup_FailsWhenAnyTargetFails_AndPingsFail()
    {
        var good = new MemoryTarget("local");
        using var service = Create([good, new MemoryTarget("s3", failWrites: true)]);

        await Assert.ThrowsAsync<AggregateException>(() =>
            service.CreateBackupAsync(CancellationToken.None)
        );

        Assert.Single(good.Files); // the healthy target still got its copy
        Assert.Equal(["https://hc-ping.com/backup/fail"], _pings.Urls);
    }

    [Fact]
    public async Task Retention_And_Listing_AcrossTargets()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 19, 3, 0, 0, TimeSpan.Zero));
        var local = new MemoryTarget("local");
        var s3 = new MemoryTarget("s3");
        using var service = Create([local, s3], time, retention: 2);

        var names = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            names.Add((await service.CreateBackupAsync(CancellationToken.None)).Name);
            time.Advance(TimeSpan.FromHours(1));
        }
        local.Files.Remove(names[2]); // newest only in s3 now

        var list = await service.ListBackupsAsync(CancellationToken.None);

        Assert.Equal([names[2], names[1]], list.Select(b => b.Name));
        Assert.Equal(["s3"], list[0].Targets);
        Assert.Equal(["local", "s3"], list[1].Targets);
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 5, 0, 0, TimeSpan.Zero), list[0].CreatedAt);
    }

    [Fact]
    public async Task Retention_NeverDeletesTheBackupJustWritten_AfterClockStepsBack()
    {
        var time = new SettableClock
        {
            Now = new DateTimeOffset(2026, 11, 19, 3, 0, 0, TimeSpan.Zero),
        };
        var target = new MemoryTarget("local");
        using var service = Create([target], time, retention: 1);

        await service.CreateBackupAsync(CancellationToken.None);
        time.Now = time.Now.AddMinutes(-1); // NTP correction backwards
        var newest = await service.CreateBackupAsync(CancellationToken.None);

        Assert.Equal([newest.Name], target.Files.Keys);
    }

    [Fact]
    public async Task BackupKeyEqualToMasterKey_RefusesToCreateBackups()
    {
        using var service = Create([new MemoryTarget("local")], masterKey: Key);

        Assert.False(service.IsConfigured);
        await Assert.ThrowsAsync<BackupNotConfiguredException>(() =>
            service.CreateBackupAsync(CancellationToken.None)
        );
    }

    [Fact]
    public async Task NotConfigured_Throws()
    {
        using var service = Create([new MemoryTarget("local")], enabled: false);

        Assert.False(service.IsConfigured);
        await Assert.ThrowsAsync<BackupNotConfiguredException>(() =>
            service.CreateBackupAsync(CancellationToken.None)
        );
        await Assert.ThrowsAsync<BackupNotConfiguredException>(() =>
            service.ListBackupsAsync(CancellationToken.None)
        );
    }

    [Fact]
    public void CreateTargets_HonoursEnabledFlags()
    {
        var options = new BackupOptions
        {
            Local = new LocalTargetOptions { Enabled = true, Directory = "/tmp/x" },
            S3 = new S3TargetOptions
            {
                Enabled = true,
                ServiceUrl = "http://127.0.0.1:9",
                Region = "us-east-1",
                Bucket = "b",
                AccessKey = "a",
                SecretKey = "s",
            },
        };

        Assert.Equal(
            ["local", "s3"],
            BackupServiceExtensions.CreateTargets(options).Select(t => t.Name)
        );
        options.S3.Enabled = false;
        Assert.Equal(["local"], BackupServiceExtensions.CreateTargets(options).Select(t => t.Name));
    }

    public void Dispose() => File.Delete(_dbPath);
}

public sealed class BackupSchedulerTests
{
    private sealed class CountingBackupService(bool configured = true, bool fail = false)
        : IBackupService
    {
        public int Calls;
        public bool IsConfigured => configured;

        public Task<BackupInfo> CreateBackupAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            if (fail)
                throw new IOException("boom");
            return Task.FromResult(new BackupInfo("x", 1, DateTimeOffset.UtcNow, ["local"]));
        }

        public Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BackupInfo>>([]);
    }

    private static async Task AdvanceUntil(
        FakeTimeProvider time,
        TimeSpan step,
        Func<bool> condition
    )
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            time.Advance(step);
            for (var spin = 0; spin < 50 && !condition(); spin++)
                await Task.Delay(2);
        }
        Assert.True(condition());
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // a failing run must not stop the schedule
    public async Task RunsOnEachCronOccurrence(bool fail)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 19, 2, 59, 30, TimeSpan.Zero));
        var backups = new CountingBackupService(fail: fail);
        using var scheduler = new BackupScheduler(
            new BackupOptions { Schedule = "0 3 * * *" },
            backups,
            time,
            NullLogger<BackupScheduler>.Instance
        );

        await scheduler.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.Equal(0, backups.Calls);

        // Advance in small steps: the scheduler computes its next occurrence asynchronously, so a
        // single big jump can land before it starts waiting and skip that occurrence.
        await AdvanceUntil(time, TimeSpan.FromSeconds(10), () => backups.Calls == 1);
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 3, 0, 0, TimeSpan.Zero), time.GetUtcNow());

        await AdvanceUntil(time, TimeSpan.FromHours(1), () => backups.Calls == 2);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero), time.GetUtcNow());

        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WaitsBeyondTaskDelayLimit_ForYearlySchedules()
    {
        // 2 Jan → next 1 Jan is ~364 days away, far past Task.Delay's ~49.7-day maximum.
        var time = new FakeTimeProvider(new DateTimeOffset(2027, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var backups = new CountingBackupService();
        using var scheduler = new BackupScheduler(
            new BackupOptions { Schedule = "0 3 1 1 *" },
            backups,
            time,
            NullLogger<BackupScheduler>.Instance
        );

        await scheduler.StartAsync(CancellationToken.None);
        for (var day = 0; day < 363; day++)
        {
            await Task.Delay(1);
            time.Advance(BackupScheduler.MaxStep);
        }
        Assert.Equal(0, backups.Calls);
        Assert.Null(scheduler.ExecuteTask!.Exception);

        time.Advance(TimeSpan.FromDays(2));
        await WaitUntil(() => backups.Calls == 1);
        await scheduler.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(false, "0 3 * * *")]
    [InlineData(true, "")]
    public async Task DoesNothing_WhenNotConfiguredOrNoSchedule(bool configured, string schedule)
    {
        var time = new FakeTimeProvider();
        var backups = new CountingBackupService(configured);
        using var scheduler = new BackupScheduler(
            new BackupOptions { Schedule = schedule },
            backups,
            time,
            NullLogger<BackupScheduler>.Instance
        );

        await scheduler.StartAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromDays(2));
        await Task.Delay(50);
        await scheduler.StopAsync(CancellationToken.None);

        Assert.Equal(0, backups.Calls);
    }
}

/// <summary>S3 target against an <see cref="IAmazonS3"/> stand-in that keeps objects in memory.</summary>
public class S3BackupTargetTests
{
    public class FakeS3 : DispatchProxy
    {
        public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case nameof(IAmazonS3.PutObjectAsync):
                {
                    var request = (PutObjectRequest)args![0]!;
                    using var copy = new MemoryStream();
                    request.InputStream.CopyTo(copy);
                    Objects[request.Key] = copy.ToArray();
                    return Task.FromResult(new PutObjectResponse());
                }
                case nameof(IAmazonS3.ListObjectsV2Async):
                {
                    var request = (ListObjectsV2Request)args![0]!;
                    // Two pages, to exercise continuation.
                    var keys = Objects
                        .Keys.Where(k => k.StartsWith(request.Prefix))
                        .Order()
                        .ToList();
                    var page = request.ContinuationToken is null ? keys.Take(1) : keys.Skip(1);
                    return Task.FromResult(
                        new ListObjectsV2Response
                        {
                            S3Objects = page.Select(k => new S3Object
                                {
                                    Key = k,
                                    Size = Objects[k].Length,
                                })
                                .ToList(),
                            IsTruncated = request.ContinuationToken is null && keys.Count > 1,
                            NextContinuationToken = "page-2",
                        }
                    );
                }
                case nameof(IAmazonS3.GetObjectAsync):
                    return Task.FromResult(
                        new GetObjectResponse
                        {
                            ResponseStream = new MemoryStream(Objects[(string)args![1]!]),
                        }
                    );
                case nameof(IAmazonS3.DeleteObjectAsync):
                    Objects.Remove((string)args![1]!);
                    return Task.FromResult(new DeleteObjectResponse());
                case nameof(IDisposable.Dispose):
                    return null;
                default:
                    throw new NotSupportedException(method.Name);
            }
        }
    }

    private static (S3BackupTarget Target, FakeS3 Fake) Create(string? prefix)
    {
        var client = DispatchProxy.Create<IAmazonS3, FakeS3>();
        return (new S3BackupTarget(client, "bucket", prefix), (FakeS3)(object)client);
    }

    [Theory]
    [InlineData("vinhedo1", "vinhedo1/")]
    [InlineData("/vinhedo1/", "vinhedo1/")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public async Task Write_List_Read_Delete(string? prefix, string expectedPrefix)
    {
        var (target, fake) = Create(prefix);
        var first = BackupNaming.For(new DateTimeOffset(2026, 11, 19, 3, 0, 0, TimeSpan.Zero));
        var second = BackupNaming.For(new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero));

        await target.WriteAsync(first, [1, 2], CancellationToken.None);
        await target.WriteAsync(second, [3], CancellationToken.None);
        fake.Objects[expectedPrefix + "notes.txt"] = [9];

        Assert.Equal(expectedPrefix + first, fake.Objects.Keys.Order().First());
        var listed = await target.ListAsync(CancellationToken.None);
        Assert.Equal(
            [new BackupObject(first, 2), new BackupObject(second, 1)],
            listed.OrderBy(o => o.Name)
        );
        Assert.Equal([3], await target.ReadAsync(second, CancellationToken.None));

        await target.DeleteAsync(first, CancellationToken.None);
        Assert.DoesNotContain(expectedPrefix + first, fake.Objects.Keys);
        Assert.Equal("s3", target.Name);
        target.Dispose();
    }

    [Fact]
    public async Task RejectsNonBackupNames()
    {
        var (target, _) = Create("p/");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            target.WriteAsync("other.bin", [1], CancellationToken.None)
        );
    }
}
