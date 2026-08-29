import { useEffect, useRef } from "react";

/**
 * Keeps a screen's data from going stale when someone else changes
 * something in the database — there's no live push channel (the frontend
 * never talks to Supabase directly, only through the backend, which is
 * where tenant isolation is enforced), so this re-runs the given fetch on
 * an interval, and immediately whenever the tab regains focus/visibility
 * (the common case: switch away, someone else changes something, switch
 * back — no need to wait for the next interval tick).
 *
 * Does not call `callback` on mount — pair with the screen's own existing
 * `useEffect(() => { load(); }, [])` for the initial fetch.
 */
export function useAutoRefresh(callback: () => void, intervalMs: number) {
  const savedCallback = useRef(callback);
  savedCallback.current = callback;

  useEffect(() => {
    const tick = () => savedCallback.current();
    const interval = window.setInterval(tick, intervalMs);

    function handleVisibility() {
      if (document.visibilityState === "visible") tick();
    }

    window.addEventListener("focus", tick);
    document.addEventListener("visibilitychange", handleVisibility);

    return () => {
      window.clearInterval(interval);
      window.removeEventListener("focus", tick);
      document.removeEventListener("visibilitychange", handleVisibility);
    };
  }, [intervalMs]);
}
