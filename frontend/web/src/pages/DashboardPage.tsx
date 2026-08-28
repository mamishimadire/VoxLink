import { useEffect, useState } from "react";
import { api } from "../api/client";
import { useAuth } from "../auth/AuthContext";
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
import { LogoutConfirmModal } from "../components/LogoutConfirmModal";

interface Company {
  status: string;
  isInternal: boolean;
}

type PlatformTab = "clients" | "pricing" | "internal" | "dialer" | "billing" | "invoices" | "agreements" | "analytics";
type ClientTab = "billing" | "invoices" | "team" | "dialer";

export function DashboardPage() {
  const { claims, isPlatformAdmin, logout, token } = useAuth();
  const [platformTab, setPlatformTab] = useState<PlatformTab>("clients");
  const [clientTab, setClientTab] = useState<ClientTab | null>(null);
  const [company, setCompany] = useState<Company | null>(null);
  const [showProfile, setShowProfile] = useState(false);
  const [logoutConfirm, setLogoutConfirm] = useState(false);
  const [photoUrl, setPhotoUrl] = useState<string | null>(null);

  function refreshPhoto() {
    api
      .get<{ photoUrl: string | null }>("/api/users/me", token)
      .then((p) => setPhotoUrl(p.photoUrl))
      .catch(() => {});
  }

  useEffect(() => {
    api.get<Company>("/api/companies/me", token).then((c) => {
      setCompany(c);
      // VoxLink's own internal team has no billing/agreement/license of its
      // own to manage — land them straight on Team instead.
      setClientTab(c.isInternal ? "team" : "billing");
    });
    refreshPhoto();
  }, []);

  if (!isPlatformAdmin && company?.status === "pending") {
    return <OnboardingPage />;
  }

  const showBillingTab = !isPlatformAdmin && company !== null && !company.isInternal;

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
          </nav>
        ) : (
          <nav className="tabs">
            {showBillingTab && (
              <button className={clientTab === "billing" ? "tab active" : "tab"} onClick={() => setClientTab("billing")}>
                Billing
              </button>
            )}
            {showBillingTab && (
              <button className={clientTab === "invoices" ? "tab active" : "tab"} onClick={() => setClientTab("invoices")}>
                Invoices
              </button>
            )}
            <button className={clientTab === "team" ? "tab active" : "tab"} onClick={() => setClientTab("team")}>
              Team
            </button>
            <button className={clientTab === "dialer" ? "tab active" : "tab"} onClick={() => setClientTab("dialer")}>
              Phone
            </button>
          </nav>
        )}
        <div className="topbar-right">
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
          ) : (
            <CompanyView />
          )
        ) : showBillingTab && clientTab === "billing" ? (
          <BillingView />
        ) : showBillingTab && clientTab === "invoices" ? (
          <InvoicesView />
        ) : clientTab === "dialer" ? (
          <DialerPage />
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
