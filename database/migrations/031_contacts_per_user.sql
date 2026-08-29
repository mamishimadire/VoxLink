-- Contacts were only ever scoped by company, so every teammate in the same
-- company shared one contact list — saving or favoriting a number showed up
-- on every other user's screen. Contacts must be personal to whoever saved
-- them, same as call history already is.
alter table contacts add column user_id uuid references users(id) on delete cascade;

-- Backfill: attribute existing (pre-fix) contacts to the company's earliest
-- user (its owner, or whoever else was created first if no owner exists) so
-- nothing is silently orphaned or deleted. Anyone who actually needs one of
-- these going forward can re-add it under their own account.
update contacts
set user_id = (
    select u.id from users u
    where u.company_id = contacts.company_id and u.role = 'owner'
    order by u.created_at asc
    limit 1
)
where user_id is null;

update contacts
set user_id = (
    select u.id from users u
    where u.company_id = contacts.company_id
    order by u.created_at asc
    limit 1
)
where user_id is null;

-- Any contact whose company no longer has any user at all (shouldn't happen,
-- but leaves nothing half-migrated) is removed rather than left without an
-- owner.
delete from contacts where user_id is null;

alter table contacts alter column user_id set not null;
create index idx_contacts_user_id on contacts(user_id);

-- Defense in depth: even if a future endpoint forgets to filter by user_id
-- in application code, the database itself still won't return another
-- user's contact. RESTRICTIVE (not the default PERMISSIVE) so it narrows
-- the existing company-level tenant_isolation policy with an AND, rather
-- than widening access with an OR the way a second permissive policy would.
create policy contacts_user_isolation on contacts
    as restrictive
    for all
    using (
        user_id = nullif(current_setting('app.current_user_id', true), '')::uuid
        or current_setting('app.is_platform_admin', true) = 'true'
    )
    with check (
        user_id = nullif(current_setting('app.current_user_id', true), '')::uuid
        or current_setting('app.is_platform_admin', true) = 'true'
    );
