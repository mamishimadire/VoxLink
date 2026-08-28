import { useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { AuthLayout } from "../components/AuthLayout";

interface AuthResponse {
  token: string;
}

interface Plan {
  id: string;
  name: string;
  description: string;
  monthlyPrice: number;
  isCustomQuote: boolean;
}

export function RegisterPage() {
  const [companyName, setCompanyName] = useState("");
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [plans, setPlans] = useState<Plan[]>([]);
  const [planId, setPlanId] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const { login } = useAuth();
  const navigate = useNavigate();

  useEffect(() => {
    api
      .get<Plan[]>("/api/plans")
      .then((list) => {
        setPlans(list);
        setPlanId(list[0]?.id ?? "");
      })
      .catch(() => setError("Failed to load plans."));
  }, []);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const res = await api.post<AuthResponse>("/api/auth/register-company", {
        companyName,
        adminFirstName: firstName,
        adminLastName: lastName,
        adminEmail: email,
        password,
        planId,
      });
      login(res.token);
      navigate("/");
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Something went wrong.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <AuthLayout>
      <form onSubmit={handleSubmit}>
        <h1>VoxLink</h1>
        <p className="subtitle">Register your company</p>
        <p className="hint">
          This creates your company and makes you its owner. After you select a category below, you'll pay the
          platform fee and upload proof of payment — a VoxLink admin then approves your account.
        </p>

        {error && <div className="error">{error}</div>}

        <label>
          Company name
          <input value={companyName} onChange={(e) => setCompanyName(e.target.value)} required autoFocus />
        </label>

        <div className="row">
          <label>
            First name
            <input value={firstName} onChange={(e) => setFirstName(e.target.value)} required />
          </label>
          <label>
            Last name
            <input value={lastName} onChange={(e) => setLastName(e.target.value)} required />
          </label>
        </div>

        <label>
          Email
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
        </label>

        <label>
          Password
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            minLength={10}
          />
        </label>
        <p className="hint">At least 10 characters, with an uppercase letter, lowercase letter, digit, and special character.</p>

        <label>
          Category
          <select value={planId} onChange={(e) => setPlanId(e.target.value)} required>
            {plans.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name} — {p.isCustomQuote ? "custom quote" : `R${p.monthlyPrice}/mo`} ({p.description})
              </option>
            ))}
          </select>
        </label>

        <button type="submit" disabled={loading || !planId}>
          {loading ? "Creating..." : "Create company"}
        </button>

        <div className="links">
          <Link to="/login">Back to sign in</Link>
        </div>
      </form>
    </AuthLayout>
  );
}
