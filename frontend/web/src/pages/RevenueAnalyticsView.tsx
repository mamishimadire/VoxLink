import { useEffect, useState } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";

interface PeriodRow {
  label: string;
  clientMinutes: number;
  internalMinutes: number;
  clientRevenue: number;
  internalCost: number;
  atRisk: boolean;
}

interface RevenueCostAnalytics {
  monthly: PeriodRow[];
  yearly: PeriodRow[];
}

type View = "monthly" | "yearly";

const MONTH_NAMES = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function formatLabel(label: string, view: View) {
  if (view === "yearly") return label;
  const [year, month] = label.split("-");
  return `${MONTH_NAMES[Number(month) - 1]} ${year}`;
}

const CHART_HEIGHT = 220;
const BAR_WIDTH = 16;
const GROUP_GAP = 36;
const BAR_GAP = 4;

function BarChart({ rows, view }: { rows: PeriodRow[]; view: View }) {
  if (rows.length === 0) {
    return <p className="hint">No call data yet — the chart fills in as calls are made.</p>;
  }

  const maxValue = Math.max(1, ...rows.map((r) => Math.max(r.clientRevenue, r.internalCost)));
  const groupWidth = BAR_WIDTH * 2 + BAR_GAP + GROUP_GAP;
  const chartWidth = rows.length * groupWidth;

  return (
    <div style={{ overflowX: "auto" }}>
      <svg width={chartWidth} height={CHART_HEIGHT + 50} role="img" aria-label="Revenue vs cost bar chart">
        {rows.map((row, i) => {
          const x = i * groupWidth + GROUP_GAP / 2;
          const revenueHeight = (row.clientRevenue / maxValue) * CHART_HEIGHT;
          const costHeight = (row.internalCost / maxValue) * CHART_HEIGHT;
          return (
            <g key={row.label}>
              <rect
                x={x}
                y={CHART_HEIGHT - revenueHeight}
                width={BAR_WIDTH}
                height={revenueHeight}
                fill="#2ecc71"
                rx={2}
              >
                <title>{`${formatLabel(row.label, view)} — Revenue: R${row.clientRevenue.toFixed(2)}`}</title>
              </rect>
              <rect
                x={x + BAR_WIDTH + BAR_GAP}
                y={CHART_HEIGHT - costHeight}
                width={BAR_WIDTH}
                height={costHeight}
                fill={row.atRisk ? "#e74c3c" : "#f0a83f"}
                rx={2}
              >
                <title>{`${formatLabel(row.label, view)} — Cost: R${row.internalCost.toFixed(2)}`}</title>
              </rect>
              {row.atRisk && (
                <text x={x + BAR_WIDTH + BAR_GAP / 2} y={CHART_HEIGHT - Math.max(revenueHeight, costHeight) - 6} textAnchor="middle" fontSize="11" fill="#e74c3c">
                  ⚠
                </text>
              )}
              <text
                x={x + BAR_WIDTH}
                y={CHART_HEIGHT + 18}
                textAnchor="middle"
                fontSize="11"
                fill="var(--text-muted, #9aa4c2)"
                transform={view === "monthly" ? `rotate(-40 ${x + BAR_WIDTH} ${CHART_HEIGHT + 18})` : undefined}
              >
                {formatLabel(row.label, view)}
              </text>
            </g>
          );
        })}
      </svg>
      <div className="row" style={{ gap: 16, marginTop: 4 }}>
        <span className="hint">
          <span style={{ display: "inline-block", width: 10, height: 10, background: "#2ecc71", borderRadius: 2, marginRight: 6 }} />
          Revenue (client calls)
        </span>
        <span className="hint">
          <span style={{ display: "inline-block", width: 10, height: 10, background: "#f0a83f", borderRadius: 2, marginRight: 6 }} />
          Cost (internal calls)
        </span>
        <span className="hint">
          <span style={{ display: "inline-block", width: 10, height: 10, background: "#e74c3c", borderRadius: 2, marginRight: 6 }} />
          Cost — at risk
        </span>
      </div>
    </div>
  );
}

export function RevenueAnalyticsView() {
  const { token } = useAuth();
  const [data, setData] = useState<RevenueCostAnalytics | null>(null);
  const [view, setView] = useState<View>("monthly");
  const [error, setError] = useState<string | null>(null);
  const [fromPeriod, setFromPeriod] = useState("");
  const [toPeriod, setToPeriod] = useState("");

  useEffect(() => {
    api
      .get<RevenueCostAnalytics>("/api/platform/analytics/revenue-cost", token)
      .then((res) => {
        setData(res);
        if (res.monthly.length > 0) {
          setFromPeriod(res.monthly[0].label);
          setToPeriod(res.monthly[res.monthly.length - 1].label);
        }
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load analytics."));
  }, []);

  const allRows = data ? (view === "monthly" ? data.monthly : data.yearly) : [];
  const fromYear = fromPeriod.slice(0, 4);
  const toYear = toPeriod.slice(0, 4);
  const rows = allRows.filter((r) => {
    if (view === "monthly") {
      return (!fromPeriod || r.label >= fromPeriod) && (!toPeriod || r.label <= toPeriod);
    }
    return (!fromYear || r.label >= fromYear) && (!toYear || r.label <= toYear);
  });
  const latest = rows.length > 0 ? rows[rows.length - 1] : null;
  const atRiskCount = rows.filter((r) => r.atRisk).length;

  function resetRange() {
    if (!data || data.monthly.length === 0) return;
    setFromPeriod(data.monthly[0].label);
    setToPeriod(data.monthly[data.monthly.length - 1].label);
  }

  return (
    <div>
      <h2>Revenue &amp; cost analytics</h2>
      <p className="hint">
        Client calls are revenue (what VoxLink bills them); VoxLink's own internal team's calls are pure cost —
        nothing gets billed back for those. Priced at each company's current plan rates.
      </p>
      {error && <div className="error">{error}</div>}

      {latest?.atRisk && (
        <div className="error">
          ⚠ {view === "monthly" ? "This month" : "This year"} ({formatLabel(latest.label, view)}), VoxLink's internal
          team used {latest.internalMinutes} minutes — as much as or more than the {latest.clientMinutes} minutes
          used by all client companies combined. Internal usage should stay below client usage, or the platform is
          costing more to run than it's earning.
        </div>
      )}
      {!latest?.atRisk && atRiskCount > 0 && (
        <div className="hint">
          {atRiskCount} earlier {view === "monthly" ? "month(s)" : "year(s)"} had internal usage reach or exceed
          client usage — see the ⚠ markers below.
        </div>
      )}

      <div className="row" style={{ marginBottom: 8, alignItems: "flex-end", flexWrap: "wrap" }}>
        <button type="button" className={view === "monthly" ? "tab active" : "tab"} onClick={() => setView("monthly")}>
          Monthly
        </button>
        <button type="button" className={view === "yearly" ? "tab active" : "tab"} onClick={() => setView("yearly")}>
          Yearly
        </button>
        <label style={{ flex: "0 1 170px" }}>
          From
          <input type="month" value={fromPeriod} onChange={(e) => setFromPeriod(e.target.value)} />
        </label>
        <label style={{ flex: "0 1 170px" }}>
          To
          <input type="month" value={toPeriod} onChange={(e) => setToPeriod(e.target.value)} />
        </label>
        <button type="button" className="link-btn" onClick={resetRange}>
          Reset range
        </button>
      </div>
      {view === "yearly" && <p className="hint">In yearly view, only the year of each picked date is used.</p>}

      <div className="card inline-card">
        <BarChart rows={rows} view={view} />
      </div>

      <table className="table">
        <thead>
          <tr>
            <th>Period</th>
            <th>Client minutes</th>
            <th>Internal minutes</th>
            <th>Revenue</th>
            <th>Cost</th>
            <th>Margin</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {rows
            .slice()
            .reverse()
            .map((r) => (
              <tr key={r.label}>
                <td>{formatLabel(r.label, view)}</td>
                <td>{r.clientMinutes}</td>
                <td>{r.internalMinutes}</td>
                <td>R{r.clientRevenue.toFixed(2)}</td>
                <td>R{r.internalCost.toFixed(2)}</td>
                <td style={r.clientRevenue - r.internalCost < 0 ? { color: "#fca5a5" } : undefined}>
                  R{(r.clientRevenue - r.internalCost).toFixed(2)}
                </td>
                <td>{r.atRisk && <span className="badge badge-suspended">at risk</span>}</td>
              </tr>
            ))}
          {rows.length === 0 && (
            <tr>
              <td colSpan={7} className="muted">
                No data yet.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
