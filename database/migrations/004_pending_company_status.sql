-- Client companies now start "pending" until a platform admin approves them;
-- only on approval does the admin-contact invite email actually go out.

alter table companies drop constraint companies_status_check;
alter table companies add constraint companies_status_check
    check (status in ('pending', 'active', 'suspended', 'cancelled'));
