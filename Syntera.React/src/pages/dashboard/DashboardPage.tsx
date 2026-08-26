import { useQuery } from "@tanstack/react-query";
import {
  AreaChart, Area, ResponsiveContainer, Tooltip, XAxis, YAxis,
  CartesianGrid,
} from "recharts";
import {
  Activity, AlertTriangle, Boxes, Package, ShoppingCart, TrendingUp,
} from "lucide-react";
import { dashboardApi } from "../../api/operations";
import { formatIDR, formatNumber, formatDate } from "../../lib/format";

export default function DashboardPage() {
  const summary = useQuery({
    queryKey: ["dashboard-summary"],
    queryFn: dashboardApi.summary,
  });
  const trend = useQuery({
    queryKey: ["dashboard-trend"],
    queryFn: dashboardApi.trend,
  });

  if (summary.isError || trend.isError) {
    return (
      <div className="flex flex-col gap-2 p-6">
        <h2 className="text-2xl font-bold">Dashboard</h2>
        <p className="text-[var(--danger)]">
          Gagal memuat data dashboard. Coba segarkan halaman.
        </p>
      </div>
    );
  }

  const summaryData = summary.data;
  const trendData = trend.data?.last14Days ?? [];

  return (
    <div className="flex flex-col gap-6">
      <header className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold tracking-tight">Dashboard</h2>
          <p className="text-sm text-[var(--muted-foreground)]">
            Ringkasan operasional apotek hari ini.
          </p>
        </div>
        <span className="rounded-full bg-[var(--accent)]/30 px-3 py-1 text-xs font-medium text-[var(--primary)]">
          Per {formatDate(new Date().toISOString())}
        </span>
      </header>

      {/* KPI cards */}
      <section className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard
          label="Penjualan Hari Ini"
          value={formatIDR(summaryData?.todaySalesAmount ?? 0)}
          sub={`${formatNumber(summaryData?.todaySalesCount ?? 0)} transaksi`}
          icon={<ShoppingCart size={18} />}
          accent="primary"
        />
        <KpiCard
          label="Penjualan Bulan Ini"
          value={formatIDR(summaryData?.monthSalesAmount ?? 0)}
          sub={`${formatNumber(summaryData?.monthSalesCount ?? 0)} transaksi`}
          icon={<TrendingUp size={18} />}
          accent="success"
        />
        <KpiCard
          label="Total Produk"
          value={formatNumber(summaryData?.totalProducts ?? 0)}
          sub={`${formatNumber(summaryData?.lowStockProducts ?? 0)} stok menipis`}
          icon={<Package size={18} />}
          accent="info"
        />
        <KpiCard
          label="Hampir Kadaluarsa"
          value={formatNumber(summaryData?.nearExpiryProducts ?? 0)}
          sub="< 30 hari menuju expiry"
          icon={<AlertTriangle size={18} />}
          accent="warning"
        />
      </section>

      {/* Trend chart + side widgets */}
      <section className="grid grid-cols-1 gap-4 xl:grid-cols-3">
        <article className="rounded-2xl border border-[var(--border)] bg-[var(--card)] p-5 xl:col-span-2">
          <header className="mb-3 flex items-center justify-between">
            <h3 className="font-semibold">Tren Penjualan 14 Hari</h3>
            <Activity className="text-[var(--muted-foreground)]" size={18} />
          </header>
          <div className="h-72 w-full">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={trendData}>
                <defs>
                  <linearGradient id="sales-area" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor="var(--primary)" stopOpacity={0.6} />
                    <stop offset="100%" stopColor="var(--primary)" stopOpacity={0} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" />
                <XAxis
                  dataKey="date"
                  tickFormatter={(v) => formatDate(v).split(" ").slice(0, 2).join(" ")}
                  tick={{ fontSize: 11 }}
                  stroke="var(--muted-foreground)"
                />
                <YAxis
                  tickFormatter={(v) => formatIDR(Number(v)).replace("Rp", "").trim()}
                  tick={{ fontSize: 11 }}
                  stroke="var(--muted-foreground)"
                />
                <Tooltip
                  formatter={(v) => formatIDR(Number(v ?? 0))}
                  labelFormatter={(l) => formatDate(String(l))}
                  contentStyle={{
                    background: "var(--card)",
                    border: "1px solid var(--border)",
                    borderRadius: 8,
                  }}
                />
                <Area
                  type="monotone"
                  dataKey="amount"
                  stroke="var(--primary)"
                  strokeWidth={2}
                  fill="url(#sales-area)"
                />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </article>

        <article className="rounded-2xl border border-[var(--border)] bg-[var(--card)] p-5">
          <header className="mb-3 flex items-center justify-between">
            <h3 className="font-semibold">Top 5 Produk Bulan Ini</h3>
            <Boxes className="text-[var(--muted-foreground)]" size={18} />
          </header>
          <ul className="space-y-3">
            {(trend.data?.top5ProductsThisMonth ?? []).map((p, idx) => (
              <li key={p.productId} className="flex items-start gap-3">
                <span className="grid h-6 w-6 shrink-0 place-items-center rounded-full bg-[var(--primary)]/10 text-xs font-bold text-[var(--primary)]">
                  {idx + 1}
                </span>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium">{p.productName}</p>
                  <p className="text-xs text-[var(--muted-foreground)]">
                    {p.productSku} • {formatNumber(p.quantitySold)} unit • {formatIDR(p.revenue)}
                  </p>
                </div>
              </li>
            ))}
            {trend.data?.top5ProductsThisMonth.length === 0 && (
              <li className="text-sm text-[var(--muted-foreground)]">
                Belum ada penjualan bulan ini.
              </li>
            )}
          </ul>
        </article>
      </section>
    </div>
  );
}

function KpiCard({
  label,
  value,
  sub,
  icon,
  accent,
}: {
  label: string;
  value: string;
  sub: string;
  icon: React.ReactNode;
  accent: "primary" | "success" | "warning" | "info" | "danger";
}) {
  const colorMap: Record<typeof accent, string> = {
    primary: "var(--primary)",
    success: "var(--success)",
    warning: "var(--warning)",
    info: "var(--info)",
    danger: "var(--danger)",
  };
  const color = colorMap[accent];
  return (
    <article className="rounded-2xl border border-[var(--border)] bg-[var(--card)] p-5">
      <div className="flex items-center justify-between">
        <span className="text-xs font-medium uppercase tracking-wide text-[var(--muted-foreground)]">
          {label}
        </span>
        <span
          className="grid h-9 w-9 place-items-center rounded-full"
          style={{ background: `${color}15`, color }}
        >
          {icon}
        </span>
      </div>
      <p className="mt-2 text-2xl font-bold tracking-tight">{value}</p>
      <p className="mt-1 text-xs text-[var(--muted-foreground)]">{sub}</p>
    </article>
  );
}
