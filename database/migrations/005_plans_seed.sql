insert into plans (id, name, description, monthly_price, included_minutes, per_minute_rate, status)
values
    (gen_random_uuid(), 'Starter', 'Small teams getting started', 499.00, 500, 1.20, 'active'),
    (gen_random_uuid(), 'Professional', 'Growing teams with higher call volume', 1499.00, 2000, 0.95, 'active'),
    (gen_random_uuid(), 'Enterprise', 'Large teams, custom terms', 3999.00, 10000, 0.75, 'active');
