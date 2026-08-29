import { useEffect, useState } from "react";
import { Bell } from "lucide-react";
import { api } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { useAutoRefresh } from "../hooks/useAutoRefresh";

interface NotificationItem {
  id: string;
  type: string;
  message: string;
}

export function NotificationBell({ onNavigate }: { onNavigate: (type: string) => void }) {
  const { token } = useAuth();
  const [items, setItems] = useState<NotificationItem[]>([]);
  const [open, setOpen] = useState(false);

  function load() {
    api
      .get<{ items: NotificationItem[] }>("/api/notifications", token)
      .then((res) => setItems(res.items))
      .catch(() => {});
  }

  useEffect(load, []);
  // Someone else approving/rejecting the same thing, or a new item showing
  // up (another admin's proposal, a new client signup), shouldn't wait for
  // this user to happen to reopen the page.
  useAutoRefresh(load, 20000);

  function handleSelect(item: NotificationItem) {
    setOpen(false);
    onNavigate(item.type);
  }

  return (
    <div style={{ position: "relative" }}>
      <button
        type="button"
        className="link-btn"
        onClick={() => setOpen((v) => !v)}
        style={{ position: "relative", display: "flex", alignItems: "center" }}
        aria-label="Notifications"
      >
        <Bell size={18} />
        {items.length > 0 && <span className="notification-badge">{items.length}</span>}
      </button>

      {open && (
        <>
          <div className="notification-backdrop" onClick={() => setOpen(false)} />
          <div className="notification-dropdown">
            <div className="notification-dropdown-header">Needs your attention</div>
            {items.length === 0 ? (
              <div className="notification-empty">Nothing pending right now.</div>
            ) : (
              items.map((item) => (
                <button type="button" key={item.id} className="notification-item" onClick={() => handleSelect(item)}>
                  {item.message}
                </button>
              ))
            )}
          </div>
        </>
      )}
    </div>
  );
}
