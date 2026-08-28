-- "submitted" (client uploaded proof, awaiting platform admin review) was
-- used by the app but never added to this constraint — payments.status
-- check bug, blocking every proof-of-payment upload.
alter table payments drop constraint payments_status_check;
alter table payments add constraint payments_status_check
    check (status in ('pending', 'submitted', 'succeeded', 'failed', 'refunded'));
