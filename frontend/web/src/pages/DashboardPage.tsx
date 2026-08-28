import { useEffect, useState } from "react";
import { api } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { PlatformAdminView } from "./PlatformAdminView";
import { CompanyView } from "./CompanyView";
import { OnboardingPage } from "./OnboardingPage";
import { PricingView } from "./PricingView";
import { BillingView } from "./BillingView";
import { DialerPage } from "./DialerPage";
import { ProfilePage } from "./ProfilePage";
import { LogoutConfirmModal } from "../components/LogoutConfirmModal";

interface Company {
  status: string;
  isInternal: boolean;
}

type PlatformTab = "clients" | "pricing" | "internal";
type ClientTab = "billing" | "team" | "dialer";

export function DashboardPage() {
  const { claims, isPlatformAdmin, logout, token } = useAuth();
  const [platformTab, setPlatformTab] = useState<PlatformTab>("clients");
  const [clientTab, setClientTab] = useState<ClientTab | null>(null);
  const [company, setCompany] = useState<Company | null>(null);
  const [showProfile, setShowProfile] = useState(false);
  const [logoutConfirm, setLogoutConfirm] = useState(false);

  useEffect(() => {
    api.get<Company>("/api/companies/me", token).then((c) => {
      setCompany(c);
      // VoxLink's own internal team has no billing/agreement/license of its
      // own to manage — land them straight on Team instead.
      setClientTab(c.isInternal ? "team" : "billing");
    });
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
          </nav>
        ) : (
          <nav className="tabs">
            {showBillingTab && (
              <button className={clientTab === "billing" ? "tab active" : "tab"} onClick={() => setClientTab("billing")}>
                Billing
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
          <button className="link-btn" onClick={() => setShowProfile(true)}>
            {claims?.email}
          </button>
          <button className="link-btn" onClick={() => setLogoutConfirm(true)}>
            Sign out
          </button>
        </div>
      </header>

      <main className={clientTab === "dialer" && !isPlatformAdmin && !showProfile ? "content content-full" : "content"}>
        {showProfile ? (
          <ProfilePage onBack={() => setShowProfile(false)} />
        ) : isPlatformAdmin ? (
          platformTab === "clients" ? (
            <PlatformAdminView />
          ) : platformTab === "pricing" ? (
            <PricingView />
          ) : (
            <CompanyView />
          )
        ) : showBillingTab && clientTab === "billing" ? (
          <BillingView />
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
