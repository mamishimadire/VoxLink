-- Splits the flat per-minute overage rate into local vs international.
-- Included minutes only ever pool local usage — international calls are
-- billed from the first minute, never covered by the plan's included pool.
alter table plans add column local_rate_per_min numeric(10,4) not null default 0;
alter table plans add column international_rate_per_min numeric(10,4) not null default 0;

update plans set local_rate_per_min = 3.00, international_rate_per_min = 5.50 where name = 'Small';
update plans set local_rate_per_min = 2.50, international_rate_per_min = 4.50 where name = 'Medium';
update plans set local_rate_per_min = 2.00, international_rate_per_min = 3.50 where name = 'Large';

-- The maker-checker price-change workflow needs to carry proposals for
-- these two new rates as well.
alter table plan_change_requests add column new_local_rate_per_min numeric(10,4) not null default 0;
alter table plan_change_requests add column new_international_rate_per_min numeric(10,4) not null default 0;
