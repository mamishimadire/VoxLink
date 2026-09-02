import { useCallback, useEffect, useRef, useState } from "react";

// 30 minutes of no interaction on this tab signs the user out automatically;
// the last 5 of those show a warning first so it isn't a surprise.
const WARNING_AFTER_MS = 25 * 60 * 1000;
const LOGOUT_AFTER_MS = 30 * 60 * 1000;
const ACTIVITY_EVENTS = ["mousemove", "mousedown", "keydown", "touchstart", "scroll", "wheel"] as const;

/**
 * Returns the seconds left before an idle auto-logout while the warning is
 * showing (null otherwise), and a `stayLoggedIn` action for the warning's
 * "Stay signed in" button. Once the warning appears, only that explicit
 * action resets the clock — passive mouse movement while it's up does NOT
 * silently dismiss it, so a stray cursor nudge can't defeat the timeout.
 */
export function useIdleLogout(active: boolean, onIdleLogout: () => void) {
  const [secondsUntilLogout, setSecondsUntilLogout] = useState<number | null>(null);
  const scheduleRef = useRef<() => void>(() => {});
  const onIdleLogoutRef = useRef(onIdleLogout);
  onIdleLogoutRef.current = onIdleLogout;

  useEffect(() => {
    if (!active) {
      setSecondsUntilLogout(null);
      return;
    }

    let warningTimer: number;
    let logoutTimer: number;
    let countdownInterval: number;
    let warningShowing = false;

    function clearTimers() {
      window.clearTimeout(warningTimer);
      window.clearTimeout(logoutTimer);
      window.clearInterval(countdownInterval);
    }

    function schedule() {
      clearTimers();
      warningShowing = false;
      setSecondsUntilLogout(null);

      warningTimer = window.setTimeout(() => {
        warningShowing = true;
        let remaining = Math.round((LOGOUT_AFTER_MS - WARNING_AFTER_MS) / 1000);
        setSecondsUntilLogout(remaining);
        countdownInterval = window.setInterval(() => {
          remaining -= 1;
          setSecondsUntilLogout(Math.max(remaining, 0));
        }, 1000);
      }, WARNING_AFTER_MS);

      logoutTimer = window.setTimeout(() => {
        clearTimers();
        onIdleLogoutRef.current();
      }, LOGOUT_AFTER_MS);
    }

    function handleActivity() {
      if (warningShowing) return;
      schedule();
    }

    scheduleRef.current = schedule;
    schedule();
    ACTIVITY_EVENTS.forEach((event) => window.addEventListener(event, handleActivity, { passive: true }));

    return () => {
      clearTimers();
      ACTIVITY_EVENTS.forEach((event) => window.removeEventListener(event, handleActivity));
    };
  }, [active]);

  const stayLoggedIn = useCallback(() => {
    scheduleRef.current();
  }, []);

  return { secondsUntilLogout, stayLoggedIn };
}
