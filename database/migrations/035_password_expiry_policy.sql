-- Drives the 30-day password expiry policy: when a password was last
-- actually set (register, reset link, or self-service change), not just
-- when the account row was last touched. Backfilled to created_at for
-- existing accounts since there's no earlier record of this.
alter table users add column password_changed_at timestamptz;
update users set password_changed_at = created_at where password_changed_at is null;
alter table users alter column password_changed_at set not null;
alter table users alter column password_changed_at set default now();
