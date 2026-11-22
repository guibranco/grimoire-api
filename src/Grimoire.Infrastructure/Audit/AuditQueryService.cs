using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Audit;

public sealed class AuditQueryService(GrimoireDbContext db) : IAuditQueryService
{
    public async Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        var events = Filter(db.AuditEvents.AsNoTracking(), query);

        var total = await events.CountAsync(ct);
        var items = await events
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new AuditPage(items, query.Page, query.PageSize, total);
    }

    internal static IQueryable<AuditEvent> Filter(IQueryable<AuditEvent> events, AuditQuery q)
    {
        if (!string.IsNullOrWhiteSpace(q.Application))
            events = events.Where(e => e.ApplicationSlug == q.Application);
        if (!string.IsNullOrWhiteSpace(q.Actor))
            events = events.Where(e => e.Actor == q.Actor);
        if (!string.IsNullOrWhiteSpace(q.Environment))
            events = events.Where(e =>
                e.Environment == q.Environment
                || (
                    e.Environment != null
                    && ("," + e.Environment + ",").Contains("," + q.Environment + ",")
                )
            );
        if (!string.IsNullOrWhiteSpace(q.Target))
            events = events.Where(e => e.Target == q.Target);
        if (q.Outcome is { } outcome)
            events = events.Where(e => e.Outcome == outcome);
        if (q.From is { } from)
        {
            var utc = from.ToUniversalTime();
            events = events.Where(e => e.OccurredAt >= utc);
        }
        if (q.To is { } to)
        {
            var utc = to.ToUniversalTime();
            events = events.Where(e => e.OccurredAt <= utc);
        }
        if (!q.IncludeReads)
            events = events.Where(e => !e.Action.EndsWith(".read"));

        if (!string.IsNullOrWhiteSpace(q.Action))
        {
            if (q.Action.EndsWith('*'))
            {
                var prefix = q.Action[..^1];
                events = events.Where(e => e.Action.StartsWith(prefix));
            }
            else
            {
                events = events.Where(e => e.Action == q.Action);
            }
        }

        return events;
    }
}
