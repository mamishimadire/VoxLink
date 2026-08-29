-- Segregation of duties for two more high-stakes platform actions:
--
-- Revoking a client's license: neither an admin nor an owner may do it
-- unilaterally. An admin's proposal needs an owner's approval; an owner's
-- proposal needs a manager's approval — never the same person, and never
-- the same pair of roles reviewing themselves.
create table license_revoke_requests (
    id uuid primary key default gen_random_uuid(),
    company_id uuid not null references companies(id) on delete cascade,
    proposed_by uuid not null references users(id),
    proposed_by_role text not null,
    reason text,
    proposed_at timestamptz not null default now(),
    status text not null default 'pending' check (status in ('pending', 'approved', 'rejected')),
    reviewed_by uuid references users(id),
    reviewed_at timestamptz,
    review_note text
);

-- A manually-generated invoice (outside the automatic monthly cycle) now
-- always needs a manager's approval before it's actually created.
create table invoice_generation_requests (
    id uuid primary key default gen_random_uuid(),
    company_id uuid not null references companies(id) on delete cascade,
    proposed_by uuid not null references users(id),
    proposed_at timestamptz not null default now(),
    status text not null default 'pending' check (status in ('pending', 'approved', 'rejected')),
    reviewed_by uuid references users(id),
    reviewed_at timestamptz,
    review_note text,
    generated_invoice_id uuid references invoices(id)
);

create index idx_license_revoke_requests_company_id on license_revoke_requests(company_id);
create index idx_invoice_generation_requests_company_id on invoice_generation_requests(company_id);

alter table license_revoke_requests enable row level security;
alter table invoice_generation_requests enable row level security;

-- Same tenant-isolation shape as every other company-scoped table: visible
-- to the owning company or a platform admin. The manager-approval endpoints
-- deliberately use the RLS-bypassing service context instead (see
-- VoxLinkServiceDbContext's doc comment) since a manager doesn't carry the
-- platform-admin claim these policies check for.
create policy tenant_isolation on license_revoke_requests
    for all
    using (
        company_id = nullif(current_setting('app.current_company_id', true), '')::uuid
        or current_setting('app.is_platform_admin', true) = 'true'
    )
    with check (
        company_id = nullif(current_setting('app.current_company_id', true), '')::uuid
        or current_setting('app.is_platform_admin', true) = 'true'
    );

create policy tenant_isolation on invoice_generation_requests
    for all
    using (
        company_id = nullif(current_setting('app.current_company_id', true), '')::uuid
        or current_setting('app.is_platform_admin', true) = 'true'
    )
    with check (
        company_id = nullif(current_setting('app.current_company_id', true), '')::uuid
        or current_setting('app.is_platform_admin', true) = 'true'
    );

grant select, insert, update, delete on license_revoke_requests to voxlink_app;
grant select, insert, update, delete on invoice_generation_requests to voxlink_app;
