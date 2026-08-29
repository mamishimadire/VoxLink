// Shared by everywhere the theme gets applied (dashboard load, Profile
// page, the softphone's own Settings screen) so "system" resolves the
// same way everywhere and there's one place to change that logic.

export function applyTheme(theme: string) {
  const effective = theme === "system" ? (systemPrefersDark() ? "dark" : "light") : theme;
  document.documentElement.setAttribute("data-theme", effective);
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
