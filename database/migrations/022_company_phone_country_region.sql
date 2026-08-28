-- Onboarding (both self-signup and platform-admin-created) now collects a
-- contact phone number and location for the client company — the phone
-- number is for VoxLink staff to reach client personnel directly, separate
-- from the softphone/calling system itself. companies.phone already existed
-- but was never actually collected or used anywhere.
alter table companies add column country text;
alter table companies add column region text;
