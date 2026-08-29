import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import "./index.css";
import { App } from "./App.tsx";
import { AuthProvider } from "./auth/AuthContext";
import { applyTheme, loadCachedTheme } from "./theme";

// Paint the last-known theme immediately, before the authenticated fetch
// that would otherwise be the first thing to apply it. Avoids a flash of
// the default dark theme on every login that made it look like a saved
// light-mode preference had been forgotten.
const cachedTheme = loadCachedTheme();
if (cachedTheme) applyTheme(cachedTheme);

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <App />
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
);
