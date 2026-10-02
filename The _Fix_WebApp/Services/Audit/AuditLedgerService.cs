using FashionFix.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FashionFix.Web.Services.Audit;

public record LedgerIssue(int AuditLogId, string Problem);

public record LedgerVerification(
    int SealedCount,
    int LegacyCount,
    int ValidCount,
    string? HeadHash,
    List<LedgerIssue> Issues,
    DateTime VerifiedAtUtc)
{
    public bool IsValid => Issues.Count == 0;
}

/// <summary>Recomputes the hash chain over every sealed audit row and reports any row that was edited or any gap where a row was removed.</summary>
public class AuditLedgerService
{
    private readonly ApplicationDbContext _context;

    public AuditLedgerService(ApplicationDbContext context) => _context = context;

    public async Task<LedgerVerification> VerifyAsync(CancellationToken ct = default)
    {
        var legacy = await _context.AuditLogs.CountAsync(a => a.Hash == null, ct);

        var issues = new List<LedgerIssue>();
        var sealedCount = 0;
        var valid = 0;
        string? expectedPrev = null;
        string? head = null;

        // Streamed, minimal columns: cheap even with tens of thousands of rows.
        var rows = _context.AuditLogs.AsNoTracking()
            .Where(a => a.Hash != null)
            .OrderBy(a => a.AuditLogId)
            .AsAsyncEnumerable();

        await foreach (var row in rows.WithCancellation(ct))
        {
            sealedCount++;
            var rowOk = true;

            if (row.PreviousHash != expectedPrev)
            {
                if (issues.Count < 20) issues.Add(new LedgerIssue(row.AuditLogId, "Chain link broken - an earlier entry was removed or altered."));
                rowOk = false;
            }

            if (AuditHasher.Compute(row, row.PreviousHash) != row.Hash)
            {
                if (issues.Count < 20) issues.Add(new LedgerIssue(row.AuditLogId, "Entry contents no longer match its stored hash."));
                rowOk = false;
            }

            if (rowOk) valid++;
            // Continue from the row's stored hash so a single edit is reported once, not on every later row.
            expectedPrev = row.Hash;
            head = row.Hash;
        }

        return new LedgerVerification(sealedCount, legacy, valid, head, issues, DateTime.UtcNow);
    }
}
