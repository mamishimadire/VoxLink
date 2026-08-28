-- Store as text rather than inet: simpler mapping from EF Core/Npgsql,
-- and we only ever use this for display/evidence, never for querying by subnet.
alter table service_agreements alter column ip_address type text;
