// Shared by everywhere the theme gets applied (dashboard load, Profile
// page, the softphone's own Settings screen) so "system" resolves the
// same way everywhere and there's one place to change that logic.

const CACHE_KEY = "voxlink_last_theme";

export function applyTheme(theme: string) {
  const effective = theme === "system" ? (systemPrefersDark() ? "dark" : "light") : theme;
  document.documentElement.setAttribute("data-theme", effective);
}

// The user's real preference lives on their account (server-side), but that
// requires a network round trip to read. Without a local hint, every fresh
// login briefly paints the wrong (default dark) theme before the fetch
// resolves, which reads to the user as "my setting didn't stick." This cache
// is only ever a same-device paint hint — the server fetch that follows
// still overwrites it with the authoritative value.
export function cacheTheme(theme: string) {
  try {
    localStorage.setItem(CACHE_KEY, theme);
  } catch {
    // Private-browsing/storage-blocked: falls back to the default dark
    // paint until the server fetch resolves, same as before this existed.
  }
}

export function loadCachedTheme(): string | null {
  try {
    return localStorage.getItem(CACHE_KEY);
  } catch {
    return null;
  }
}

// Called on sign-out so the theme hint never leaks into the next person's
// session on this same device/browser.
export function clearCachedTheme() {
  try {
    localStorage.removeItem(CACHE_KEY);
  } catch {
    // nothing to clear
  }
}

function systemPrefersDark(): boolean {
  return window.matchMedia("(prefers-color-scheme: dark)").matches;
}

/**
 * Calls `onChange` whenever the OS/browser's light/dark preference flips —
 * only matters while the user's saved theme is "system"; the caller is
 * responsible for checking that before reacting.
 */
export function watchSystemTheme(onChange: () => void): () => void {
  const mq = window.matchMedia("(prefers-color-scheme: dark)");
  mq.addEventListener("change", onChange);
  return () => mq.removeEventListener("change", onChange);
}
