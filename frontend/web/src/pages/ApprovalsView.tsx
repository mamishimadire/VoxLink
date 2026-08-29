import { useEffect, useState } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { useAutoRefresh } from "../hooks/useAutoRefresh";

interface RevokeRequest {
  id: string;
  companyId: string;
  companyName: string;
  proposedByRole: string;
  reason: string | null;
  proposedAt: string;
}

interface InvoiceGenerationRequest {
  id: string;
  companyId: string;
  companyName: string;
  proposedAt: string;
}

export function ApprovalsView() {
  const { token } = useAuth();
  const [revokeRequests, setRevokeRequests] = useState<RevokeRequest[]>([]);
  const [invoiceRequests, setInvoiceRequests] = useState<InvoiceGenerationRequest[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    const [revokeResult, invoiceResult] = await Promise.allSettled([
      api.get<RevokeRequest[]>("/api/approvals/revoke-requests", token),
      api.get<InvoiceGenerationRequest[]>("/api/approvals/invoice-generation-requests", token),
    ]);
    if (revokeResult.status === "fulfilled") setRevokeRequests(revokeResult.value);
    if (invoiceResult.status === "fulfilled") setInvoiceRequests(invoiceResult.value);

    if (revokeResult.status === "rejected") throw revokeResult.reason;
    if (invoiceResult.status === "rejected") throw invoiceResult.reason;
  }

  function load() {
    refresh().catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load."));
  }

  useEffect(load, []);
  useAutoRefresh(load, 15000);

  async function handleReviewRevoke(id: string, approve: boolean) {
    setError(null);
    setMessage(null);
    try {
      const res = await api.post<{ message: string }>(`/api/approvals/revoke-requests/${id}/${approve ? "approve" : "reject"}`, {}, token);
      setMessage(res.message);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Action failed.");
    }
  }

  async function handleReviewInvoice(id: string, approve: boolean) {
    setError(null);
    setMessage(null);
    try {
      const res = await api.post<{ message: string }>(
        `/api/approvals/invoice-generation-requests/${id}/${approve ? "approve" : "reject"}`,
        {},
        token,
      );
      setMessage(res.message);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Action failed.");
    }
  }

  return (
    <div>
      <h2>Approvals</h2>
      <p className="hint">
        License revoke requests an owner proposed (an admin's proposal goes to an owner, not you), and every manually
        requested invoice, need your review before anything actually happens.
      </p>
      {message && <div className="success">{message}</div>}
      {error && <div className="error">{error}</div>}

      <h2>License revoke requests</h2>
      <table className="table">
        <thead>
          <tr>
            <th>Company</th>
            <th>Reason</th>
            <th>Requested</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {revokeRequests.map((r) => (
            <tr key={r.id}>
              <td>{r.companyName}</td>
              <td>{r.reason ?? "—"}</td>
              <td>{new Date(r.proposedAt).toLocaleString()}</td>
              <td className="actions">
                <button className="link-btn" onClick={() => handleReviewRevoke(r.id, true)}>
                  Approve
                </button>
                <button className="link-btn" onClick={() => handleReviewRevoke(r.id, false)}>
                  Reject
                </button>
              </td>
            </tr>
          ))}
          {revokeRequests.length === 0 && (
            <tr>
              <td colSpan={4} className="muted">
                No pending revoke requests.
              </td>
            </tr>
          )}
        </tbody>
      </table>

      <h2>Invoice generation requests</h2>
      <table className="table">
        <thead>
          <tr>
            <th>Company</th>
            <th>Requested</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {invoiceRequests.map((r) => (
            <tr key={r.id}>
              <td>{r.companyName}</td>
              <td>{new Date(r.proposedAt).toLocaleString()}</td>
              <td className="actions">
                <button className="link-btn" onClick={() => handleReviewInvoice(r.id, true)}>
                  Approve
                </button>
                <button className="link-btn" onClick={() => handleReviewInvoice(r.id, false)}>
                  Reject
                </button>
              </td>
            </tr>
          ))}
          {invoiceRequests.length === 0 && (
            <tr>
              <td colSpan={3} className="muted">
                No pending invoice generation requests.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
