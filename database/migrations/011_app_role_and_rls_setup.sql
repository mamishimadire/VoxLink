-- A non-owner role for the backend to connect as, so Row Level Security
-- policies actually apply to it. The existing 'postgres' role (table owner)
-- bypasses RLS by default no matter what policies exist — this role does not.

create role voxlink_app with login password 'ImEvp2t8aRx0WElD1oDRcv2EgqtOYqW' nobypassrls;

grant usage on schema public to voxlink_app;
grant select, insert, update, delete on all tables in schema public to voxlink_app;
alter default privileges in schema public grant select, insert, update, delete on tables to voxlink_app;
