using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VoxLink.Api.Auth;

namespace VoxLink.Api.Data;

/// <summary>
/// Stamps every connection this DbContext opens with the current request's
/// tenant (company_id) and platform-admin status as Postgres session
/// variables, which the RLS policies on every table read via
/// current_setting('app.current_company_id'/'app.is_platform_admin').
///
/// Runs on every Open() call, not just the first — including when a logical
/// connection reuses a pooled physical one — so a connection can never carry
/// a stale tenant context left over from a previous request.
/// </summary>
public class TenantContextInterceptor : DbConnectionInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantContextInterceptor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await SetSessionContextAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetSessionContextAsync(connection, CancellationToken.None).GetAwaiter().GetResult();
        base.ConnectionOpened(connection, eventData);
    }

    private async Task SetSessionContextAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var companyId = user?.FindFirst("company_id")?.Value ?? "";
        var isPlatformAdmin = user?.IsPlatformAdmin() ?? false;

        var command = connection.CreateCommand();
        await using (command)
        {
            command.CommandText = "select set_config('app.current_company_id', @company_id, false), set_config('app.is_platform_admin', @is_admin, false)";

            var companyIdParam = command.CreateParameter();
            companyIdParam.ParameterName = "company_id";
            companyIdParam.Value = companyId;
            command.Parameters.Add(companyIdParam);

            var isAdminParam = command.CreateParameter();
            isAdminParam.ParameterName = "is_admin";
            isAdminParam.Value = isPlatformAdmin ? "true" : "false";
            command.Parameters.Add(isAdminParam);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
