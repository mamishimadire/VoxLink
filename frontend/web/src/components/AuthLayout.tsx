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

function AuthIllustration() {
  return (
    <div className="auth-mock-scene">
      <div className="auth-mock-laptop">
        <div className="auth-mock-laptop-screen">
          <div className="auth-mock-laptop-sidebar">
            <div className="auth-mock-laptop-title">Calls</div>
            {[0, 1, 2, 3].map((i) => (
              <div className="auth-mock-call-row" key={i}>
                <div className="auth-mock-call-avatar" />
                <div className="auth-mock-call-lines">
                  <div className="auth-mock-call-name" style={{ width: `${60 + i * 8}%` }} />
                  <div className="auth-mock-call-sub" />
                </div>
              </div>
            ))}
          </div>
          <div className="auth-mock-laptop-main">
            <div className="auth-mock-dialpad">
              {Array.from({ length: 9 }).map((_, i) => (
                <div key={i} />
              ))}
            </div>
          </div>
        </div>
      </div>

      <div className="auth-mock-phone">
        <div className="auth-mock-phone-screen">
          <div>
            <div className="auth-mock-phone-label">Incoming call</div>
            <div className="auth-mock-phone-name">VoxLink</div>
          </div>
          <div className="auth-mock-phone-actions">
            <div className="auth-mock-phone-btn decline">✕</div>
            <div className="auth-mock-phone-btn accept">✓</div>
          </div>
        </div>
      </div>
    </div>
  );
}
