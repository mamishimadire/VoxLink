-- An ad-hoc invoice advances subscriptions.current_period_start so the
-- *next* invoice doesn't re-bill the same calls, but it had no memory of
-- whether the flat monthly fee or part of the included-minutes pool was
-- already billed earlier in the same period — a second ad-hoc invoice (or
-- the month-end auto invoice, if it fires after an ad-hoc one already did)
-- would re-charge the full monthly fee and re-grant the full included
-- pool, double-billing the fee and under-billing overage.
--
-- Both reset to false/0 whenever a subscription's period actually rolls
-- over (current_period_end changes), and get set as each invoice for that
-- period is generated.
alter table subscriptions add column current_period_fee_billed boolean not null default false;
alter table subscriptions add column current_period_local_minutes_billed numeric(10,2) not null default 0;
