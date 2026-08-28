-- Tenant-isolation policies for voxlink_app (the role the backend now
-- connects as for normal request traffic). The owner role ('postgres') used
-- for background jobs and pre-auth flows bypasses RLS entirely and is
-- unaffected by anything here.
--
-- Session context is set per-request by the backend via set_config():
--   app.current_company_id — the caller's company (empty/unset if none)
--   app.is_platform_admin  — 'true' for platform admins, else unset/'false'
--
-- nullif(..., '')::uuid avoids a hard cast error when the GUC is unset or
-- empty — it becomes NULL instead, which just never matches company_id.

-- companies: a row is visible/writable if it IS the caller's own company,
-- or the caller is a platform admin.
create policy tenant_isolation on companies
    for all
    using (
        id = nullif(current_setting('app.current_company_id', true), '')::uuid
        or current_setting('app.is_platform_admin', true) = 'true'
    )
    with check (
        id = nullif(current_setting('app.current_company_id', true), '')::uuid
        or current_setting('app.is_platform_admin', true) = 'true'
    );

-- Every other tenant table follows the same company_id-based rule.
do $$
declare
    tbl text;
begin
    foreach tbl in array array[
        'users', 'departments', 'phone_numbers', 'contacts', 'calls',
        'subscriptions', 'invoices', 'payments', 'email_logs', 'audit_logs',
        'service_agreements'
    ]
    loop
        execute format($f$
            create policy tenant_isolation on %I
                for all
                using (
                    company_id = nullif(current_setting('app.current_company_id', true), '')::uuid
                    or current_setting('app.is_platform_admin', true) = 'true'
                )
                with check (
                    company_id = nullif(current_setting('app.current_company_id', true), '')::uuid
                    or current_setting('app.is_platform_admin', true) = 'true'
                );
        $f$, tbl);
    end loop;
end $$;

-- plans: readable by anyone (registration form needs this pre-login), but
-- only a platform admin can create/modify/delete a plan directly.
create policy plans_read_all on plans
    for select
    using (true);

create policy plans_modify_platform_admin on plans
    for all
    using (current_setting('app.is_platform_admin', true) = 'true')
    with check (current_setting('app.is_platform_admin', true) = 'true');

-- plan_change_requests: platform-admin-only end to end (proposing and
-- reviewing price changes is never a client-facing action).
create policy platform_admin_only on plan_change_requests
    for all
    using (current_setting('app.is_platform_admin', true) = 'true')
    with check (current_setting('app.is_platform_admin', true) = 'true');
