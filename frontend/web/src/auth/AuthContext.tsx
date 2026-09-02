import { createContext, useContext, useState, type ReactNode } from "react";
import { decodeToken, getRole, isBusinessOwner, isPlatformAdmin, type VoxLinkClaims } from "./jwt";
import { clearCachedTheme } from "../theme";
import { api } from "../api/client";
import { useIdleLogout } from "./useIdleLogout";
import { IdleWarningModal } from "../components/IdleWarningModal";

interface AuthState {
  token: string | null;
  claims: VoxLinkClaims | null;
  role: string | null;
  isPlatformAdmin: boolean;
  isBusinessOwner: boolean;
  login: (token: string) => void;
  logout: (reason?: "idle_timeout") => void;
}

const AuthContext = createContext<AuthState | null>(null);

const STORAGE_KEY = "voxlink_token";

export function AuthProvider({ children }: { children: ReactNode }) {
  // sessionStorage, not localStorage: closing the browser/tab must require
  // logging in again, rather than silently staying signed in indefinitely.
  const [token, setToken] = useState<string | null>(() => sessionStorage.getItem(STORAGE_KEY));

  const claims = token ? decodeToken(token) : null;

  const login = (newToken: string) => {
    sessionStorage.setItem(STORAGE_KEY, newToken);
    setToken(newToken);
  };

  const logout = (reason?: "idle_timeout") => {
    // Fire-and-forget, purely so "signed out" lands in the audit trail — the
    // JWT itself is stateless, so there's nothing to actually revoke, and a
    // failed request here must never block the user from signing out.
    if (token) {
      api.post("/api/auth/logout", { reason }, token).catch(() => {});
    }
    sessionStorage.removeItem(STORAGE_KEY);
    setToken(null);
    // Theme is per-user, not per-browser — don't let it leak into the next
    // person's login/session on this same device.
    document.documentElement.removeAttribute("data-theme");
    clearCachedTheme();
  };

  // Signs out automatically after 30 minutes with no interaction anywhere in
  // the app (not just the phone), with a 5-minute warning first — runs for
  // the whole authenticated session regardless of which page is showing.
  const { secondsUntilLogout, stayLoggedIn } = useIdleLogout(token !== null, () => logout("idle_timeout"));

  return (
    <AuthContext.Provider
      value={{
        token,
        claims,
        role: claims ? getRole(claims) : null,
        isPlatformAdmin: claims ? isPlatformAdmin(claims) : false,
        isBusinessOwner: claims ? isBusinessOwner(claims) : false,
        login,
        logout,
      }}
    >
      {children}
      {secondsUntilLogout !== null && <IdleWarningModal secondsLeft={secondsUntilLogout} onStaySignedIn={stayLoggedIn} />}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
