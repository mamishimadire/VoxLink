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
        var seq = await database.SqlQuery<long>($"select nextval('invoice_number_seq')").SingleAsync(cancellationToken);
        return $"INV-{issuedAt:yyyy}-{seq:D5}";
    }
}
