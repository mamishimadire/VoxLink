-- The audit_logs table existed since the initial schema but nothing wrote
-- to it yet. Adds the columns the new audit-log feature needs: a
-- human-readable one-liner (details) and the actor's email captured as
-- plain text at write time — not read via a join to users, so a client
-- owner reading their own log can still see who at VoxLink acted on their
-- account even though RLS would otherwise hide that user's row from them,
-- and so the record stays legible even if that user is later deleted.
alter table audit_logs add column details text;
alter table audit_logs add column actor_email text;
