import { useEffect, useState } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { useAutoRefresh } from "../hooks/useAutoRefresh";

interface Usage {
  planName: string | null;
  includedMinutes: number;
  localMinutesUsed: number;
  internationalMinutesUsed: number;
  localRatePerMin: number;
  internationalRatePerMin: number;
  callCount: number;
  estimatedAmountDue: number;
  periodStart: string | null;
  periodEnd: string | null;
  maxUsers: number | null;
  currentUserCount: number;
}

interface Agreement {
  signed: boolean;
  agreement: { agreedByName: string; agreedAt: string } | null;
}

interface UserUsage {
  userId: string;
  userName: string;
  callCount: number;
  totalMinutes: number;
}

interface DestinationUsage {
  destinationNumber: string;
  callCount: number;
  totalMinutes: number;
}

interface Analytics {
  periodStart: string;
  periodEnd: string;
  byUser: UserUsage[];
  byDestination: DestinationUsage[];
}

interface UserUsageRow {
  userId: string;
  userName: string;
  callCount: number;
  totalMinutes: number;
}

interface DestinationUsageRow {
  destinationNumber: string;
  callCount: number;
  totalMinutes: number;
}

interface Analytics {
  periodStart: string;
  periodEnd: string;
  byUser: UserUsageRow[];
  byDestination: DestinationUsageRow[];
}

interface ClientUsageRow {
  companyId: string;
  companyName: string;
  callCount: number;
  totalMinutes: number;
}

export function BillingView() {
  const { token, role, isPlatformAdmin } = useAuth();
  const canManage = role === "owner" || role === "admin";

  const [usage, setUsage] = useState<Usage | null>(null);
  const [analytics, setAnalytics] = useState<Analytics | null>(null);
  const [agreement, setAgreement] = useState<Agreement | null>(null);
  const [isInternal, setIsInternal] = useState(false);
  const [clientUsage, setClientUsage] = useState<ClientUsageRow[] | null>(null);
  const [fullName, setFullName] = useState("");
  const [agree, setAgree] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    const [usageResult, companyResult, agreementResult, analyticsResult, clientUsageResult] = await Promise.allSettled([
      api.get<Usage>("/api/billing/usage", token),
      api.get<{ isInternal: boolean }>("/api/companies/me", token),
      api.get<Agreement>("/api/billing/agreement", token),
      api.get<Analytics>("/api/billing/analytics", token),
      isPlatformAdmin ? api.get<ClientUsageRow[]>("/api/platform/usage", token) : Promise.resolve(null),
    ]);
    if (usageResult.status === "fulfilled") setUsage(usageResult.value);
    if (companyResult.status === "fulfilled") setIsInternal(companyResult.value.isInternal);
    if (agreementResult.status === "fulfilled") setAgreement(agreementResult.value);
    if (analyticsResult.status === "fulfilled") setAnalytics(analyticsResult.value);
    if (clientUsageResult.status === "fulfilled" && clientUsageResult.value) setClientUsage(clientUsageResult.value);
  }

  function load() {
    refresh().catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load."));
  }

  useEffect(load, []);
  // Usage/agreement/client-usage data can change from another admin's
  // action (or a new call being made) at any time.
  useAutoRefresh(load, 15000);

  async function handleSignAgreement() {
    setError(null);
    setMessage(null);
    try {
      const res = await api.post<{ message: string }>("/api/billing/agreement/sign", { fullName, agree }, token);
      setMessage(res.message);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to sign agreement.");
    }
  }

  return (
    <div>
      <h2>Billing</h2>
      {message && <div className="success">{message}</div>}
      {error && <div className="error">{error}</div>}

      <div className="card inline-card">
        <h2>Usage this cycle</h2>
        {usage?.planName ? (
          <>
            <p>
              Plan: <strong>{usage.planName}</strong>
            </p>
            <table className="table">
              <tbody>
                <tr>
                  <td>Local calls</td>
                  <td>
                    {usage.localMinutesUsed} / {usage.includedMinutes} min included
                  </td>
                  <td className="muted">R{usage.localRatePerMin.toFixed(2)}/min after included minutes</td>
                </tr>
                <tr>
                  <td>International calls</td>
                  <td>{usage.internationalMinutesUsed} min</td>
                  <td className="muted">R{usage.internationalRatePerMin.toFixed(2)}/min (not included in plan)</td>
                </tr>
              </tbody>
            </table>
            <p className="muted">{usage.callCount} calls this cycle</p>
            <p>
              Estimated amount due: <strong>R{usage.estimatedAmountDue.toFixed(2)}</strong> (platform fee + usage)
            </p>
            <p>
              Users: <strong>{usage.currentUserCount}</strong>
              {usage.maxUsers !== null ? ` / ${usage.maxUsers}` : " (no limit)"}
            </p>
            {usage.periodStart && usage.periodEnd && (
              <p className="muted">
                Period: {usage.periodStart.slice(0, 10)} – {usage.periodEnd.slice(0, 10)}
              </p>
            )}
          </>
        ) : (
          <p className="hint">No active license yet.</p>
        )}
      </div>

      {canManage && analytics && (analytics.byUser.length > 0 || analytics.byDestination.length > 0) && (
        <>
          <h2>Usage monitoring</h2>
          <p className="hint">
            {analytics.periodStart.slice(0, 10)} – {analytics.periodEnd.slice(0, 10)}. Use this to spot overuse or
            misuse before it shows up on the invoice.
          </p>
          <div className="row" style={{ alignItems: "flex-start" }}>
            <div style={{ flex: 1 }}>
              <h2 style={{ fontSize: 15 }}>By user</h2>
              <table className="table">
                <thead>
                  <tr>
                    <th>User</th>
                    <th>Calls</th>
                    <th>Minutes</th>
                  </tr>
                </thead>
                <tbody>
                  {analytics.byUser.map((row) => (
                    <tr key={row.userId}>
                      <td>{row.userName}</td>
                      <td>{row.callCount}</td>
                      <td>{row.totalMinutes}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div style={{ flex: 1 }}>
              <h2 style={{ fontSize: 15 }}>By destination</h2>
              <table className="table">
                <thead>
                  <tr>
                    <th>Number</th>
                    <th>Calls</th>
                    <th>Minutes</th>
                  </tr>
                </thead>
                <tbody>
                  {analytics.byDestination.map((row) => (
                    <tr key={row.destinationNumber}>
                      <td>{row.destinationNumber}</td>
                      <td>{row.callCount}</td>
                      <td>{row.totalMinutes}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </>
      )}

      {isPlatformAdmin && clientUsage && (
        <>
          <h2>Client usage</h2>
          <p className="hint">Calls and minutes used by each client company, across all time.</p>
          <table className="table">
            <thead>
              <tr>
                <th>Company</th>
                <th>Calls</th>
                <th>Minutes</th>
              </tr>
            </thead>
            <tbody>
              {clientUsage
                .slice()
                .sort((a, b) => b.totalMinutes - a.totalMinutes)
                .map((row) => (
                  <tr key={row.companyId}>
                    <td>{row.companyName}</td>
                    <td>{row.callCount}</td>
                    <td>{row.totalMinutes}</td>
                  </tr>
                ))}
              {clientUsage.length === 0 && (
                <tr>
                  <td colSpan={3} className="muted">
                    No client companies yet.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </>
      )}

      {!isInternal && (
        <>
          <h2>Agreement</h2>
          {agreement?.signed ? (
            <div className="card inline-card">
              <p className="success" style={{ margin: 0 }}>
                Signed by {agreement.agreement?.agreedByName} on {agreement.agreement?.agreedAt.slice(0, 10)}
              </p>
            </div>
          ) : canManage ? (
            <div className="card inline-card">
              <p className="hint">You need to sign the pay-as-you-go services agreement.</p>
              <label>
                Full name
                <input value={fullName} onChange={(e) => setFullName(e.target.value)} />
              </label>
              <label style={{ flexDirection: "row", alignItems: "center", gap: 8 }}>
                <input type="checkbox" checked={agree} onChange={(e) => setAgree(e.target.checked)} style={{ width: "auto" }} />
                I agree to the terms
              </label>
              <button type="button" disabled={!fullName || !agree} onClick={handleSignAgreement}>
                Sign agreement
              </button>
            </div>
          ) : (
            <p className="hint">Not yet signed — ask your company admin to sign it.</p>
          )}
        </>
      )}
    </div>
  );
}
