import { useAuthStore } from "../../store/authStore";
import { useThemeStore } from "@sebastianbelmero/kalventis-ui";
import { User, Moon, Sun, Shield, Activity } from "lucide-react";

export default function SettingsPage() {
  const profile = useAuthStore((s) => s.profile);
  const logout = useAuthStore((s) => s.logout);
  const { isDark, toggleTheme } = useThemeStore();

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
          <Activity size={18} /> Preferensi Tampilan
        </h3>
        <div className="flex items-center justify-between">
          <div>
            <p className="text-sm font-medium">Mode Gelap</p>
            <p className="text-xs text-[var(--muted-foreground)]">
              Saklar tema untuk konsistensi di sore/malam hari.
            </p>
          </div>
          <button
            type="button"
            onClick={toggleTheme}
            className="flex items-center gap-2 rounded-lg border border-[var(--border)] bg-white px-3 py-2 text-sm transition hover:bg-[var(--surface)]"
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
        Syntera v1.0.0 — Kalventis UI v2.0 — © 2026
      </footer>
    </div>
  );
}
