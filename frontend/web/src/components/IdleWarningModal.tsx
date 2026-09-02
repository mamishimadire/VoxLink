import { Clock } from "lucide-react";

export function IdleWarningModal({ secondsLeft, onStaySignedIn }: { secondsLeft: number; onStaySignedIn: () => void }) {
  const minutes = Math.floor(secondsLeft / 60);
  const seconds = secondsLeft % 60;

  return (
    <div className="modal-overlay">
      <div className="modal-card">
        <div className="modal-header">
          <span>Still there?</span>
        </div>
        <div className="modal-body">
          <Clock size={36} color="#c9c7d6" />
          <div className="modal-text">
            You've been inactive. For your security, you'll be signed out in{" "}
            <strong>
              {minutes}:{seconds.toString().padStart(2, "0")}
            </strong>
            .
          </div>
          <div className="modal-actions">
            <button type="button" className="modal-btn-confirm" onClick={onStaySignedIn}>
              Stay signed in
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
