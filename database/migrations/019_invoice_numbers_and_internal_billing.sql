-- Two things needed for the new Invoices tab (search/filter by invoice
-- number, monthly auto-generation) and for treating VoxLink's own internal
-- team as a billable "customer" of its own calling costs, same as any
-- client company:
--
-- 1. A human-readable, searchable invoice_number (INV-<year>-<seq>) backed
--    by a database sequence, so numbers stay unique even when the hourly
--    background job and a manual "generate now" both fire around the same
--    time.
-- 2. A cost-only plan + subscription for the internal company, so the
--    existing per-subscription usage/invoice pipeline just works for it
--    too — no special-casing required anywhere else in the app.

create sequence invoice_number_seq;

-- Every prior migration only ever needed table privileges (ids are
-- gen_random_uuid(), not serial) — this is the first real sequence, so the
-- low-privilege app role (migration 011) needs USAGE explicitly or
-- nextval() from a tenant-scoped request (e.g. the sign-up invoice path)
-- fails with permission denied.
grant usage, select on sequence invoice_number_seq to voxlink_app;

alter table invoices add column invoice_number text;

with numbered as (
    select id, row_number() over (order by issued_at) as rn
    from invoices
)
update invoices i
set invoice_number = 'INV-' || to_char(i.issued_at, 'YYYY') || '-' || lpad(numbered.rn::text, 5, '0')
from numbered
where numbered.id = i.id;

select setval('invoice_number_seq', greatest((select count(*) from invoices), 1), (select count(*) from invoices) > 0);

alter table invoices alter column invoice_number set not null;
alter table invoices add constraint invoices_invoice_number_key unique (invoice_number);

-- Marks the usage-period boundary an invoice actually covers (null for
-- signup invoices, which aren't period-based). Lets the auto-generator tell
-- "already billed through this exact month-end" apart from "there's an
-- earlier ad-hoc invoice that only covers part of the period" — without
-- this, a manually-generated invoice mid-period would make the month-end
-- job skip billing the remaining days.
alter table invoices add column period_end timestamptz;

-- "internal" is a third plan status alongside active/retired: never shown
-- in the public/signup plan lists (which filter on status = 'active'), only
-- ever attached to VoxLink's own subscription.
alter table plans drop constraint plans_status_check;
alter table plans add constraint plans_status_check
    check (status in ('active', 'retired', 'internal'));

insert into plans (
    id, name, description, monthly_price, included_minutes, per_minute_rate,
    min_users, max_users, is_custom_quote, status, local_rate_per_min, international_rate_per_min
)
select
    gen_random_uuid(), 'Internal Usage',
    'VoxLink''s own call cost tracking — no monthly platform fee, billed at cost per minute so internal usage shows up the same way a client''s does.',
    0, 0, 0, 0, null, false, 'internal', 1.50, 3.00
where not exists (select 1 from plans where name = 'Internal Usage');

insert into subscriptions (id, company_id, plan_id, status, current_period_start, current_period_end, created_at)
select
    gen_random_uuid(), c.id, p.id, 'active',
    date_trunc('month', now()), date_trunc('month', now()) + interval '1 month', now()
from companies c, plans p
where c.name = 'VoxLink' and c.is_internal = true and p.name = 'Internal Usage'
and not exists (select 1 from subscriptions s where s.company_id = c.id);
