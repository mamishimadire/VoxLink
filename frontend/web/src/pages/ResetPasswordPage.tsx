import { useState, type FormEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { api, ApiError } from "../api/client";
import { AuthLayout } from "../components/AuthLayout";

export function ResetPasswordPage() {
  const [searchParams] = useSearchParams();
  const token = searchParams.get("token") ?? "";
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [done, setDone] = useState(false);
  const navigate = useNavigate();

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);

    if (password !== confirm) {
      setError("Passwords don't match.");
      return;
    }

    setLoading(true);
    try {
      await api.post("/api/auth/reset-password", { token, newPassword: password });
      setDone(true);
      setTimeout(() => navigate("/login"), 2000);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Something went wrong.");
    } finally {
      setLoading(false);
    }
  }

  if (!token) {
    return (
      <AuthLayout>
        <div>
          <h1>VoxLink</h1>
          <p className="error">This link is missing its token. Use the link from your email.</p>
          <div className="links">
            <Link to="/login">Back to sign in</Link>
          </div>
        </div>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout>
      <form onSubmit={handleSubmit}>
        <h1>VoxLink</h1>
        <p className="subtitle">Choose a new password</p>

        {error && <div className="error">{error}</div>}

        {done ? (
          <p className="hint">Password set. Redirecting to sign in...</p>
        ) : (
          <>
            <label>
              New password
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                minLength={10}
                autoFocus
              />
            </label>
            <p className="hint">At least 10 characters, with an uppercase letter, lowercase letter, digit, and special character.</p>
            <label>
              Confirm password
              <input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} required />
            </label>
            <button type="submit" disabled={loading}>
              {loading ? "Saving..." : "Set password"}
            </button>
          </>
        )}
      </form>
    </AuthLayout>
  );
}
