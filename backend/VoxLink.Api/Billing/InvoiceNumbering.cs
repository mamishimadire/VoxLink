using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace VoxLink.Api.Billing;

/// <summary>
/// Human-readable, searchable invoice numbers backed by a database sequence
/// (invoice_number_seq), so the hourly background job and a manual
/// "generate now" click can never hand out the same number.
/// </summary>
public static class InvoiceNumbering
{
    public static async Task<string> NextAsync(DatabaseFacade database, DateTimeOffset issuedAt, CancellationToken cancellationToken)
    {
        // EF Core's SqlQuery<T> wraps the raw SQL as `select s."Value" from (...) as s`,
        // so the inner query's column must be named exactly "Value" (quoted, case-sensitive)
        // or it fails with "column s.Value does not exist" — nextval()'s own column name
        // (`nextval`) doesn't match unless explicitly aliased.
        var seq = await database.SqlQuery<long>($"select nextval('invoice_number_seq') as \"Value\"").SingleAsync(cancellationToken);
        return $"INV-{issuedAt:yyyy}-{seq:D5}";
    }
}
