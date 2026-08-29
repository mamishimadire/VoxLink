using Microsoft.EntityFrameworkCore;
using VoxLink.Api.Models;

namespace VoxLink.Api.Data;

public class VoxLinkDbContext : DbContext, IVoxLinkDbContext
{
    public VoxLinkDbContext(DbContextOptions<VoxLinkDbContext> options) : base(options)
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

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ConfigureModel(modelBuilder);

    internal static void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>().ToTable("companies");
        modelBuilder.Entity<Department>().ToTable("departments");
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasIndex(u => u.Email).IsUnique();
        });
        modelBuilder.Entity<Plan>().ToTable("plans");
        modelBuilder.Entity<Subscription>().ToTable("subscriptions");
        modelBuilder.Entity<Call>().ToTable("calls");
        modelBuilder.Entity<Invoice>().ToTable("invoices");
        modelBuilder.Entity<Payment>().ToTable("payments");
        modelBuilder.Entity<ServiceAgreement>().ToTable("service_agreements");
        modelBuilder.Entity<PlanChangeRequest>().ToTable("plan_change_requests");
        modelBuilder.Entity<Contact>().ToTable("contacts");
        modelBuilder.Entity<AuditLog>().ToTable("audit_logs");
        modelBuilder.Entity<LicenseRevokeRequest>().ToTable("license_revoke_requests");
        modelBuilder.Entity<InvoiceGenerationRequest>().ToTable("invoice_generation_requests");
    }
}
