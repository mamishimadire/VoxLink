-- Replaces the placeholder minute-based plans with the real active-user tiers,
-- and lets a company record which tier it selected during signup (before it's
-- approved / has a real subscription).

alter table plans add column min_users integer not null default 0;
alter table plans add column max_users integer;
alter table plans add column is_custom_quote boolean not null default false;

delete from plans;

insert into plans (id, name, description, monthly_price, included_minutes, per_minute_rate, min_users, max_users, is_custom_quote, status)
values
    (gen_random_uuid(), 'Small', '1-10 active users', 750.00, 0, 0, 1, 10, false, 'active'),
    (gen_random_uuid(), 'Medium', '11-50 active users', 2000.00, 0, 0, 11, 50, false, 'active'),
    (gen_random_uuid(), 'Large', '50+ active users, custom quote', 4500.00, 0, 0, 51, null, true, 'active');

alter table companies add column selected_plan_id uuid references plans(id);
