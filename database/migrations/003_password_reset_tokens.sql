-- Supports emailed, token-based password setup/reset instead of returning
-- plaintext passwords from the API. The raw token is only ever emailed;
-- we store its hash so a DB leak alone can't be used to reset a password.

alter table users add column password_reset_token_hash text;
alter table users add column password_reset_expires_at timestamptz;
