using Grimoire.Api.AccessTokens;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Grimoire.Tests;

public class IssuedTokenCleanupTests
{
    private sealed class Tokens(bool fail = false) : IAccessTokenRepository
    {
        public List<DateTimeOffset> Cleanups { get; } = [];

        public Task<int> DeleteExpiredIssuedAsync(DateTimeOffset before, CancellationToken ct)
        {
            Cleanups.Add(before);
            return fail ? throw new InvalidOperationException("locked") : Task.FromResult(0);
        }

        public Task<List<AccessToken>> ListAsync(Guid applicationId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AccessToken?> GetAsync(Guid applicationId, Guid id, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<List<AccessToken>> FindByPrefixAsync(string prefix, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> NameExistsAsync(Guid applicationId, string name, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task AddAsync(AccessToken token, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task UpdateAsync(AccessToken token, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task TouchAsync(Guid id, DateTimeOffset usedAt, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<int> RevokeIssuedAsync(
            Guid trustPolicyId,
            DateTimeOffset at,
            CancellationToken ct
        ) => throw new NotSupportedException();
    }

    private static readonly DateTimeOffset Start = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunsAtMostOncePerInterval()
    {
        var time = new FakeTimeProvider(Start);
        var cleanup = new IssuedTokenCleanup(time, NullLogger<IssuedTokenCleanup>.Instance);
        var tokens = new Tokens();

        await cleanup.RunAsync(tokens, default);
        await cleanup.RunAsync(tokens, default);
        time.Advance(IssuedTokenCleanup.Interval);
        await cleanup.RunAsync(tokens, default);

        Assert.Equal(
            [
                Start - IssuedTokenCleanup.Retention,
                Start + IssuedTokenCleanup.Interval - IssuedTokenCleanup.Retention,
            ],
            tokens.Cleanups
        );
    }

    [Fact]
    public async Task AFailedCleanup_DoesNotThrow()
    {
        var cleanup = new IssuedTokenCleanup(
            new FakeTimeProvider(Start),
            NullLogger<IssuedTokenCleanup>.Instance
        );
        var tokens = new Tokens(fail: true);
        var error = await Record.ExceptionAsync(() => cleanup.RunAsync(tokens, default));
        Assert.Null(error);
        Assert.Single(tokens.Cleanups);
    }
}
