import { useAuthStore } from "../../store/authStore";
import {
  useThemeStore,
  THEME_BRANDS,
  THEME_LABELS,
  THEME_SWATCH,
  type ThemeBrand,
} from "../../store/themeStore";
import { User, Moon, Sun, Shield, Activity, Palette, Check } from "lucide-react";
import { cn } from "../../lib/cn";

export default function SettingsPage() {
  const profile = useAuthStore((s) => s.profile);
  const logout = useAuthStore((s) => s.logout);
  const { brand, isDark, setBrand, toggleMode } = useThemeStore();

  return (
    <div className="flex flex-col gap-6">
      <header>
        <h2 className="text-2xl font-bold tracking-tight">Pengaturan</h2>
        <p className="text-sm text-[var(--muted-foreground)]">
          Profil pengguna, preferensi tampilan, dan sesi login.
        </p>
      </header>

      <section className="rounded-2xl border border-[var(--border)] bg-[var(--card)] p-6">
        <h3 className="mb-4 flex items-center gap-2 text-lg font-semibold">
          <User size={18} /> Profil
        </h3>
        <dl className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div>
            <dt className="text-xs font-medium uppercase tracking-wide text-[var(--muted-foreground)]">
              Nama
            </dt>
            <dd className="mt-1 text-sm font-medium">
              {profile?.fullName ?? profile?.email ?? "—"}
            </dd>
          </div>
          <div>
            <dt className="text-xs font-medium uppercase tracking-wide text-[var(--muted-foreground)]">
              Email
            </dt>
            <dd className="mt-1 text-sm font-medium">{profile?.email ?? "—"}</dd>
          </div>
          <div>
            <dt className="text-xs font-medium uppercase tracking-wide text-[var(--muted-foreground)]">
              User ID
            </dt>
            <dd className="mt-1 text-sm font-mono">{profile?.id ?? "—"}</dd>
          </div>
          <div>
            <dt className="text-xs font-medium uppercase tracking-wide text-[var(--muted-foreground)]">
              Peran
            </dt>
            <dd className="mt-1 flex flex-wrap gap-1">
              {profile?.roles.map((r) => (
                <span
                  key={r}
                  className="rounded-full bg-[var(--primary)]/15 px-2 py-1 text-xs font-medium text-[var(--primary)]"
                >
                  <Shield size={11} className="mr-1 inline" />
                  {r}
                </span>
              ))}
            </dd>
          </div>
        </dl>
      </section>

      <section className="rounded-2xl border border-[var(--border)] bg-[var(--card)] p-6">
        <h3 className="mb-4 flex items-center gap-2 text-lg font-semibold">
          <Palette size={18} /> Palet Merek
        </h3>
        <p className="mb-4 text-xs text-[var(--muted-foreground)]">
          Pilih palet warna merek yang diturunkan dari studi logo:
          Kalbe · Dankos · Hexpharm · Fima · GOF · Kalventis. Setiap
          palet bekerja dalam mode terang maupun gelap.
        </p>
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
          {THEME_BRANDS.map((b: ThemeBrand) => {
            const active = brand === b;
            return (
              <button
                key={b}
                type="button"
                onClick={() => setBrand(b)}
                className={cn(
                  "group relative flex flex-col items-center gap-2 rounded-xl border p-3 transition-all",
                  active
                    ? "border-[var(--primary)] bg-[var(--primary)]/5 ring-2 ring-[var(--primary)]/30"
                    : "border-[var(--border)] hover:border-[var(--input-hover)] hover:bg-[var(--surface)]",
                )}
                aria-pressed={active}
                aria-label={`Pilih tema ${THEME_LABELS[b]}`}
              >
                <span
                  className="size-10 rounded-full border border-black/10 shadow-sm"
                  style={{ background: THEME_SWATCH[b] }}
                />
                <span className="text-xs font-medium">{THEME_LABELS[b]}</span>
                {active && (
                  <span className="absolute right-1.5 top-1.5 flex size-5 items-center justify-center rounded-full bg-[var(--primary)] text-white">
                    <Check size={12} />
                  </span>
                )}
              </button>
            );
          })}
        </div>
      </section>

      <section className="rounded-2xl border border-[var(--border)] bg-[var(--card)] p-6">
        <h3 className="mb-4 flex items-center gap-2 text-lg font-semibold">
          <Activity size={18} /> Mode Tampilan
        </h3>
        <div className="flex items-center justify-between">
          <div>
            <p className="text-sm font-medium">Mode Gelap</p>
            <p className="text-xs text-[var(--muted-foreground)]">
              Saklar untuk konsistensi di sore/malam hari. Preferensi OS
              digunakan saat pertama kali membuka aplikasi.
            </p>
          </div>
          <button
            type="button"
            onClick={toggleMode}
            className="flex items-center gap-2 rounded-lg border border-[var(--border)] bg-[var(--card)] px-3 py-2 text-sm transition hover:bg-[var(--surface)]"
            aria-pressed={isDark}
          >
            {isDark ? <Moon size={16} /> : <Sun size={16} />}
            {isDark ? "Gelap" : "Terang"}
          </button>
        </div>
      </section>

      <section className="rounded-2xl border border-[var(--border)] bg-[var(--card)] p-6">
        <h3 className="mb-4 text-lg font-semibold">Sesi</h3>
        <button
          type="button"
          onClick={() => {
            logout();
            window.location.href = "/login";
          }}
          className="rounded-lg bg-[var(--danger)] px-4 py-2 text-sm font-semibold text-white transition hover:bg-[var(--danger-hover)]"
        >
          Keluar
        </button>
      </section>

      <footer className="text-xs text-[var(--muted-foreground)]">
        Syntera v1.0.0 — Connecting Engineering. Unifying Excellence. —
        One Platform. One Standard. One Direction. — © 2026
      </footer>
    </div>
  );
}
