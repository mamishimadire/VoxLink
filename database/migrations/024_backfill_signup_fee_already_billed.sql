-- PlatformController.Approve/SetLicense now mark a brand-new subscription's
-- current_period_fee_billed = true when the company already paid the
-- platform fee upfront via its signup invoice — otherwise the
-- subscription's first real invoice charges that same fee a second time.
-- This backfills any subscription created before that fix: if it hasn't
-- had a real invoice generated against it yet (fee not billed, no minutes
-- billed) and the company has a paid signup invoice, the fee is already
-- covered.
update subscriptions s
set current_period_fee_billed = true
where s.current_period_fee_billed = false
  and s.current_period_local_minutes_billed = 0
  and exists (
    select 1 from invoices i
    where i.company_id = s.company_id
      and i.subscription_id is null
      and i.status = 'paid'
  );
