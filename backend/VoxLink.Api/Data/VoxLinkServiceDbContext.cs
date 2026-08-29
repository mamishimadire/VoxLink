using Microsoft.EntityFrameworkCore;
using VoxLink.Api.Models;

namespace VoxLink.Api.Data;

/// <summary>
/// Same schema/model as VoxLinkDbContext, but connects as the database owner
/// role, which bypasses Row Level Security entirely. Reserved for the
/// handful of operations that are legitimately cross-tenant by nature:
///   - Auth (login/register/forgot-password): there is no company context
///     yet — establishing it is the whole point of these operations.
///   - The hourly invoice-generation background job: iterates every
///     company's subscriptions by design.
///   - The anonymous Twilio status webhook: Twilio has no session/company
///     context to give us; we look the call up by provider call ID.
///   - The manager-approval endpoints for revoke/invoice-generation requests
///     (ApprovalsController): a manager approving one of these is
///     legitimately acting across companies, but deliberately does NOT hold
///     the is_platform_admin claim (that would also hand them every other
///     PlatformController capability) — so RLS's usual platform-admin
///     bypass wouldn't apply to them even though the action is authorized.
/// Everything else must use the regular VoxLinkDbContext so tenant isolation
/// is enforced by the database itself, not just application code.
///
/// (This is a separate class rather than a subclass of VoxLinkDbContext:
/// EF Core requires each DbContext type registered in DI to take its own
/// DbContextOptions&lt;TSelf&gt; constructor, so the two share model
/// configuration via ConfigureModel instead of via inheritance.)
/// </summary>
public class VoxLinkServiceDbContext : DbContext, IVoxLinkDbContext
{
    public VoxLinkServiceDbContext(DbContextOptions<VoxLinkServiceDbContext> options) : base(options)
    {
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Call> Calls => Set<Call>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ServiceAgreement> ServiceAgreements => Set<ServiceAgreement>();
    public DbSet<PlanChangeRequest> PlanChangeRequests => Set<PlanChangeRequest>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<LicenseRevokeRequest> LicenseRevokeRequests => Set<LicenseRevokeRequest>();
    public DbSet<InvoiceGenerationRequest> InvoiceGenerationRequests => Set<InvoiceGenerationRequest>();
    public DbSet<LicenseChangeRequest> LicenseChangeRequests => Set<LicenseChangeRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => VoxLinkDbContext.ConfigureModel(modelBuilder);
}
