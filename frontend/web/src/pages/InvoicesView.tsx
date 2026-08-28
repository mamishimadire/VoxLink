import { useEffect, useState } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";

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

const emptyFilters: Filters = { number: "", status: "", year: "", from: "", to: "" };

export function InvoicesView() {
  const { token, role } = useAuth();
  const canManage = role === "owner" || role === "admin";

  const [filters, setFilters] = useState<Filters>(emptyFilters);
  const [invoices, setInvoices] = useState<Invoice[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [generating, setGenerating] = useState(false);
  const [uploadingFor, setUploadingFor] = useState<string | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
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
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function updateFilter<K extends keyof Filters>(key: K, value: Filters[K]) {
    setFilters((f) => ({ ...f, [key]: value }));
  }

  function clearFilters() {
    setFilters(emptyFilters);
  }

  async function handleGenerate() {
    setGenerating(true);
    setMessage(null);
    setError(null);
    try {
      const res = await api.post<{ message: string }>("/api/billing/invoices/generate", {}, token);
      setMessage(res.message);
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to generate invoice.");
    } finally {
      setGenerating(false);
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
          {canManage && (
            <button type="button" onClick={handleGenerate} disabled={generating}>
              {generating ? "Generating…" : "Generate invoice now"}
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
    </div>
  );
}
