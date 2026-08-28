-- Adds a distinct "rejected" outcome (never approved, with a reason) separate
-- from "suspended" (was active, later cut off — e.g. for non-payment).

alter table companies drop constraint companies_status_check;
alter table companies add constraint companies_status_check
    check (status in ('pending', 'active', 'suspended', 'rejected', 'cancelled'));

alter table companies add column rejected_reason text;
