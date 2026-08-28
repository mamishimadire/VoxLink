-- Adds platform-admin capability (a user who manages all client companies,
-- independent of the company they themselves belong to) and the contact
-- fields captured during client onboarding (Primary / Billing / Administrative).

alter table users add column is_platform_admin boolean not null default false;

alter table companies add column primary_contact_name text;
alter table companies add column primary_contact_email text;
alter table companies add column billing_contact_name text;
alter table companies add column billing_contact_email text;
alter table companies add column admin_contact_name text;
alter table companies add column admin_contact_email text;
