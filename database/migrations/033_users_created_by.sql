-- Tracks who added a teammate, so the admin who submitted it (not just the
-- owner who approves it) can see a "still pending" reminder of their own
-- submission rather than it silently disappearing from view.
alter table users add column created_by uuid references users(id) on delete set null;
