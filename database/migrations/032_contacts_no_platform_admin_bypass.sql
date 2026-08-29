-- The restrictive per-user policy added in 031 still let a platform admin
-- bypass it (mirroring the company-level tenant_isolation policy's bypass,
-- copied out of habit) — meaning any VoxLink staff member could still see
-- every client's own contacts in their own dialer, the exact leak just
-- reported. A personal contact list has no legitimate cross-tenant admin
-- use case (unlike billing/companies/invoices, which platform admins
-- genuinely need to see across every client) — so this policy narrows to
-- "your own contacts only" with no exception for anyone, platform admin
-- included.
drop policy contacts_user_isolation on contacts;

create policy contacts_user_isolation on contacts
    as restrictive
    for all
    using (user_id = nullif(current_setting('app.current_user_id', true), '')::uuid)
    with check (user_id = nullif(current_setting('app.current_user_id', true), '')::uuid);
