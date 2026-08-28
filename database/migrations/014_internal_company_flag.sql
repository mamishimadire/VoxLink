-- Distinguishes VoxLink's own operating company from real clients, so it
-- never shows up in the platform admin's client list — VoxLink is the
-- service provider, not its own customer.
alter table companies add column is_internal boolean not null default false;
update companies set is_internal = true where name = 'VoxLink';
