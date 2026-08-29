-- Lets a user star a contact or a past call as a favorite. Contacts are a
-- shared company address book, so favoriting one is shared too; calls are
-- already scoped to the user who made them (calls.user_id), so favoriting
-- a call is inherently per-user already.
alter table contacts add column is_favorite boolean not null default false;
alter table calls add column is_favorite boolean not null default false;
