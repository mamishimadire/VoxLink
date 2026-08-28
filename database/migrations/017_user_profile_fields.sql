-- Lets any user edit their own profile: country/region, gender, and a
-- photo (stored in Supabase Storage, this column just holds the public URL).

alter table users add column country text;
alter table users add column region text;
alter table users add column gender text;
alter table users add column profile_picture_url text;
