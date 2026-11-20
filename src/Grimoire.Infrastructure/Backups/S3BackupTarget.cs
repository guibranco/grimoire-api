using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Grimoire.Infrastructure.Backups;

/// <summary>S3-compatible target (OCI Object Storage, AWS S3, MinIO, R2…).</summary>
public sealed class S3BackupTarget : IBackupTarget, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly string _prefix;

    public S3BackupTarget(S3TargetOptions options)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = options.ServiceUrl,
            ForcePathStyle = options.ForcePathStyle,
            // Newer SDKs add CRC checksums to every request by default; several S3-compatible
            // stores (OCI Object Storage included) reject them. Only send them when required.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        if (!string.IsNullOrWhiteSpace(options.Region))
            config.AuthenticationRegion = options.Region;

        _client = new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            config
        );
        _bucket = options.Bucket!;
        _prefix = NormalizePrefix(options.Prefix);
    }

    internal S3BackupTarget(IAmazonS3 client, string bucket, string? prefix)
    {
        _client = client;
        _bucket = bucket;
        _prefix = NormalizePrefix(prefix);
    }

    public string Name => "s3";

    public async Task WriteAsync(string name, byte[] content, CancellationToken ct)
    {
        using var body = new MemoryStream(content, writable: false);
        await _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _bucket,
                Key = KeyFor(name),
                InputStream = body,
                ContentType = "application/octet-stream",
                AutoCloseStream = false,
            },
            ct
        );
    }

    public async Task<IReadOnlyList<BackupObject>> ListAsync(CancellationToken ct)
    {
        var result = new List<BackupObject>();
        var request = new ListObjectsV2Request { BucketName = _bucket, Prefix = _prefix };
        ListObjectsV2Response response;
        do
        {
            response = await _client.ListObjectsV2Async(request, ct);
            foreach (var obj in response.S3Objects ?? [])
            {
                var name = obj.Key[_prefix.Length..];
                if (BackupNaming.IsBackupName(name))
                    result.Add(new BackupObject(name, obj.Size ?? 0));
            }
            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);

        return result;
    }

    public async Task<byte[]> ReadAsync(string name, CancellationToken ct)
    {
        using var response = await _client.GetObjectAsync(_bucket, KeyFor(name), ct);
        using var buffer = new MemoryStream();
        await response.ResponseStream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    public Task DeleteAsync(string name, CancellationToken ct) =>
        _client.DeleteObjectAsync(_bucket, KeyFor(name), ct);

    public void Dispose() => _client.Dispose();

    private string KeyFor(string name)
    {
        if (!BackupNaming.IsBackupName(name))
            throw new ArgumentException($"'{name}' is not a backup name.", nameof(name));
        return _prefix + name;
    }

    private static string NormalizePrefix(string? prefix)
    {
        prefix = (prefix ?? string.Empty).Trim().TrimStart('/');
        return prefix.Length == 0 || prefix.EndsWith('/') ? prefix : prefix + '/';
    }
}
