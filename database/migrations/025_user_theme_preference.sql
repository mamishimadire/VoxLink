-- Light/dark theme is a per-user preference (not per-device/localStorage),
-- so it follows the signed-in user across browsers and devices, and never
-- affects any other user's session.
alter table users add column theme text not null default 'dark' check (theme in ('dark', 'light'));
