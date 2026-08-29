import { useEffect, useState } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { useAutoRefresh } from "../hooks/useAutoRefresh";

interface AuditLogEntry {
  id: string;
  action: string;
  entityType: string | null;
  entityId: string | null;
  details: string | null;
  actorEmail: string | null;
  createdAt: string;
}

function formatAction(action: string) {
  return action.replace(/[._]/g, " ");
}

export function AuditLogView() {
  const { token } = useAuth();
  const [entries, setEntries] = useState<AuditLogEntry[]>([]);
  const [error, setError] = useState<string | null>(null);

  function load() {
    api
      .get<AuditLogEntry[]>("/api/audit-log", token)
      .then((entries) => {
        setEntries(entries);
        setError(null);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load audit log."));
  }

  useEffect(load, []);
  useAutoRefresh(load, 15000);

  return (
    <div>
      <h2>Audit log</h2>
      <p className="hint">A record of approvals, price changes, and other administrative actions on this account.</p>
      {error && <div className="error">{error}</div>}

      <table className="table">
        <thead>
          <tr>
            <th>When</th>
            <th>Action</th>
            <th>Details</th>
            <th>By</th>
          </tr>
        </thead>
        <tbody>
          {entries.map((e) => (
            <tr key={e.id}>
              <td>{new Date(e.createdAt).toLocaleString()}</td>
              <td style={{ textTransform: "capitalize" }}>{formatAction(e.action)}</td>
              <td>{e.details}</td>
              <td>{e.actorEmail ?? "—"}</td>
            </tr>
          ))}
          {entries.length === 0 && (
            <tr>
              <td colSpan={4} className="muted">
                No audit log entries yet.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
