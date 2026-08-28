import { X, Power } from "lucide-react";

export function LogoutConfirmModal({ onCancel, onConfirm }: { onCancel: () => void; onConfirm: () => void }) {
  return (
    <div className="modal-overlay">
      <div className="modal-card">
        <div className="modal-header">
          <span>Logout</span>
          <X size={16} className="modal-muted-icon" onClick={onCancel} />
        </div>
        <div className="modal-body">
          <Power size={36} color="#c9c7d6" />
          <div className="modal-text">Are you sure you want to logout?</div>
          <div className="modal-actions">
            <button type="button" className="modal-btn-cancel" onClick={onCancel}>
              Cancel
            </button>
            <button type="button" className="modal-btn-confirm" onClick={onConfirm}>
              Confirm
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
