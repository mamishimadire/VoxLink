import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/client";
import { AuthLayout } from "../components/AuthLayout";

export function ForgotPasswordPage() {
  const [email, setEmail] = useState("");
  const [sent, setSent] = useState(false);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setLoading(true);
    try {
      await api.post("/api/auth/forgot-password", { email });
    } finally {
      setLoading(false);
      setSent(true);
    }
  }

  return (
    <AuthLayout>
      <form onSubmit={handleSubmit}>
        <h1>VoxLink</h1>
        <p className="subtitle">Reset your password</p>

        {sent ? (
          <p className="hint">If that email is registered, a reset link has been sent to it.</p>
        ) : (
          <>
            <label>
              Email
              <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoFocus />
            </label>
            <button type="submit" disabled={loading}>
              {loading ? "Sending..." : "Send reset link"}
            </button>
          </>
        )}

        <div className="links">
          <Link to="/login">Back to sign in</Link>
        </div>
      </form>
    </AuthLayout>
  );
}
