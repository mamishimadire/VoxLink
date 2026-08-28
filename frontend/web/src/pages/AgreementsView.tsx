import { useEffect, useState } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";

interface Agreement {
  id: string;
  companyName: string;
  agreedByName: string;
  agreedByEmail: string;
  agreedAt: string;
  termsVersion: string;
}

export function AgreementsView() {
  const { token } = useAuth();
  const [agreements, setAgreements] = useState<Agreement[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    try {
      const data = await api.get<Agreement[]>("/api/platform/agreements", token);
      setAgreements(data);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to load agreements.");
    }
  }

  useEffect(() => {
    load();
  }, []);

  async function handleDownload(id: string) {
    const res = await api.get<{ url: string }>(`/api/platform/agreements/${id}/pdf`, token);
    window.open(res.url, "_blank");
  }

  return (
    <div>
      <h2>Agreements</h2>
      <p className="hint">Every client's signed pay-as-you-go services agreement, downloadable at any time.</p>
      {error && <div className="error">{error}</div>}

      <table className="table">
        <thead>
          <tr>
            <th>Company</th>
            <th>Signed by</th>
            <th>Signed</th>
            <th>Terms version</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {agreements?.map((a) => (
            <tr key={a.id}>
              <td>{a.companyName}</td>
              <td>
                {a.agreedByName}
                <br />
                <span className="muted">{a.agreedByEmail}</span>
              </td>
              <td>{new Date(a.agreedAt).toLocaleString()}</td>
              <td>{a.termsVersion}</td>
              <td className="actions">
                <button className="link-btn" onClick={() => handleDownload(a.id)}>
                  Download
                </button>
              </td>
            </tr>
          ))}
          {agreements !== null && agreements.length === 0 && (
            <tr>
              <td colSpan={5} className="muted">
                No signed agreements yet.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
