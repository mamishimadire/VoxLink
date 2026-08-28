-- Renames the leftover Telnyx-era column now that calls go through Twilio,
-- and adds the tables/columns needed for: the signed pay-as-you-go agreement,
-- proof-of-payment uploads, and downloadable invoice PDFs.

alter table calls rename column telnyx_call_id to provider_call_id;

create table service_agreements (
    id uuid primary key default gen_random_uuid(),
    company_id uuid not null references companies(id) on delete cascade,
    terms_version text not null,
    agreed_by_name text not null,
    agreed_by_email text not null,
    agreed_at timestamptz not null default now(),
    ip_address inet,
    pdf_storage_path text not null,
    created_at timestamptz not null default now()
);

create index idx_service_agreements_company_id on service_agreements(company_id);

alter table payments add column proof_file_path text;
alter table invoices add column pdf_storage_path text;
