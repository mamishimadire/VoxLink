-- Lets a user clear entries out of their own call history view without
-- destroying usage data the automatic invoicing run still needs to see —
-- deleted calls are hidden from /api/calls/recent but still counted in
-- billing for the period they actually happened in.

alter table calls add column deleted_at timestamptz;
