import { useEffect, useState, type FormEvent } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { useAutoRefresh } from "../hooks/useAutoRefresh";

interface Company {
  id: string;
  name: string;
  status: string;
  adminContactName: string | null;
  adminContactEmail: string | null;
  createdAt: string;
  selectedPlanName: string | null;
  planName: string | null;
  licenseExpiresAt: string | null;
  maxUsers: number | null;
  currentUserCount: number;
  signupPaymentStatus: string;
}

interface UsageRow {
  companyId: string;
  companyName: string;
  callCount: number;
  totalMinutes: number;
}

interface Plan {
  id: string;
  name: string;
  monthlyPrice: number;
  includedMinutes: number;
}

interface PendingPayment {
  id: string;
  companyName: string;
  invoiceId: string | null;
  amount: number;
  proofFilePath: string | null;
  createdAt: string;
}

interface PendingRevokeRequest {
  id: string;
  companyId: string;
  companyName: string;
  proposedByRole: string;
  reason: string | null;
  proposedAt: string;
}

const emptyForm = {
  companyName: "",
  phone: "",
  country: "",
  region: "",
  primaryContactName: "",
  primaryContactEmail: "",
  billingContactName: "",
  billingContactEmail: "",
  adminContactName: "",
  adminContactEmail: "",
};

export function PlatformAdminView() {
  const { token, isBusinessOwner } = useAuth();
  const [companies, setCompanies] = useState<Company[]>([]);
  const [usage, setUsage] = useState<UsageRow[]>([]);
  const [plans, setPlans] = useState<Plan[]>([]);
  const [pendingPayments, setPendingPayments] = useState<PendingPayment[]>([]);
  const [pendingRevokes, setPendingRevokes] = useState<PendingRevokeRequest[]>([]);
  const [form, setForm] = useState(emptyForm);
  const [licenseFormFor, setLicenseFormFor] = useState<string | null>(null);
  const [licensePlanId, setLicensePlanId] = useState("");
  const [licenseExpiry, setLicenseExpiry] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [manualLink, setManualLink] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function refresh() {
    // Each piece loads independently: a hiccup fetching one (e.g. usage)
    // must not blank out data the others already fetched successfully.
    const [companyResult, usageResult, planResult, pendingPaymentResult, pendingRevokeResult] = await Promise.allSettled([
      api.get<Company[]>("/api/platform/companies", token),
      api.get<UsageRow[]>("/api/platform/usage", token),
      api.get<Plan[]>("/api/platform/plans", token),
      api.get<PendingPayment[]>("/api/platform/payments/pending", token),
      api.get<PendingRevokeRequest[]>("/api/platform/revoke-requests", token),
    ]);

    if (companyResult.status === "fulfilled") setCompanies(companyResult.value);
    if (usageResult.status === "fulfilled") setUsage(usageResult.value);
    if (planResult.status === "fulfilled") setPlans(planResult.value);
    if (pendingPaymentResult.status === "fulfilled") setPendingPayments(pendingPaymentResult.value);
    if (pendingRevokeResult.status === "fulfilled") setPendingRevokes(pendingRevokeResult.value);

    const failed = [companyResult, usageResult, planResult, pendingPaymentResult, pendingRevokeResult].find(
      (r) => r.status === "rejected",
    );
    if (failed && failed.status === "rejected") {
      throw failed.reason;
    }
  }

  async function handleViewProof(paymentId: string) {
    const res = await api.get<{ url: string }>(`/api/platform/payments/${paymentId}/proof`, token);
    window.open(res.url, "_blank");
  }

  async function handleVerifyPayment(paymentId: string) {
    setError(null);
    setMessage(null);
    try {
      const res = await api.post<{ message: string }>(`/api/platform/payments/${paymentId}/verify`, undefined, token);
      setMessage(res.message);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Verification failed.");
    }
  }

  function load() {
    refresh().catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load."));
  }

  useEffect(load, []);
  // New clients, payments, and usage can appear from other admins/sessions
  // at any time — keep this screen from going stale without a manual reload.
  useAutoRefresh(load, 15000);

  async function handleOnboard(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setMessage(null);
    setManualLink(null);
    setLoading(true);
    try {
      const res = await api.post<{ adminEmail: string; emailSent: boolean; manualLink: string | null }>(
        "/api/platform/companies",
        form,
        token,
      );
      setMessage(
        res.emailSent
          ? `${form.companyName} created as pending. An invite to log in was emailed to ${res.adminEmail}.`
          : `${form.companyName} created as pending, but the invite email to ${res.adminEmail} failed to send. Copy the link below and send it to them another way.`,
      );
      setManualLink(res.manualLink);
      setForm(emptyForm);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to onboard client.");
    } finally {
      setLoading(false);
    }
  }

  async function handleApprove(companyId: string) {
    setError(null);
    setMessage(null);
    try {
      const res = await api.post<{ message: string }>(`/api/platform/companies/${companyId}/approve`, undefined, token);
      setMessage(res.message);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Approval failed.");
    }
  }

  async function handleProposeRevoke(companyId: string) {
    setError(null);
    setMessage(null);
    try {
      const res = await api.post<{ message: string }>(`/api/platform/companies/${companyId}/revoke-request`, {}, token);
      setMessage(res.message);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Action failed.");
    }
  }

  async function handleReactivate(companyId: string) {
    setError(null);
    setMessage(null);
    try {
      await api.post(`/api/platform/companies/${companyId}/reactivate`, undefined, token);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Action failed.");
    }
  }

  async function handleReviewRevoke(id: string, approve: boolean) {
    setError(null);
    setMessage(null);
    try {
      const res = await api.post<{ message: string }>(
        `/api/approvals/revoke-requests/${id}/${approve ? "approve" : "reject"}`,
        {},
        token,
      );
      setMessage(res.message);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Action failed.");
    }
  }

  async function handleResetAdminPassword(companyId: string) {
    setError(null);
    setMessage(null);
    setManualLink(null);
    try {
      const res = await api.post<{ message: string; manualLink: string | null }>(
        `/api/platform/companies/${companyId}/reset-admin-password`,
        undefined,
        token,
      );
      setMessage(res.message);
      setManualLink(res.manualLink);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Action failed.");
    }
  }

  function openLicenseForm(companyId: string) {
    setLicenseFormFor(companyId);
    setLicensePlanId(plans[0]?.id ?? "");
    const oneYearOut = new Date();
    oneYearOut.setFullYear(oneYearOut.getFullYear() + 1);
    setLicenseExpiry(oneYearOut.toISOString().slice(0, 10));
  }

  async function handleSetLicense(companyId: string) {
    setError(null);
    setMessage(null);
    try {
      const res = await api.post<{ message: string }>(
        `/api/platform/companies/${companyId}/license`,
        { planId: licensePlanId, expiresAt: new Date(licenseExpiry).toISOString() },
        token,
      );
      setMessage(res.message);
      setLicenseFormFor(null);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to set license.");
    }
  }

  function update<K extends keyof typeof emptyForm>(key: K, value: string) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  return (
    <div>
      <h2>Client companies</h2>
      {message && <div className="success">{message}</div>}
      {error && <div className="error">{error}</div>}
      {manualLink && (
        <div className="card inline-card">
          <label>
            Manual invite/reset link
            <input readOnly value={manualLink} onClick={(e) => e.currentTarget.select()} />
          </label>
          <button type="button" onClick={() => navigator.clipboard.writeText(manualLink)}>
            Copy link
          </button>
        </div>
      )}

      <table className="table">
        <thead>
          <tr>
            <th>Company</th>
            <th>Admin contact</th>
            <th>Status</th>
            <th>Tier / Payment</th>
            <th>License</th>
            <th>Users</th>
            <th>Calls</th>
            <th>Minutes</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {companies.map((c) => {
            const u = usage.find((row) => row.companyId === c.id);
            return (
              <tr key={c.id}>
                <td>{c.name}</td>
                <td>
                  {c.adminContactName}
                  <br />
                  <span className="muted">{c.adminContactEmail}</span>
                </td>
                <td>
                  <span className={`badge badge-${c.status}`}>{c.status}</span>
                </td>
                <td>
                  {c.selectedPlanName ?? <span className="muted">not selected</span>}
                  <br />
                  <span className={`badge badge-${c.signupPaymentStatus === "succeeded" ? "active" : c.signupPaymentStatus === "submitted" ? "invited" : "pending"}`}>
                    {c.signupPaymentStatus === "none" ? "no payment" : c.signupPaymentStatus}
                  </span>
                </td>
                <td>
                  {c.planName ? (
                    <>
                      {c.planName}
                      <br />
                      <span className="muted">until {c.licenseExpiresAt?.slice(0, 10)}</span>
                    </>
                  ) : (
                    <span className="muted">none</span>
                  )}
                </td>
                <td>
                  {c.currentUserCount}
                  {c.maxUsers !== null ? ` / ${c.maxUsers}` : ""}
                </td>
                <td>{u?.callCount ?? 0}</td>
                <td>{u?.totalMinutes ?? 0}</td>
                <td className="actions">
                  {c.status === "pending" && (
                    <button className="link-btn" onClick={() => handleApprove(c.id)}>
                      Approve
                    </button>
                  )}
                  {c.status === "active" &&
                    (pendingRevokes.some((r) => r.companyId === c.id) ? (
                      <span className="muted">Revoke pending review</span>
                    ) : (
                      <button className="link-btn" onClick={() => handleProposeRevoke(c.id)}>
                        Request revoke
                      </button>
                    ))}
                  {c.status === "suspended" && (
                    <button className="link-btn" onClick={() => handleReactivate(c.id)}>
                      Reactivate
                    </button>
                  )}
                  <button className="link-btn" onClick={() => handleResetAdminPassword(c.id)}>
                    Reset admin password
                  </button>
                  <button className="link-btn" onClick={() => openLicenseForm(c.id)}>
                    Set license
                  </button>
                </td>
              </tr>
            );
          })}
          {companies.length === 0 && (
            <tr>
              <td colSpan={9} className="muted">
                No client companies yet.
              </td>
            </tr>
          )}
        </tbody>
      </table>

      <h2>Pending license revoke requests</h2>
      <p className="hint">
        Neither an admin nor an owner can revoke a license alone: an admin's request needs an owner's approval, and an
        owner's request needs a manager's approval.
      </p>
      <table className="table">
        <thead>
          <tr>
            <th>Company</th>
            <th>Requested by</th>
            <th>Reason</th>
            <th>Requested</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {pendingRevokes.map((r) => {
            const canReview = r.proposedByRole === "admin" && isBusinessOwner;
            return (
              <tr key={r.id}>
                <td>{r.companyName}</td>
                <td style={{ textTransform: "capitalize" }}>{r.proposedByRole}</td>
                <td>{r.reason ?? "—"}</td>
                <td>{new Date(r.proposedAt).toLocaleString()}</td>
                <td className="actions">
                  {canReview ? (
                    <>
                      <button className="link-btn" onClick={() => handleReviewRevoke(r.id, true)}>
                        Approve
                      </button>
                      <button className="link-btn" onClick={() => handleReviewRevoke(r.id, false)}>
                        Reject
                      </button>
                    </>
                  ) : r.proposedByRole === "admin" ? (
                    <span className="muted">Awaiting a business owner</span>
                  ) : (
                    <span className="muted">Awaiting a manager</span>
                  )}
                </td>
              </tr>
            );
          })}
          {pendingRevokes.length === 0 && (
            <tr>
              <td colSpan={5} className="muted">
                No pending revoke requests.
              </td>
            </tr>
          )}
        </tbody>
      </table>

      <h2>Pending payment verification</h2>
      <table className="table">
        <thead>
          <tr>
            <th>Company</th>
            <th>Amount</th>
            <th>Submitted</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {pendingPayments.map((p) => (
            <tr key={p.id}>
              <td>{p.companyName}</td>
              <td>R{p.amount.toFixed(2)}</td>
              <td>{new Date(p.createdAt).toLocaleString()}</td>
              <td className="actions">
                <button className="link-btn" onClick={() => handleViewProof(p.id)}>
                  View proof
                </button>
                <button className="link-btn" onClick={() => handleVerifyPayment(p.id)}>
                  Verify
                </button>
              </td>
            </tr>
          ))}
          {pendingPayments.length === 0 && (
            <tr>
              <td colSpan={4} className="muted">
                Nothing awaiting verification.
              </td>
            </tr>
          )}
        </tbody>
      </table>

      {licenseFormFor && (
        <div className="card inline-card">
          <h2>Set license</h2>
          <label>
            Plan
            <select value={licensePlanId} onChange={(e) => setLicensePlanId(e.target.value)}>
              {plans.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name} — R{p.monthlyPrice}/mo, {p.includedMinutes} min included
                </option>
              ))}
            </select>
          </label>
          <label>
            Expires
            <input type="date" value={licenseExpiry} onChange={(e) => setLicenseExpiry(e.target.value)} />
          </label>
          <div className="row">
            <button type="button" onClick={() => handleSetLicense(licenseFormFor)}>
              Save license
            </button>
            <button type="button" className="link-btn" onClick={() => setLicenseFormFor(null)}>
              Cancel
            </button>
          </div>
        </div>
      )}

      <h2>Onboard a new client</h2>
      <form className="card inline-card" onSubmit={handleOnboard}>
        <label>
          Client name
          <input value={form.companyName} onChange={(e) => update("companyName", e.target.value)} required />
        </label>

        <label>
          Phone number
          <input type="tel" value={form.phone} onChange={(e) => update("phone", e.target.value)} required />
        </label>

        <div className="row">
          <label>
            Country
            <input value={form.country} onChange={(e) => update("country", e.target.value)} placeholder="e.g. South Africa" required />
          </label>
          <label>
            Province / region
            <input value={form.region} onChange={(e) => update("region", e.target.value)} placeholder="e.g. Gauteng" required />
          </label>
        </div>

        <div className="contact-grid">
          <span></span>
          <span className="col-label">Name</span>
          <span className="col-label">Email</span>

          <span>Primary contact</span>
          <input value={form.primaryContactName} onChange={(e) => update("primaryContactName", e.target.value)} required />
          <input
            type="email"
            value={form.primaryContactEmail}
            onChange={(e) => update("primaryContactEmail", e.target.value)}
            required
          />

          <span>Billing contact</span>
          <input value={form.billingContactName} onChange={(e) => update("billingContactName", e.target.value)} />
          <input type="email" value={form.billingContactEmail} onChange={(e) => update("billingContactEmail", e.target.value)} />

          <span>Administrative contact</span>
          <input value={form.adminContactName} onChange={(e) => update("adminContactName", e.target.value)} required />
          <input type="email" value={form.adminContactEmail} onChange={(e) => update("adminContactEmail", e.target.value)} required />
        </div>

        <p className="hint">
          This creates the client as <strong>pending</strong> and immediately emails their admin contact a link to
          log in, pick a tier, and pay — approve them below once their payment is verified.
        </p>

        <button type="submit" disabled={loading}>
          {loading ? "Creating..." : "Create client (pending)"}
        </button>
      </form>
    </div>
  );
}
