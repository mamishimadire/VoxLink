-- Same segregation-of-duties shape as license_revoke_requests: setting or
-- changing a client's license (plan + expiry) directly changes what they're
-- billed for and how much service they get, so it goes through the same
-- propose-then-review workflow — an admin's proposal needs an owner's
-- approval, an owner's proposal needs a manager's approval.
create table license_change_requests (
    id uuid primary key default gen_random_uuid(),
    company_id uuid not null references companies(id) on delete cascade,
    proposed_by uuid not null references users(id),
    proposed_by_role text not null,
    plan_id uuid not null references plans(id),
    expires_at timestamptz not null,
    proposed_at timestamptz not null default now(),
    status text not null default 'pending' check (status in ('pending', 'approved', 'rejected')),
    reviewed_by uuid references users(id),
    reviewed_at timestamptz,
    review_note text
);

create index idx_license_change_requests_company_id on license_change_requests(company_id);

alter table license_change_requests enable row level security;

create policy tenant_isolation on license_change_requests
    for all
    using (
        company_id = nullif(current_setting('app.current_company_id', true), '')::uuid
        or current_setting('app.is_platform_admin', true) = 'true'
    )
    with check (
        company_id = nullif(current_setting('app.current_company_id', true), '')::uuid
        or current_setting('app.is_platform_admin', true) = 'true'
    );

grant select, insert, update, delete on license_change_requests to voxlink_app;
