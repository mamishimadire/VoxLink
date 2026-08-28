import { createContext, useContext, useState, type ReactNode } from "react";
import { decodeToken, getRole, isBusinessOwner, isPlatformAdmin, type VoxLinkClaims } from "./jwt";

interface AuthState {
  token: string | null;
  claims: VoxLinkClaims | null;
  role: string | null;
  isPlatformAdmin: boolean;
  isBusinessOwner: boolean;
  login: (token: string) => void;
  logout: () => void;
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

  const logout = () => {
    sessionStorage.removeItem(STORAGE_KEY);
    setToken(null);
  };

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
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
