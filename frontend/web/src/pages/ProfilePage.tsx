import { useEffect, useState, type FormEvent } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";

interface Profile {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  country: string | null;
  region: string | null;
  gender: string | null;
  photoUrl: string | null;
}

export function ProfilePage({ onBack }: { onBack: () => void }) {
  const { token } = useAuth();
  const [profile, setProfile] = useState<Profile | null>(null);
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [country, setCountry] = useState("");
  const [region, setRegion] = useState("");
  const [gender, setGender] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  function load() {
    api
      .get<Profile>("/api/users/me", token)
      .then((p) => {
        setProfile(p);
        setFirstName(p.firstName);
        setLastName(p.lastName);
        setCountry(p.country ?? "");
        setRegion(p.region ?? "");
        setGender(p.gender ?? "");
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load profile."));
  }

  useEffect(() => {
    load();
  }, []);

  async function handleSave(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setMessage(null);
    setSaving(true);
    try {
      await api.put(
        "/api/users/me",
        { firstName, lastName, country: country || null, region: region || null, gender: gender || null },
        token,
      );
      setMessage("Profile updated.");
      load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to update profile.");
    } finally {
      setSaving(false);
    }
  }

  async function handleUploadPhoto() {
    if (!file) return;
    setError(null);
    setMessage(null);
    try {
      const form = new FormData();
      form.append("file", file);
      await api.postForm("/api/users/me/photo", form, token);
      setMessage("Photo updated.");
      setFile(null);
      load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to upload photo.");
    }
  }

  return (
    <div>
      <button type="button" className="link-btn" onClick={onBack} style={{ marginBottom: 16 }}>
        ← Back
      </button>
      <h2>My profile</h2>
      {message && <div className="success">{message}</div>}
      {error && <div className="error">{error}</div>}

      <div className="card inline-card">
        <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
          <div
            style={{
              width: 64,
              height: 64,
              borderRadius: "50%",
              background: "var(--surface)",
              border: "1px solid var(--border)",
              backgroundImage: profile?.photoUrl ? `url(${profile.photoUrl})` : undefined,
              backgroundSize: "cover",
              backgroundPosition: "center",
              flexShrink: 0,
            }}
          />
          <div style={{ flex: 1 }}>
            <input type="file" accept="image/*" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
          </div>
          <button type="button" disabled={!file} onClick={handleUploadPhoto}>
            Upload photo
          </button>
        </div>
      </div>

      <form className="card inline-card" onSubmit={handleSave}>
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
          <input value={profile?.email ?? ""} disabled />
        </label>

        <div className="row">
          <label>
            Country
            <input value={country} onChange={(e) => setCountry(e.target.value)} placeholder="e.g. South Africa" />
          </label>
          <label>
            Province / region
            <input value={region} onChange={(e) => setRegion(e.target.value)} placeholder="e.g. Gauteng" />
          </label>
        </div>

        <label>
          Gender
          <select value={gender} onChange={(e) => setGender(e.target.value)}>
            <option value="">Prefer not to say</option>
            <option value="female">Female</option>
            <option value="male">Male</option>
            <option value="other">Other</option>
          </select>
        </label>

        <button type="submit" disabled={saving}>
          {saving ? "Saving..." : "Save changes"}
        </button>
      </form>
    </div>
  );
}
