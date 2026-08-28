-- VoxLink's own internal call cost should be priced at the Large tier's
-- per-minute rates (the closest proxy to actual carrier cost VoxLink pays),
-- never the Large tier's platform fee or included-minutes pool — the
-- Internal Usage plan already has monthly_price = 0 and included_minutes = 0
-- for that reason. This is a one-time sync to the Large tier's current
-- rates; PlatformController.ApprovePlanChange keeps them in sync going
-- forward whenever the Large tier's rates are changed.
update plans
set local_rate_per_min = large.local_rate_per_min,
    international_rate_per_min = large.international_rate_per_min
from (select local_rate_per_min, international_rate_per_min from plans where name = 'Large') as large
where plans.name = 'Internal Usage';
