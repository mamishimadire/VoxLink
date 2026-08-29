-- Adds "system" (follow the OS/browser's own light/dark preference)
-- alongside the existing "dark"/"light" per-user theme choice.
alter table users drop constraint users_theme_check;
alter table users add constraint users_theme_check
    check (theme in ('dark', 'light', 'system'));
