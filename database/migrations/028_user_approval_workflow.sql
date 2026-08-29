-- Segregation of duties for adding teammates: an admin-created user now
-- starts "pending_approval" and only goes live once a business owner in
-- the same company approves it (the admin cannot approve their own
-- addition). Owner-created users still go straight to "active" since
-- there's no higher authority in the company to review them against.
alter table users drop constraint users_status_check;
alter table users add constraint users_status_check
    check (status in ('active', 'suspended', 'invited', 'pending_approval', 'rejected'));
