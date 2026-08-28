using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VoxLink.Api.Auth;
using VoxLink.Api.Billing;
using VoxLink.Api.Data;
using VoxLink.Api.Email;
using VoxLink.Api.Pdf;
using VoxLink.Api.Services;
using VoxLink.Api.Storage;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.Configure<TwilioOptions>(builder.Configuration.GetSection(TwilioOptions.SectionName));
builder.Services.Configure<BackendOptions>(builder.Configuration.GetSection(BackendOptions.SectionName));
builder.Services.Configure<TwilioVoiceOptions>(builder.Configuration.GetSection(TwilioVoiceOptions.SectionName));
builder.Services.AddHttpClient<ITwilioClient, TwilioClient>();
builder.Services.AddScoped<VoiceTokenService>();

builder.Services.Configure<ResendOptions>(builder.Configuration.GetSection(ResendOptions.SectionName));
builder.Services.Configure<SendGridOptions>(builder.Configuration.GetSection(SendGridOptions.SectionName));
builder.Services.Configure<FrontendOptions>(builder.Configuration.GetSection(FrontendOptions.SectionName));
builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<TenantContextInterceptor>();

// Tenant-scoped: connects as the low-privilege 'voxlink_app' role, which
// does NOT bypass Row Level Security. Every request is stamped with the
// caller's company/platform-admin status via TenantContextInterceptor, and
// RLS policies on every table enforce isolation at the database level —
// a second, independent layer under the app-level company_id checks.
builder.Services.AddDbContext<VoxLinkDbContext>((sp, options) =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"),
            npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null))
        .UseSnakeCaseNamingConvention()
        .AddInterceptors(sp.GetRequiredService<TenantContextInterceptor>()));

// Elevated: connects as the table-owner role, which bypasses RLS entirely.
// Reserved for auth (pre-tenant-context by nature) and cross-tenant
// background/webhook work — see VoxLinkServiceDbContext's doc comment.
builder.Services.AddDbContext<VoxLinkServiceDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Service"),
            npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null))
        .UseSnakeCaseNamingConvention());

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<PasswordResetService>();

builder.Services.Configure<SupabaseStorageOptions>(builder.Configuration.GetSection(SupabaseStorageOptions.SectionName));
builder.Services.AddHttpClient<SupabaseStorageClient>();

builder.Services.Configure<BillingOptions>(builder.Configuration.GetSection(BillingOptions.SectionName));
builder.Services.AddScoped<InvoiceGenerationService>();
builder.Services.AddScoped<SignupInvoiceService>();
builder.Services.AddHostedService<InvoiceGenerationBackgroundService>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, the handler remaps the short "sub" claim to the long
        // ClaimTypes.NameIdentifier URI, breaking GetUserId()'s FindFirst("sub").
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PlatformAdmin", policy =>
        policy.RequireClaim("is_platform_admin", "true"));
    options.AddPolicy("BusinessOwner", policy =>
        policy.RequireClaim("is_business_owner", "true"));
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
