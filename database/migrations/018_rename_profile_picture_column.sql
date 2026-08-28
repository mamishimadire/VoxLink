-- Matches the existing invoice/payment-proof pattern: store the Supabase
-- Storage object path, not a URL — signed URLs are minted on request.
alter table users rename column profile_picture_url to profile_picture_path;
