import { useEffect, useState } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";

interface Plan {
  id: string;
  name: string;
  description: string;
  monthlyPrice: number;
  localRatePerMin: number;
  internationalRatePerMin: number;
  includedMinutes: number;
  minUsers: number;
  maxUsers: number | null;
  isCustomQuote: boolean;
}

interface ChangeRequest {
  id: string;
  currentPlanName: string;
  proposedBy: string;
  newName: string;
  newDescription: string | null;
  newMonthlyPrice: number;
  newIncludedMinutes: number;
  newLocalRatePerMin: number;
  newInternationalRatePerMin: number;
  newMinUsers: number;
  newMaxUsers: number | null;
  newIsCustomQuote: boolean;
  status: string;
  proposedAt: string;
  reviewNote: string | null;
}

const emptyEdit = {
  name: "",
  description: "",
  monthlyPrice: "",
  includedMinutes: "",
  localRatePerMin: "",
  internationalRatePerMin: "",
  minUsers: "",
  maxUsers: "",
  isCustomQuote: false,
};

export function PricingView() {
  const { token, claims, isBusinessOwner } = useAuth();
  const [plans, setPlans] = useState<Plan[]>([]);
  const [requests, setRequests] = useState<ChangeRequest[]>([]);
  const [editingPlanId, setEditingPlanId] = useState<string | null>(null);
  const [edit, setEdit] = useState(emptyEdit);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    const [planList, requestList] = await Promise.all([
      api.get<Plan[]>("/api/platform/plans", token),
      api.get<ChangeRequest[]>("/api/platform/plans/change-requests", token),
    ]);
    setPlans(planList);
    setRequests(requestList);
  }

  useEffect(() => {
    refresh().catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load."));
  }, []);

  function startEdit(plan: Plan) {
    setEditingPlanId(plan.id);
    setEdit({
      name: plan.name,
      description: plan.description,
      monthlyPrice: String(plan.monthlyPrice),
      includedMinutes: String(plan.includedMinutes),
      localRatePerMin: String(plan.localRatePerMin),
      internationalRatePerMin: String(plan.internationalRatePerMin),
      minUsers: String(plan.minUsers),
      maxUsers: plan.maxUsers === null ? "" : String(plan.maxUsers),
      isCustomQuote: plan.isCustomQuote,
    });
  }

  async function submitProposal() {
    if (!editingPlanId) return;
    setError(null);
    setMessage(null);
    try {
      const res = await api.post<{ message: string }>(
        `/api/platform/plans/${editingPlanId}/propose-change`,
        {
          newName: edit.name,
          newDescription: edit.description,
          newMonthlyPrice: Number(edit.monthlyPrice),
          newIncludedMinutes: Number(edit.includedMinutes),
          newLocalRatePerMin: Number(edit.localRatePerMin),
          newInternationalRatePerMin: Number(edit.internationalRatePerMin),
          newMinUsers: Number(edit.minUsers),
          newMaxUsers: edit.maxUsers === "" ? null : Number(edit.maxUsers),
          newIsCustomQuote: edit.isCustomQuote,
        },
        token,
      );
      setMessage(res.message);
      setEditingPlanId(null);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to propose change.");
    }
  }

  async function handleReview(id: string, approve: boolean) {
    setError(null);
    setMessage(null);
    try {
      const res = await api.post<{ message: string }>(`/api/platform/plans/change-requests/${id}/${approve ? "approve" : "reject"}`, { note: null }, token);
      setMessage(res.message);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Review failed.");
    }
  }

  const pendingRequests = requests.filter((r) => r.status === "pending");

  return (
    <div>
      <h2>Pricing</h2>
      {message && <div className="success">{message}</div>}
      {error && <div className="error">{error}</div>}
      <p className="hint">
        Price changes go through approval: any platform admin can propose a change, but only a business owner can
        approve it before it takes effect on the live plan. Included minutes only ever pool local usage —
        international calls are billed from the first minute at the international rate.
      </p>

      <table className="table">
        <thead>
          <tr>
            <th>Tier</th>
            <th>Platform fee</th>
            <th>Included</th>
            <th>Local rate</th>
            <th>Intl. rate</th>
            <th>Users</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {plans.map((p) => (
            <tr key={p.id}>
              <td>
                {p.name}
                <br />
                <span className="muted">{p.description}</span>
              </td>
              <td>{p.isCustomQuote ? "Custom quote" : `R${p.monthlyPrice}/mo`}</td>
              <td>{p.includedMinutes} min</td>
              <td>R{p.localRatePerMin.toFixed(2)}/min</td>
              <td>R{p.internationalRatePerMin.toFixed(2)}/min</td>
              <td>
                {p.minUsers}
                {p.maxUsers ? `–${p.maxUsers}` : "+"}
              </td>
              <td className="actions">
                <button className="link-btn" onClick={() => startEdit(p)}>
                  Propose change
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {editingPlanId && (
        <div className="card inline-card">
          <h2>Propose price change</h2>
          <label>
            Name
            <input value={edit.name} onChange={(e) => setEdit((f) => ({ ...f, name: e.target.value }))} />
          </label>
          <label>
            Description
            <input value={edit.description} onChange={(e) => setEdit((f) => ({ ...f, description: e.target.value }))} />
          </label>
          <div className="row">
            <label>
              Monthly platform fee (R)
              <input type="number" value={edit.monthlyPrice} onChange={(e) => setEdit((f) => ({ ...f, monthlyPrice: e.target.value }))} />
            </label>
            <label>
              Included minutes
              <input
                type="number"
                min="0"
                value={edit.includedMinutes}
                onChange={(e) => setEdit((f) => ({ ...f, includedMinutes: e.target.value }))}
              />
            </label>
            <label>
              Local rate (R/min)
              <input
                type="number"
                step="0.01"
                value={edit.localRatePerMin}
                onChange={(e) => setEdit((f) => ({ ...f, localRatePerMin: e.target.value }))}
              />
            </label>
            <label>
              International rate (R/min)
              <input
                type="number"
                step="0.01"
                value={edit.internationalRatePerMin}
                onChange={(e) => setEdit((f) => ({ ...f, internationalRatePerMin: e.target.value }))}
              />
            </label>
          </div>
          <div className="row">
            <label>
              Min users
              <input type="number" value={edit.minUsers} onChange={(e) => setEdit((f) => ({ ...f, minUsers: e.target.value }))} />
            </label>
            <label>
              Max users (blank = unlimited)
              <input type="number" value={edit.maxUsers} onChange={(e) => setEdit((f) => ({ ...f, maxUsers: e.target.value }))} />
            </label>
          </div>
          <label style={{ flexDirection: "row", alignItems: "center", gap: 8 }}>
            <input
              type="checkbox"
              checked={edit.isCustomQuote}
              onChange={(e) => setEdit((f) => ({ ...f, isCustomQuote: e.target.checked }))}
              style={{ width: "auto" }}
            />
            Custom quote tier
          </label>
          <div className="row">
            <button type="button" onClick={submitProposal}>
              Submit for approval
            </button>
            <button type="button" className="link-btn" onClick={() => setEditingPlanId(null)}>
              Cancel
            </button>
          </div>
        </div>
      )}

      <h2>Pending price changes</h2>
      <table className="table">
        <thead>
          <tr>
            <th>Tier</th>
            <th>Proposed change</th>
            <th>Proposed</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {pendingRequests.map((r) => (
            <tr key={r.id}>
              <td>{r.currentPlanName}</td>
              <td>
                {r.newName} — {r.newIsCustomQuote ? "custom quote" : `R${r.newMonthlyPrice}/mo`}, {r.newIncludedMinutes} min
                included, local R{r.newLocalRatePerMin.toFixed(2)}/min, intl. R{r.newInternationalRatePerMin.toFixed(2)}/min,{" "}
                {r.newMinUsers}
                {r.newMaxUsers ? `–${r.newMaxUsers}` : "+"} users
              </td>
              <td>{new Date(r.proposedAt).toLocaleString()}</td>
              <td className="actions">
                {isBusinessOwner && r.proposedBy !== claims?.sub ? (
                  <>
                    <button className="link-btn" onClick={() => handleReview(r.id, true)}>
                      Approve
                    </button>
                    <button className="link-btn" onClick={() => handleReview(r.id, false)}>
                      Reject
                    </button>
                  </>
                ) : isBusinessOwner ? (
                  <span className="muted">You proposed this — another business owner must review it</span>
                ) : (
                  <span className="muted">Awaiting business owner</span>
                )}
              </td>
            </tr>
          ))}
          {pendingRequests.length === 0 && (
            <tr>
              <td colSpan={4} className="muted">
                No pending price changes.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
