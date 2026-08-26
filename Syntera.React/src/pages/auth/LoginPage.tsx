import { useState } from "react";
import { Navigate, useLocation, useNavigate } from "react-router-dom";
import { toast } from "sonner";
import {
  Boxes,
  Share2,
  Gauge,
  ShieldCheck,
  Globe2,
} from "lucide-react";
import { useAuthStore } from "../../store/authStore";
import { authApi } from "../../api/auth";
import { ApiError } from "../../api/client";
import logoUrl from "../../assets/syntera-logo.jpg";
import logoTaglineUrl from "../../assets/syntera-logo-tagline.png";

/**
 * Five pillars of the Syntera brand — derived from the official
 * logo description document. Shown on the login brand panel as a
 * small reminder of what the platform stands for.
 */
const PILLARS = [
  {
    icon: Share2,
    name: "Synergy",
    desc: "Kolaborasi & sinergi sumber daya antar fasilitas (cross-site).",
  },
  {
    icon: Globe2,
    name: "Integration",
    desc: "Terhubung Oracle EAM sebagai jembatan orkestrasi data terpusat.",
  },
  {
    icon: Gauge,
    name: "Performance",
    desc: "Operasional akurat & data-driven via kalkulasi matematis otomatis.",
  },
  {
    icon: ShieldCheck,
    name: "Compliance",
    desc: "Integritas data 21 CFR Part 11 + jejak audit kriptografis + GxP.",
  },
  {
    icon: Boxes,
    name: "One Platform",
    desc: "Satu platform tunggal — One Platform. One Standard. One Direction.",
  },
];

export default function LoginPage() {
  // Demo credentials are only auto-filled in dev (Vite's DEV flag).
  // Production builds ship empty inputs — avoids shipping known
  // admin credentials in the bundle's first paint.
  const isDev = import.meta.env.DEV;
  const [email, setEmail] = useState(isDev ? "admin@syntera.local" : "");
  const [password, setPassword] = useState(isDev ? "ChangeMe!Strong#1" : "");
  const [loading, setLoading] = useState(false);
  const navigate = useNavigate();
  const location = useLocation();
  const login = useAuthStore((s) => s.login);
  const isAuthed = useAuthStore((s) => s.isAuthenticated());

  if (isAuthed) {
    const from = (location.state as { from?: string } | null)?.from ?? "/dashboard";
    return <Navigate to={from} replace />;
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email || !password) {
      toast.error("Email dan password wajib diisi.");
      return;
    }
    setLoading(true);
    try {
      const res = await authApi.login({ email, password });
      login(res);
      toast.success(`Selamat datang, ${res.profile.fullName ?? res.profile.email}!`);
      navigate("/dashboard", { replace: true });
    } catch (err) {
      const apiErr = err as ApiError;
      toast.error(apiErr.message ?? "Login gagal. Periksa kredensial Anda.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="grid min-h-screen lg:grid-cols-[1.05fr_1fr]">
      {/* ── Brand panel (left side on desktop, hidden on mobile) ─────
          Uses the Syntera navy→teal brand gradient derived from the
          logo's two ribbon-swooshes. Logo + tagline block sits at
          top-left; the full infographic PNG (with the 5 pillars
          iconography) anchors the lower half on large screens. */}
      <div
        className="relative hidden flex-col justify-between overflow-hidden p-8 text-white lg:flex xl:p-12"
        style={{
          // Gradient derives from the active palette's --primary and
          // --accent tokens, blended through color-mix so the brand
          // panel re-themes correctly when the user switches palettes
          // (e.g. Kalbe crimson, Fima violet). Previously hardcoded
          // to Syntera navy/teal hex, which broke the brand panel
          // for the other 5 palettes.
          background:
            "linear-gradient(135deg, var(--primary) 0%, color-mix(in srgb, var(--primary) 60%, #000 40%) 55%, color-mix(in srgb, var(--accent) 70%, #000 30%) 100%)",
        }}
      >
        {/* Top: logo chip + wordmark */}
        <div className="flex items-center gap-3">
          <div className="overflow-hidden rounded-xl bg-white p-1.5 shadow-lg">
            <img
              src={logoUrl}
              alt="Syntera"
              className="h-12 w-12 object-contain sm:h-14 sm:w-14"
              draggable={false}
            />
          </div>
          <div className="leading-tight">
            <div className="text-xl font-bold tracking-tight xl:text-2xl">
              SYNTERA
            </div>
            <div className="text-[10px] font-medium uppercase tracking-[0.18em] text-[var(--accent)]">
              Synergy · Integration · Performance · Compliance · One Platform
            </div>
          </div>
        </div>

        {/* Middle: hero copy + tagline */}
        <div className="relative space-y-5">
          <h1 className="max-w-md text-3xl font-bold leading-tight xl:text-4xl">
            Connecting Engineering.
            <br />
            <span className="text-[var(--accent)]">Unifying Excellence.</span>
          </h1>
          <p className="max-w-md text-sm leading-relaxed opacity-85 xl:text-base">
            Platform Manajemen & Kepatuhan Kalibrasi Terintegrasi yang
            menyatukan seluruh proses engineering lifecycle di entitas
            Kalbe — terintegrasi Oracle EAM, patuh 21 CFR Part 11 & GxP.
          </p>
        </div>

        {/* 5 pillars grid */}
        <ul className="grid grid-cols-1 gap-2.5 sm:grid-cols-2 xl:grid-cols-3">
          {PILLARS.map((p) => (
            <li
              key={p.name}
              className="flex items-start gap-2.5 rounded-lg bg-white/5 p-2.5 backdrop-blur-sm ring-1 ring-white/10"
            >
              <span className="mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-md bg-[var(--accent)]/15 text-[var(--accent)]">
                <p.icon className="size-4" />
              </span>
              <span className="flex flex-col">
                <span className="text-xs font-semibold">{p.name}</span>
                <span className="text-[11px] leading-snug opacity-70">
                  {p.desc}
                </span>
              </span>
            </li>
          ))}
        </ul>

        {/* Bottom: slogan chip + footer */}
        <div className="flex flex-col gap-3 text-xs opacity-70">
          <div className="inline-flex w-fit items-center gap-2 rounded-full bg-white/10 px-3 py-1.5 text-[11px] font-medium uppercase tracking-wider backdrop-blur">
            <span>One Platform.</span>
            <span className="text-[var(--success)]">One Standard.</span>
            <span>One Direction.</span>
          </div>
          <div className="flex items-center gap-3">
            <span>© 2026 Syntera</span>
            <span>•</span>
            <span>Powered by .NET 10 + React 19</span>
          </div>
        </div>

        {/* Decorative geometry — orbit line motif from the logo */}
        <div
          aria-hidden
          className="pointer-events-none absolute -right-32 -top-32 size-[420px] rounded-full opacity-20"
          style={{
            background:
              "radial-gradient(circle, var(--accent) 0%, transparent 70%)",
          }}
        />
        <div
          aria-hidden
          className="pointer-events-none absolute -bottom-40 -left-20 size-[380px] rounded-full opacity-10"
          style={{
            background:
              "radial-gradient(circle, var(--success) 0%, transparent 70%)",
          }}
        />
      </div>

      {/* ── Form panel (right side, full-width on mobile) ─────────── */}
      <div className="flex items-center justify-center p-6 sm:p-10 xl:p-12">
        <form
          onSubmit={handleSubmit}
          className="w-full max-w-sm space-y-6"
          aria-label="Login form"
        >
          <header className="space-y-3 text-center">
            {/* Mobile-only logo (lg+ shows the full brand panel) */}
            <div className="mx-auto w-fit overflow-hidden rounded-2xl bg-white p-2 shadow-lg ring-1 ring-black/5 lg:hidden">
              <img
                src={logoUrl}
                alt="Syntera"
                className="h-14 w-14 object-contain"
                draggable={false}
              />
            </div>
            <h1 className="text-2xl font-bold tracking-tight text-[var(--foreground)]">
              Masuk ke akun Anda
            </h1>
            <p className="text-sm text-[var(--muted-foreground)]">
              Gunakan kredensial yang terdaftar pada platform Syntera.
            </p>
          </header>

          <div className="space-y-3">
            <label className="block text-sm font-medium text-[var(--foreground)]">
              Email
              <input
                type="email"
                autoComplete="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                className="mt-1 w-full rounded-lg border border-[var(--input)] bg-card px-3 py-2 text-sm text-foreground shadow-sm outline-none transition focus:border-[var(--primary)] focus:ring-2 focus:ring-[var(--ring)]"
                placeholder="anda@apotek.co.id"
                required
              />
            </label>

            <label className="block text-sm font-medium text-[var(--foreground)]">
              Password
              <input
                type="password"
                autoComplete="current-password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                minLength={8}
                className="mt-1 w-full rounded-lg border border-[var(--input)] bg-card px-3 py-2 text-sm text-foreground shadow-sm outline-none transition focus:border-[var(--primary)] focus:ring-2 focus:ring-[var(--ring)]"
                placeholder="••••••••"
                required
              />
            </label>
          </div>

          <button
            type="submit"
            disabled={loading}
            className="w-full rounded-lg bg-[var(--primary)] px-4 py-2.5 text-sm font-semibold text-[var(--primary-foreground)] shadow-sm transition hover:bg-[var(--primary-hover)] disabled:cursor-not-allowed disabled:opacity-60"
          >
            {loading ? "Memverifikasi…" : "Masuk"}
          </button>

          {isDev && (
            <p className="text-center text-xs text-[var(--muted-foreground)]">
              Demo: <code className="font-mono">admin@syntera.local</code> /{" "}
              <code className="font-mono">ChangeMe!Strong#1</code>
            </p>
          )}

          {/* Compact brand strip with the full infographic PNG for
              context. Hidden on very small screens to avoid layout
              crowding; visible sm+ where there's horizontal room. */}
          <div className="hidden flex-col items-center gap-2 border-t border-[var(--border)] pt-4 sm:flex">
            <img
              src={logoTaglineUrl}
              alt="Syntera logo with tagline and 5 pillars infographic"
              className="h-auto w-full max-w-[280px] rounded-lg"
              draggable={false}
            />
          </div>
        </form>
      </div>
    </div>
  );
}
