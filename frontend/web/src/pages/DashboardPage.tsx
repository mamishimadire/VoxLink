import { useEffect, useRef, useState } from "react";
import { api } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { applyTheme, cacheTheme, watchSystemTheme } from "../theme";
import { PlatformAdminView } from "./PlatformAdminView";
import { CompanyView } from "./CompanyView";
import { OnboardingPage } from "./OnboardingPage";
import { PricingView } from "./PricingView";
import { BillingView } from "./BillingView";
import { InvoicesView } from "./InvoicesView";
import { AgreementsView } from "./AgreementsView";
import { RevenueAnalyticsView } from "./RevenueAnalyticsView";
import { DialerPage } from "./DialerPage";
import { ProfilePage } from "./ProfilePage";
import { AuditLogView } from "./AuditLogView";
import { ApprovalsView } from "./ApprovalsView";
import { LogoutConfirmModal } from "../components/LogoutConfirmModal";
import { NotificationBell } from "../components/NotificationBell";

interface Company {
  status: string;
  isInternal: boolean;
}

type PlatformTab =
  | "clients"
  | "pricing"
  | "internal"
  | "dialer"
  | "billing"
  | "invoices"
  | "agreements"
  | "analytics"
  | "auditlog";
type ClientTab = "billing" | "invoices" | "team" | "dialer" | "auditlog" | "approvals";

export function DashboardPage() {
  const { claims, role, isPlatformAdmin, logout, token } = useAuth();
  const [platformTab, setPlatformTab] = useState<PlatformTab>("clients");
  const [clientTab, setClientTab] = useState<ClientTab | null>(null);
  const [company, setCompany] = useState<Company | null>(null);
  const [showProfile, setShowProfile] = useState(false);
  const [logoutConfirm, setLogoutConfirm] = useState(false);
  const [photoUrl, setPhotoUrl] = useState<string | null>(null);
  const themeRef = useRef("dark");

  function refreshPhoto() {
    api
      .get<{ photoUrl: string | null; theme: string }>("/api/users/me", token)
      .then((p) => {
        setPhotoUrl(p.photoUrl);
        // Per-user theme, not per-device — applied globally here so every
        // page (not just the profile page) reflects the signed-in user's
        // own saved preference.
        themeRef.current = p.theme;
        applyTheme(p.theme);
        cacheTheme(p.theme);
      })
      .catch(() => {});
  }

  // Only matters while the saved preference is "system" — re-resolves
  // light/dark if the OS/browser preference flips while the app is open.
  useEffect(() => watchSystemTheme(() => applyTheme(themeRef.current)), []);

  const isEmployee = role === "employee";

  useEffect(() => {
    api.get<Company>("/api/companies/me", token).then((c) => {
      setCompany(c);
      // An employee only ever has the dialer — everything else here (team,
      // billing, invoices) is a management view. VoxLink's own internal
      // team has no billing/agreement/license of its own to manage, so it
      // still lands on Team instead of Billing.
      setClientTab(isEmployee ? "dialer" : c.isInternal ? "team" : "billing");
    });
    refreshPhoto();
  }, []);

  if (!isPlatformAdmin && company?.status === "pending") {
    return <OnboardingPage />;
  }

  const showBillingTab = !isPlatformAdmin && !isEmployee && company !== null && !company.isInternal;
  // Invoices are financial/legal documents — a manager can view usage
  // analytics for oversight, but invoices stay owner/admin only.
  const showInvoicesTab = showBillingTab && (role === "owner" || role === "admin");
  // Only a VoxLink-internal manager reviews revoke/invoice-generation
  // requests — a client company's own manager has no such authority over
  // anyone's account, including their own.
  const isInternalManager = role === "manager" && company?.isInternal === true;

  // Maps a notification's type to wherever that action actually lives —
  // "user_approval" means different tabs depending on whether this caller
  // sees the platform-admin nav or the client nav, and "price_change" only
  // ever shows for VoxLink's own business owner (platform nav), so there's
  // no ambiguity in practice.
  function handleNotificationNavigate(type: string) {
    if (type === "password_expired" || type === "password_expiring") {
      setShowProfile(true);
      return;
    }
    if (isPlatformAdmin) {
      if (type === "user_approval" || type === "user_approval_pending") setPlatformTab("internal");
      else if (type === "price_change" || type === "price_change_pending") setPlatformTab("pricing");
      else if (
        type === "company_approval" ||
        type === "payment_verification" ||
        type === "revoke_approval" ||
        type === "revoke_pending" ||
        type === "invoice_generation_pending" ||
        type === "license_change_approval" ||
        type === "license_change_pending"
      )
        setPlatformTab("clients");
    } else {
      if (type === "user_approval" || type === "user_approval_pending") setClientTab("team");
      else if (type === "agreement_unsigned") setClientTab("billing");
      else if (type === "revoke_approval" || type === "invoice_generation_approval" || type === "license_change_approval")
        setClientTab("approvals");
    }
  }

  return (
    <div className="dashboard">
      <header className="topbar">
        <span className="brand">VoxLink</span>
        {isPlatformAdmin ? (
          <nav className="tabs">
            <button className={platformTab === "clients" ? "tab active" : "tab"} onClick={() => setPlatformTab("clients")}>
              Clients
            </button>
            <button className={platformTab === "pricing" ? "tab active" : "tab"} onClick={() => setPlatformTab("pricing")}>
              Pricing
            </button>
            <button className={platformTab === "internal" ? "tab active" : "tab"} onClick={() => setPlatformTab("internal")}>
              Internal team
            </button>
            <button className={platformTab === "dialer" ? "tab active" : "tab"} onClick={() => setPlatformTab("dialer")}>
              Phone
            </button>
            <button className={platformTab === "billing" ? "tab active" : "tab"} onClick={() => setPlatformTab("billing")}>
              Billing
            </button>
            <button className={platformTab === "invoices" ? "tab active" : "tab"} onClick={() => setPlatformTab("invoices")}>
              Invoices
            </button>
            <button className={platformTab === "agreements" ? "tab active" : "tab"} onClick={() => setPlatformTab("agreements")}>
              Agreements
            </button>
            <button className={platformTab === "analytics" ? "tab active" : "tab"} onClick={() => setPlatformTab("analytics")}>
              Analytics
            </button>
            {role === "owner" && (
              <button className={platformTab === "auditlog" ? "tab active" : "tab"} onClick={() => setPlatformTab("auditlog")}>
                Audit log
              </button>
            )}
          </nav>
        ) : (
          <nav className="tabs">
            {showBillingTab && (
              <button className={clientTab === "billing" ? "tab active" : "tab"} onClick={() => setClientTab("billing")}>
                Billing
              </button>
            )}
            {showInvoicesTab && (
              <button className={clientTab === "invoices" ? "tab active" : "tab"} onClick={() => setClientTab("invoices")}>
                Invoices
              </button>
            )}
            {!isEmployee && (
              <button className={clientTab === "team" ? "tab active" : "tab"} onClick={() => setClientTab("team")}>
                Team
              </button>
            )}
            <button className={clientTab === "dialer" ? "tab active" : "tab"} onClick={() => setClientTab("dialer")}>
              Phone
            </button>
            {isInternalManager && (
              <button className={clientTab === "approvals" ? "tab active" : "tab"} onClick={() => setClientTab("approvals")}>
                Approvals
              </button>
            )}
            {role === "owner" && (
              <button className={clientTab === "auditlog" ? "tab active" : "tab"} onClick={() => setClientTab("auditlog")}>
                Audit log
              </button>
            )}
          </nav>
        )}
        <div className="topbar-right">
          <NotificationBell onNavigate={handleNotificationNavigate} />
          <button
            className="link-btn"
            onClick={() => setShowProfile(true)}
            style={{ display: "flex", alignItems: "center", gap: 8 }}
          >
            <span
              style={{
                width: 24,
                height: 24,
                borderRadius: "50%",
                background: "var(--surface)",
                border: "1px solid var(--border)",
                backgroundImage: photoUrl ? `url(${photoUrl})` : undefined,
                backgroundSize: "cover",
                backgroundPosition: "center",
                flexShrink: 0,
              }}
            />
            {claims?.email}
          </button>
          <button className="link-btn" onClick={() => setLogoutConfirm(true)}>
            Sign out
          </button>
        </div>
      </header>

      <main
        className={
          ((isPlatformAdmin && platformTab === "dialer") || (!isPlatformAdmin && clientTab === "dialer")) &&
          !showProfile
            ? "content content-full"
            : "content"
        }
      >
        {showProfile ? (
          <ProfilePage
            onBack={() => {
              setShowProfile(false);
              refreshPhoto();
            }}
          />
        ) : isPlatformAdmin ? (
          platformTab === "clients" ? (
            <PlatformAdminView />
          ) : platformTab === "pricing" ? (
            <PricingView />
          ) : platformTab === "dialer" ? (
            <DialerPage />
          ) : platformTab === "billing" ? (
            <BillingView />
          ) : platformTab === "invoices" ? (
            <InvoicesView />
          ) : platformTab === "agreements" ? (
            <AgreementsView />
          ) : platformTab === "analytics" ? (
            <RevenueAnalyticsView />
          ) : platformTab === "auditlog" ? (
            <AuditLogView />
          ) : (
            <CompanyView />
          )
        ) : showBillingTab && clientTab === "billing" ? (
          <BillingView />
        ) : showInvoicesTab && clientTab === "invoices" ? (
          <InvoicesView />
        ) : clientTab === "dialer" ? (
          <DialerPage />
        ) : isInternalManager && clientTab === "approvals" ? (
          <ApprovalsView />
        ) : clientTab === "auditlog" ? (
          <AuditLogView />
        ) : (
          <CompanyView />
        )}
      </main>

      {logoutConfirm && (
        <LogoutConfirmModal onCancel={() => setLogoutConfirm(false)} onConfirm={logout} />
      )}
    </div>
  );
}
