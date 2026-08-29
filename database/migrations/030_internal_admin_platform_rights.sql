-- Any teammate added to VoxLink's own internal company with the "admin"
-- role is platform staff by definition and must be able to manage clients
-- and pricing — new admin creation already sets this going forward
-- (UsersController.CreateUser); this backfills anyone added before that fix
-- who is stuck without it (e.g. an internal admin who couldn't see pricing).
update users
set is_platform_admin = true
from companies
where users.company_id = companies.id
  and companies.is_internal = true
  and users.role = 'admin'
  and users.is_platform_admin = false;
