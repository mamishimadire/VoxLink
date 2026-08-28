-- VoxLink V1 schema
-- Run in Supabase SQL Editor (or via psql) against the VoxLink project.
-- Multi-tenant model: every business table carries company_id.
-- Auth/authorization is enforced by the FastAPI backend (service-role DB access),
-- not by Supabase Auth/RLS, so RLS is intentionally omitted here — do not expose
-- this database directly to untrusted clients without adding it later.

create extension if not exists pgcrypto;

create or replace function set_updated_at()
returns trigger as $$
begin
  new.updated_at = now();
  return new;
end;
$$ language plpgsql;

-- companies -----------------------------------------------------------

create table companies (
  id uuid primary key default gen_random_uuid(),
  name text not null,
  registration_number text,
  email text,
  phone text,
  status text not null default 'active' check (status in ('active', 'suspended', 'cancelled')),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create trigger trg_companies_updated_at
  before update on companies
  for each row execute function set_updated_at();

-- departments -----------------------------------------------------------

create table departments (
  id uuid primary key default gen_random_uuid(),
  company_id uuid not null references companies(id) on delete cascade,
  name text not null,
  status text not null default 'active' check (status in ('active', 'inactive')),
  created_at timestamptz not null default now()
);

create index idx_departments_company_id on departments(company_id);

-- users -----------------------------------------------------------

create table users (
  id uuid primary key default gen_random_uuid(),
  company_id uuid not null references companies(id) on delete cascade,
  department_id uuid references departments(id) on delete set null,
  first_name text not null,
  last_name text not null,
  email text not null unique,
  password_hash text not null,
  role text not null default 'employee' check (role in ('owner', 'admin', 'manager', 'employee')),
  status text not null default 'active' check (status in ('active', 'suspended', 'invited')),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create index idx_users_company_id on users(company_id);
create index idx_users_department_id on users(department_id);

create trigger trg_users_updated_at
  before update on users
  for each row execute function set_updated_at();

-- phone_numbers -----------------------------------------------------------

create table phone_numbers (
  id uuid primary key default gen_random_uuid(),
  company_id uuid not null references companies(id) on delete cascade,
  department_id uuid references departments(id) on delete set null,
  phone_number text not null unique,
  telnyx_number_id text,
  caller_id_name text,
  status text not null default 'active' check (status in ('active', 'inactive', 'porting')),
  created_at timestamptz not null default now()
);

create index idx_phone_numbers_company_id on phone_numbers(company_id);

-- contacts -----------------------------------------------------------

create table contacts (
  id uuid primary key default gen_random_uuid(),
  company_id uuid not null references companies(id) on delete cascade,
  first_name text,
  last_name text,
  company_name text,
  phone_number text not null,
  email text,
  notes text,
  created_at timestamptz not null default now()
);

create index idx_contacts_company_id on contacts(company_id);

-- calls -----------------------------------------------------------

create table calls (
  id uuid primary key default gen_random_uuid(),
  company_id uuid not null references companies(id) on delete cascade,
  user_id uuid references users(id) on delete set null,
  phone_number_id uuid references phone_numbers(id) on delete set null,
  destination_number text not null,
  direction text not null check (direction in ('inbound', 'outbound')),
  status text not null default 'initiated'
    check (status in ('initiated', 'ringing', 'answered', 'completed', 'failed', 'no_answer', 'busy')),
  started_at timestamptz,
  answered_at timestamptz,
  ended_at timestamptz,
  duration_seconds integer not null default 0,
  telnyx_call_id text,
  carrier_cost numeric(10, 4) not null default 0,
  customer_charge numeric(10, 4) not null default 0,
  created_at timestamptz not null default now()
);

create index idx_calls_company_id on calls(company_id);
create index idx_calls_user_id on calls(user_id);
create index idx_calls_telnyx_call_id on calls(telnyx_call_id);
create index idx_calls_started_at on calls(started_at);

-- plans -----------------------------------------------------------

create table plans (
  id uuid primary key default gen_random_uuid(),
  name text not null,
  description text,
  monthly_price numeric(10, 2) not null default 0,
  included_minutes integer not null default 0,
  per_minute_rate numeric(10, 4) not null default 0,
  status text not null default 'active' check (status in ('active', 'retired')),
  created_at timestamptz not null default now()
);

-- subscriptions -----------------------------------------------------------

create table subscriptions (
  id uuid primary key default gen_random_uuid(),
  company_id uuid not null references companies(id) on delete cascade,
  plan_id uuid not null references plans(id),
  status text not null default 'active' check (status in ('active', 'past_due', 'cancelled')),
  current_period_start timestamptz not null default now(),
  current_period_end timestamptz not null,
  created_at timestamptz not null default now()
);

create index idx_subscriptions_company_id on subscriptions(company_id);

-- invoices -----------------------------------------------------------

create table invoices (
  id uuid primary key default gen_random_uuid(),
  company_id uuid not null references companies(id) on delete cascade,
  subscription_id uuid references subscriptions(id) on delete set null,
  amount_due numeric(10, 2) not null,
  amount_paid numeric(10, 2) not null default 0,
  status text not null default 'pending' check (status in ('pending', 'paid', 'overdue', 'void')),
  due_date date,
  issued_at timestamptz not null default now(),
  paid_at timestamptz
);

create index idx_invoices_company_id on invoices(company_id);

-- payments -----------------------------------------------------------

create table payments (
  id uuid primary key default gen_random_uuid(),
  company_id uuid not null references companies(id) on delete cascade,
  invoice_id uuid references invoices(id) on delete set null,
  amount numeric(10, 2) not null,
  method text,
  status text not null default 'pending' check (status in ('pending', 'succeeded', 'failed', 'refunded')),
  provider_reference text,
  created_at timestamptz not null default now()
);

create index idx_payments_company_id on payments(company_id);

-- email_logs -----------------------------------------------------------

create table email_logs (
  id uuid primary key default gen_random_uuid(),
  company_id uuid references companies(id) on delete set null,
  user_id uuid references users(id) on delete set null,
  email text not null,
  email_type text not null,
  status text not null default 'queued' check (status in ('queued', 'sent', 'failed')),
  provider_message_id text,
  sent_at timestamptz
);

create index idx_email_logs_company_id on email_logs(company_id);

-- audit_logs -----------------------------------------------------------

create table audit_logs (
  id uuid primary key default gen_random_uuid(),
  company_id uuid references companies(id) on delete set null,
  user_id uuid references users(id) on delete set null,
  action text not null,
  entity_type text,
  entity_id uuid,
  ip_address inet,
  created_at timestamptz not null default now()
);

create index idx_audit_logs_company_id on audit_logs(company_id);
