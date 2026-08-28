using Microsoft.EntityFrameworkCore;
using VoxLink.Api.Models;

namespace VoxLink.Api.Data;

/// <summary>
/// The subset of the model that services shared between the tenant-scoped
/// and service DbContexts need typed access to (beyond plain SaveChangesAsync,
/// which both already get from DbContext directly). Lets a service like
/// SignupInvoiceService accept "whichever context the caller holds" without
/// caring which one it is.
/// </summary>
public interface IVoxLinkDbContext
{
    DbSet<Invoice> Invoices { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
