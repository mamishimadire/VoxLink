import { useEffect, useState } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";

interface Plan {
  id: string;
  name: string;
  description: string;
  monthlyPrice: number;
  minUsers: number;
  maxUsers: number | null;
  isCustomQuote: boolean;
}

interface OnboardingStatus {
  companyStatus: string;
  selectedPlanName: string | null;
  signupInvoiceId: string | null;
  signupInvoiceAmount: number | null;
  signupPaymentStatus: string | null;
}

export function OnboardingPage() {
  const { token, logout } = useAuth();
  const [plans, setPlans] = useState<Plan[]>([]);
  const [status, setStatus] = useState<OnboardingStatus | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function refresh() {
    const [planList, statusData] = await Promise.all([
      api.get<Plan[]>("/api/billing/plans", token),
      api.get<OnboardingStatus>("/api/billing/onboarding-status", token),
    ]);
    setPlans(planList);
    setStatus(statusData);
  }

  useEffect(() => {
    refresh().catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load."));
  }, []);

  async function handleSelectPlan(planId: string) {
    setError(null);
    setMessage(null);
    setLoading(true);
    try {
      const res = await api.post<{ message: string }>("/api/billing/select-plan", { planId }, token);
      setMessage(res.message);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to select plan.");
    } finally {
      setLoading(false);
    }
  }

  async function handleUploadProof() {
    if (!file || !status?.signupInvoiceId) return;
    setError(null);
    setMessage(null);
    setLoading(true);
    try {
      const form = new FormData();
      form.append("file", file);
      const res = await api.postForm<{ message: string }>(
        `/api/billing/invoices/${status.signupInvoiceId}/proof`,
        form,
        token,
      );
      setMessage(res.message);
      setFile(null);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to upload proof of payment.");
    } finally {
      setLoading(false);
    }
  }

  async function handleDownloadInvoice() {
    if (!status?.signupInvoiceId) return;
    const res = await api.get<{ url: string }>(`/api/billing/invoices/${status.signupInvoiceId}/pdf`, token);
    window.open(res.url, "_blank");
  }

  const awaitingVerification = status?.signupPaymentStatus === "submitted";
  const verified = status?.signupPaymentStatus === "succeeded";

  return (
    <div className="dashboard">
      <header className="topbar">
        <span className="brand">VoxLink</span>
        <div className="topbar-right">
          <button className="link-btn" onClick={logout}>
            Sign out
          </button>
        </div>
      </header>

      <main className="content">
        <h2>Finish setting up your account</h2>
        <p className="hint">
          Your account is pending approval. Select a plan and complete payment of the platform fee — once our
          team verifies it, your account will be fully activated.
        </p>

        {message && <div className="success">{message}</div>}
        {error && <div className="error">{error}</div>}

        {verified && (
          <div className="success">
            Payment verified. Your account is being reviewed for final approval — check back shortly.
          </div>
        )}

        {!status?.selectedPlanName && (
          <div className="table" style={{ display: "grid", gap: 16, gridTemplateColumns: "repeat(3, 1fr)" }}>
            {plans.map((p) => (
              <div key={p.id} className="card">
                <h2>{p.name}</h2>
                <p className="hint">{p.description}</p>
                <p style={{ fontSize: 22, fontWeight: 700 }}>
                  {p.isCustomQuote ? "Custom quote" : `R${p.monthlyPrice}/mo`}
                </p>
                <button type="button" disabled={loading} onClick={() => handleSelectPlan(p.id)}>
                  Choose {p.name}
                </button>
              </div>
            ))}
          </div>
        )}

        {status?.selectedPlanName && status.signupInvoiceAmount != null && (
          <div className="card inline-card">
            <h2>Selected plan: {status.selectedPlanName}</h2>
            <p>
              Amount due: <strong>R{status.signupInvoiceAmount.toFixed(2)}</strong>
            </p>
            <button type="button" className="link-btn" onClick={handleDownloadInvoice}>
              Download invoice PDF
            </button>

            {!awaitingVerification && !verified && (
              <>
                <label>
                  Proof of payment
                  <input type="file" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
                </label>
                <button type="button" disabled={!file || loading} onClick={handleUploadProof}>
                  {loading ? "Uploading..." : "Upload proof of payment"}
                </button>
              </>
            )}

            {awaitingVerification && (
              <p className="hint">Proof of payment submitted — awaiting verification by our team.</p>
            )}
          </div>
        )}

        {status?.selectedPlanName && status.signupInvoiceAmount == null && (
          <div className="card inline-card">
            <h2>Selected plan: {status.selectedPlanName}</h2>
            <p className="hint">This is a custom-quote tier. Our team will follow up with pricing shortly.</p>
          </div>
        )}
      </main>
    </div>
  );
}
