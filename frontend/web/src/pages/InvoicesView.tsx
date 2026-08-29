import { useEffect, useState } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { useAutoRefresh } from "../hooks/useAutoRefresh";

interface Invoice {
  id: string;
  invoiceNumber: string;
  amountDue: number;
  amountPaid: number;
  status: string;
  dueDate: string | null;
  issuedAt: string;
}

interface Filters {
  number: string;
  status: string;
  year: string;
  from: string;
  to: string;
}

interface ClientOption {
  id: string;
  name: string;
}

interface PreviewLineItem {
  description: string;
  amount: number;
}

interface Preview {
  companyId: string;
  companyName: string;
  periodStart: string;
  periodEnd: string;
  lineItems: PreviewLineItem[];
  amountDue: number;
}

const emptyFilters: Filters = { number: "", status: "", year: "", from: "", to: "" };

export function InvoicesView() {
  const { token, role, isPlatformAdmin, claims } = useAuth();
  const canManage = role === "owner" || role === "admin";
  // Only VoxLink generates invoices — for a client or for its own internal
  // usage. A client never generates its own invoice, only views/pays one.
  const canGenerate = isPlatformAdmin && canManage;
  const ownCompanyId = claims?.company_id ?? "";

  const [filters, setFilters] = useState<Filters>(emptyFilters);
  const [invoices, setInvoices] = useState<Invoice[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [uploadingFor, setUploadingFor] = useState<string | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [modalOpen, setModalOpen] = useState(false);
  const [clients, setClients] = useState<ClientOption[] | null>(null);
  const [selectedClientId, setSelectedClientId] = useState("");
  const [preview, setPreview] = useState<Preview | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const [committing, setCommitting] = useState(false);

  async function load(silent = false) {
    if (!silent) setLoading(true);
    setError(null);
    try {
      const params = new URLSearchParams();
      if (filters.number.trim()) params.set("number", filters.number.trim());
      if (filters.status) params.set("status", filters.status);
      if (filters.year) params.set("year", filters.year);
      if (filters.from) params.set("from", filters.from);
      if (filters.to) params.set("to", filters.to);
      const qs = params.toString();
      const data = await api.get<Invoice[]>(`/api/billing/invoices${qs ? `?${qs}` : ""}`, token);
      setInvoices(data);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to load invoices.");
    } finally {
      if (!silent) setLoading(false);
    }
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  // A platform admin can generate a new invoice at any time.
  useAutoRefresh(() => load(true), 15000);

  function updateFilter<K extends keyof Filters>(key: K, value: Filters[K]) {
    setFilters((f) => ({ ...f, [key]: value }));
  }

  function clearFilters() {
    setFilters(emptyFilters);
  }

  async function loadPreview(companyId: string) {
    setPreviewLoading(true);
    setPreviewError(null);
    setPreview(null);
    try {
      const path =
        isPlatformAdmin && companyId !== ownCompanyId
          ? `/api/platform/companies/${companyId}/invoices/preview`
          : "/api/billing/invoices/preview";
      const data = await api.get<Preview>(path, token);
      setPreview(data);
    } catch (err) {
      setPreviewError(err instanceof ApiError ? err.message : "Failed to build invoice preview.");
    } finally {
      setPreviewLoading(false);
    }
  }

  async function openGenerateModal() {
    setModalOpen(true);
    setMessage(null);
    setError(null);

    if (isPlatformAdmin) {
      setSelectedClientId(ownCompanyId);
      if (clients === null) {
        try {
          const list = await api.get<{ id: string; name: string }[]>("/api/platform/companies", token);
          setClients([{ id: ownCompanyId, name: "VoxLink (internal usage)" }, ...list.map((c) => ({ id: c.id, name: c.name }))]);
        } catch (err) {
          setPreviewError(err instanceof ApiError ? err.message : "Failed to load client list.");
        }
      }
      await loadPreview(ownCompanyId);
    } else {
      await loadPreview(ownCompanyId);
    }
  }

  function closeGenerateModal() {
    setModalOpen(false);
    setPreview(null);
    setPreviewError(null);
  }

  function handleChooseClient(companyId: string) {
    setSelectedClientId(companyId);
    loadPreview(companyId);
  }

  async function handleComplete() {
    if (!preview) return;
    setCommitting(true);
    setPreviewError(null);
    try {
      const path =
        isPlatformAdmin && preview.companyId !== ownCompanyId
          ? `/api/platform/companies/${preview.companyId}/invoices/generate`
          : "/api/billing/invoices/generate";
      const res = await api.post<{ message: string }>(path, {}, token);
      setMessage(res.message);
      closeGenerateModal();
      await load();
    } catch (err) {
      setPreviewError(err instanceof ApiError ? err.message : "Failed to generate invoice.");
    } finally {
      setCommitting(false);
    }
  }

  async function handleDownload(id: string) {
    const res = await api.get<{ url: string }>(`/api/billing/invoices/${id}/pdf`, token);
    window.open(res.url, "_blank");
  }

  async function handleUploadProof(invoiceId: string) {
    if (!file) return;
    setError(null);
    setMessage(null);
    try {
      const form = new FormData();
      form.append("file", file);
      const res = await api.postForm<{ message: string }>(`/api/billing/invoices/${invoiceId}/proof`, form, token);
      setMessage(res.message);
      setUploadingFor(null);
      setFile(null);
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to upload proof of payment.");
    }
  }

  const yearOptions = Array.from({ length: 6 }, (_, i) => new Date().getFullYear() - i);

  return (
    <div>
      <h2>Invoices</h2>
      <p className="hint">Search and filter your invoice history, download any invoice as a PDF, or generate one now.</p>

      {message && <div className="success">{message}</div>}
      {error && <div className="error">{error}</div>}

      <div className="card inline-card">
        <div className="row" style={{ flexWrap: "wrap" }}>
          <label style={{ flex: "1 1 160px" }}>
            Invoice number
            <input
              placeholder="INV-2026-00001"
              value={filters.number}
              onChange={(e) => updateFilter("number", e.target.value)}
            />
          </label>
          <label style={{ flex: "0 1 140px" }}>
            Year
            <select value={filters.year} onChange={(e) => updateFilter("year", e.target.value)}>
              <option value="">Any</option>
              {yearOptions.map((y) => (
                <option key={y} value={y}>
                  {y}
                </option>
              ))}
            </select>
          </label>
          <label style={{ flex: "0 1 160px" }}>
            Status
            <select value={filters.status} onChange={(e) => updateFilter("status", e.target.value)}>
              <option value="">Any</option>
              <option value="pending">Pending</option>
              <option value="paid">Paid</option>
              <option value="overdue">Overdue</option>
              <option value="void">Void</option>
            </select>
          </label>
          <label style={{ flex: "0 1 160px" }}>
            From
            <input type="date" value={filters.from} onChange={(e) => updateFilter("from", e.target.value)} />
          </label>
          <label style={{ flex: "0 1 160px" }}>
            To
            <input type="date" value={filters.to} onChange={(e) => updateFilter("to", e.target.value)} />
          </label>
        </div>
        <div className="row">
          <button type="button" onClick={load} disabled={loading}>
            {loading ? "Searching…" : "Search"}
          </button>
          <button type="button" className="link-btn" onClick={clearFilters}>
            Clear filters
          </button>
          <div style={{ flex: 1 }} />
          {canGenerate && (
            <button type="button" onClick={openGenerateModal}>
              Generate invoice
            </button>
          )}
        </div>
      </div>

      <table className="table">
        <thead>
          <tr>
            <th>Invoice #</th>
            <th>Issued</th>
            <th>Amount</th>
            <th>Paid</th>
            <th>Status</th>
            <th>Due</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {invoices?.map((inv) => (
            <tr key={inv.id}>
              <td>{inv.invoiceNumber}</td>
              <td>{inv.issuedAt.slice(0, 10)}</td>
              <td>R{inv.amountDue.toFixed(2)}</td>
              <td>R{inv.amountPaid.toFixed(2)}</td>
              <td>
                <span className={`badge badge-${inv.status === "paid" ? "active" : inv.status === "submitted" ? "invited" : "pending"}`}>
                  {inv.status}
                </span>
              </td>
              <td>{inv.dueDate?.slice(0, 10) ?? "—"}</td>
              <td className="actions">
                <button className="link-btn" onClick={() => handleDownload(inv.id)}>
                  Download
                </button>
                {canManage && inv.status === "pending" && (
                  <button className="link-btn" onClick={() => setUploadingFor(inv.id)}>
                    Upload proof
                  </button>
                )}
              </td>
            </tr>
          ))}
          {invoices !== null && invoices.length === 0 && (
            <tr>
              <td colSpan={7} className="muted">
                No invoices match your filters.
              </td>
            </tr>
          )}
        </tbody>
      </table>

      {uploadingFor && (
        <div className="card inline-card">
          <h2>Upload proof of payment</h2>
          <label>
            File
            <input type="file" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
          </label>
          <div className="row">
            <button type="button" disabled={!file} onClick={() => handleUploadProof(uploadingFor)}>
              Upload
            </button>
            <button type="button" className="link-btn" onClick={() => setUploadingFor(null)}>
              Cancel
            </button>
          </div>
        </div>
      )}

      {modalOpen && (
        <div className="card inline-card">
          <h2>Generate invoice</h2>

          {isPlatformAdmin && (
            <label>
              Client
              <select value={selectedClientId} onChange={(e) => handleChooseClient(e.target.value)}>
                {clients === null ? (
                  <option>Loading…</option>
                ) : (
                  clients.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}
                    </option>
                  ))
                )}
              </select>
            </label>
          )}

          {previewError && <div className="error">{previewError}</div>}

          {previewLoading && <p className="hint">Building preview…</p>}

          {!previewLoading && preview && (
            <div className="card inline-card">
              <p>
                <strong>{preview.companyName}</strong>
              </p>
              <p className="muted">
                Period: {preview.periodStart.slice(0, 10)} – {preview.periodEnd.slice(0, 10)}
              </p>
              <table className="table">
                <thead>
                  <tr>
                    <th>Description</th>
                    <th>Amount</th>
                  </tr>
                </thead>
                <tbody>
                  {preview.lineItems.map((item, i) => (
                    <tr key={i}>
                      <td>{item.description}</td>
                      <td>R{item.amount.toFixed(2)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <p>
                Total due: <strong>R{preview.amountDue.toFixed(2)}</strong>
              </p>
            </div>
          )}

          <div className="row">
            <button type="button" disabled={!preview || previewLoading || committing} onClick={handleComplete}>
              {committing ? "Sending…" : "Complete and send"}
            </button>
            <button type="button" className="link-btn" onClick={closeGenerateModal}>
              Cancel
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
