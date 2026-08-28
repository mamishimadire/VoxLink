-- The maker-checker price-change proposal form let an admin change every
-- other plan attribute (price, rates, user tiers) but not included minutes,
-- so there was no way to raise or lower a tier's included-minutes pool
-- without editing the plans table directly.
alter table plan_change_requests add column new_included_minutes integer not null default 0;
