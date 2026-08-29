import type { ReactNode } from "react";

export function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <div className="auth-shell">
      <div className="auth-form-pane">
        <div className="auth-form-inner">{children}</div>
      </div>
      <div className="auth-illustration-pane">
        <AuthIllustration />
      </div>
    </div>
  );
}

const PHONE_ICON_PATH =
  "M6.6 10.8c1.4 2.8 3.8 5.2 6.6 6.6l2.2-2.2c.3-.3.7-.4 1-.2 1.1.4 2.3.6 3.6.6.6 0 1 .4 1 1V20c0 .6-.4 1-1 1C10.6 21 3 13.4 3 4c0-.6.4-1 1-1h3.5c.6 0 1 .4 1 1 0 1.2.2 2.4.6 3.5.1.4 0 .8-.2 1L6.6 10.8z";

function AuthIllustration() {
  return (
    <div className="auth-mock-scene">
      <div className="auth-hero-laptop">
        <div className="auth-hero-laptop-tilt">
          <div className="auth-hero-laptop-screen">
            <div className="auth-hero-laptop-cam" />
            <div className="auth-hero-app-top">
              <div className="auth-hero-app-tabs">
                <div className="auth-hero-app-tab" />
                <div className="auth-hero-app-tab" />
                <div className="auth-hero-app-tab" />
              </div>
              <div className="auth-hero-app-title">Calls</div>
              <div className="auth-hero-app-status">
                <span className="auth-hero-status-dot" />
                Connected
              </div>
            </div>
            <div className="auth-hero-app-body">
              <div className="auth-hero-calls-list">
                {[
                  { initials: "AT", time: "00:42", active: true },
                  { initials: "MK", time: "09:14", active: false },
                  { initials: "SD", time: "Yesterday", active: false },
                  { initials: "RN", time: "Yesterday", active: false },
                ].map((row, i) => (
                  <div className={row.active ? "auth-hero-call-row active" : "auth-hero-call-row"} key={i}>
                    <div className="auth-hero-call-avatar">{row.initials}</div>
                    <div className="auth-hero-call-meta">
                      <div className="auth-hero-call-name" />
                      <div className="auth-hero-call-sub" />
                    </div>
                    <div className="auth-hero-call-time">{row.time}</div>
                  </div>
                ))}
              </div>
              <div className="auth-hero-dial-panel">
                <div className="auth-hero-dial-grid">
                  {Array.from({ length: 9 }).map((_, i) => (
                    <div className="auth-hero-dial-key" key={i} />
                  ))}
                </div>
                <div className="auth-hero-dial-call-btn">
                  <svg viewBox="0 0 24 24">
                    <path d={PHONE_ICON_PATH} />
                  </svg>
                </div>
              </div>
            </div>
          </div>
        </div>
        <div className="auth-hero-laptop-base" />
      </div>

      <div className="auth-hero-phone">
        <div className="auth-hero-phone-body">
          <div className="auth-hero-phone-screen">
            <div className="auth-hero-phone-notch" />
            <div className="auth-hero-incoming-label">Incoming call</div>
            <div className="auth-hero-avatar-wrap">
              <div className="auth-hero-pulse-ring" />
              <div className="auth-hero-pulse-ring r2" />
              <div className="auth-hero-avatar">AT</div>
            </div>
            <div className="auth-hero-caller-name">Alex Turner</div>
            <div className="auth-hero-caller-sub">Mobile · VoxLink</div>
            <div className="auth-hero-call-actions">
              <div className="auth-hero-call-btn decline" aria-label="Decline call">
                <svg viewBox="0 0 24 24" style={{ transform: "rotate(135deg)" }}>
                  <path d={PHONE_ICON_PATH} />
                </svg>
              </div>
              <div className="auth-hero-call-btn accept" aria-label="Accept call">
                <svg viewBox="0 0 24 24">
                  <path d={PHONE_ICON_PATH} />
                </svg>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
