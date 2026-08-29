import { useEffect, useState, type FormEvent } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { useAutoRefresh } from "../hooks/useAutoRefresh";

interface CompanyUser {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  role: string;
  status: string;
}

interface Company {
  id: string;
  name: string;
  status: string;
}

interface UserLimit {
  maxUsers: number | null;
  currentUserCount: number;
}

const emptyForm = { firstName: "", lastName: "", email: "", role: "employee" };

function formatStatus(status: string) {
  return status.replace(/_/g, " ");
}

export function CompanyView() {
  const { token, role } = useAuth();
  const canManageUsers = role === "owner" || role === "admin";
  const isOwner = role === "owner";

  const [company, setCompany] = useState<Company | null>(null);
  const [users, setUsers] = useState<CompanyUser[]>([]);
  const [userLimit, setUserLimit] = useState<UserLimit | null>(null);
  const [form, setForm] = useState(emptyForm);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [manualLink, setManualLink] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function refresh() {
    const [companyResult, userResult, limitResult] = await Promise.allSettled([
      api.get<Company>("/api/companies/me", token),
      api.get<CompanyUser[]>("/api/users", token),
      api.get<UserLimit>("/api/billing/usage", token),
    ]);

    if (companyResult.status === "fulfilled") setCompany(companyResult.value);
    if (userResult.status === "fulfilled") setUsers(userResult.value);
    // limitResult failing is non-fatal — it's supplementary info.
    if (limitResult.status === "fulfilled") setUserLimit(limitResult.value);

    if (companyResult.status === "rejected") throw companyResult.reason;
    if (userResult.status === "rejected") throw userResult.reason;
  }

  const atUserLimit = userLimit?.maxUsers != null && users.length >= userLimit.maxUsers;

  function load() {
    // A prior failed refresh (or action) must not leave a stale error on
    // screen forever once things start working again.
    refresh()
      .then(() => setError(null))
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load."));
  }

  useEffect(load, []);
  // Another admin could add/deactivate a teammate at any time.
  useAutoRefresh(load, 15000);

  async function handleAddUser(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setMessage(null);
    setManualLink(null);
    setLoading(true);
    try {
      const res = await api.post<{ status: string; emailSent: boolean; manualLink: string | null }>(
        "/api/users",
        form,
        token,
      );
      const pendingApproval = res.status === "pending_approval";
      setMessage(
        pendingApproval
          ? `${form.email} was added and can set their password, but needs a business owner to approve them before they can sign in.`
          : res.emailSent
            ? `Invite sent to ${form.email}.`
            : `User created, but the invite email to ${form.email} failed to send. Copy the link below and send it to them another way.`,
      );
      setManualLink(res.manualLink);
      setForm(emptyForm);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to add user.");
    } finally {
      setLoading(false);
    }
  }

  async function handleApprove(userId: string) {
    setError(null);
    setMessage(null);
    try {
      const res = await api.put<{ message: string }>(`/api/users/${userId}/approve`, undefined, token);
      setMessage(res.message);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Action failed.");
    }
  }

  async function handleReject(userId: string) {
    setError(null);
    setMessage(null);
    try {
      const res = await api.put<{ message: string }>(`/api/users/${userId}/reject`, undefined, token);
      setMessage(res.message);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Action failed.");
    }
  }

  async function handleResetPassword(userId: string) {
    setError(null);
    setMessage(null);
    setManualLink(null);
    try {
      const res = await api.post<{ message: string; manualLink: string | null }>(`/api/users/${userId}/reset-password`, undefined, token);
      setMessage(res.message);
      setManualLink(res.manualLink);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Action failed.");
    }
  }

  async function handleDeactivate(userId: string) {
    setError(null);
    setMessage(null);
    try {
      await api.delete(`/api/users/${userId}`, token);
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Action failed.");
    }
  }

  return (
    <div>
      <h2>{company?.name ?? "Your company"}</h2>
      {userLimit && (
        <p className="hint">
          {users.length} of {userLimit.maxUsers ?? "unlimited"} users
          {atUserLimit && " — at your plan's limit, upgrade your tier to add more"}
        </p>
      )}
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
            <th>Name</th>
            <th>Email</th>
            <th>Role</th>
            <th>Status</th>
            {canManageUsers && <th></th>}
          </tr>
        </thead>
        <tbody>
          {users.map((u) => (
            <tr key={u.id}>
              <td>
                {u.firstName} {u.lastName}
              </td>
              <td>{u.email}</td>
              <td>{u.role}</td>
              <td>
                <span className={`badge badge-${u.status}`}>{formatStatus(u.status)}</span>
              </td>
              {canManageUsers && (
                <td className="actions">
                  {u.status === "pending_approval" && isOwner && (
                    <>
                      <button className="link-btn" onClick={() => handleApprove(u.id)}>
                        Approve
                      </button>
                      <button className="link-btn" onClick={() => handleReject(u.id)}>
                        Reject
                      </button>
                    </>
                  )}
                  {u.status === "pending_approval" && !isOwner && (
                    <span className="hint">Awaiting owner approval</span>
                  )}
                  {u.status !== "pending_approval" && (
                    <button className="link-btn" onClick={() => handleResetPassword(u.id)}>
                      Reset password
                    </button>
                  )}
                  {u.status === "active" && (
                    <button className="link-btn" onClick={() => handleDeactivate(u.id)}>
                      Deactivate
                    </button>
                  )}
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>

      {canManageUsers && (
        <>
          <h2>Add a user</h2>
          <form className="card inline-card" onSubmit={handleAddUser}>
            <div className="row">
              <label>
                First name
                <input value={form.firstName} onChange={(e) => setForm((f) => ({ ...f, firstName: e.target.value }))} required />
              </label>
              <label>
                Last name
                <input value={form.lastName} onChange={(e) => setForm((f) => ({ ...f, lastName: e.target.value }))} required />
              </label>
            </div>
            <label>
              Email
              <input
                type="email"
                value={form.email}
                onChange={(e) => setForm((f) => ({ ...f, email: e.target.value }))}
                required
              />
            </label>
            <label>
              Role
              <select value={form.role} onChange={(e) => setForm((f) => ({ ...f, role: e.target.value }))}>
                <option value="employee">Employee</option>
                <option value="manager">Manager</option>
                <option value="admin">Admin</option>
              </select>
            </label>
            <p className="hint">They'll receive an email to set their own password.</p>
            {atUserLimit && (
              <div className="error">Your plan allows up to {userLimit?.maxUsers} users. Upgrade your tier to add more.</div>
            )}
            <button type="submit" disabled={loading || atUserLimit}>
              {loading ? "Adding..." : "Add user"}
            </button>
          </form>
        </>
      )}
    </div>
  );
}
