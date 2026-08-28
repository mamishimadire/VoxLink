-- Password/account security: lockout after repeated failed logins.
alter table users add column failed_login_attempts integer not null default 0;
alter table users add column locked_until timestamptz;

-- Segregation of duties: a business_owner is a distinct capability from
-- is_platform_admin, so a VoxLink admin can be added later who can propose
-- price changes but not approve their own.
alter table users add column is_business_owner boolean not null default false;

-- Maker-checker workflow for plan/price changes: a platform admin proposes,
-- a business owner approves before the live `plans` row is touched.
create table plan_change_requests (
    id uuid primary key default gen_random_uuid(),
    plan_id uuid not null references plans(id),
    proposed_by uuid not null references users(id),
    proposed_at timestamptz not null default now(),
    new_name text not null,
    new_description text,
    new_monthly_price numeric(10,2) not null,
    new_min_users integer not null,
    new_max_users integer,
    new_is_custom_quote boolean not null default false,
    status text not null default 'pending' check (status in ('pending', 'approved', 'rejected')),
    reviewed_by uuid references users(id),
    reviewed_at timestamptz,
    review_note text
);

create index idx_plan_change_requests_status on plan_change_requests(status);
