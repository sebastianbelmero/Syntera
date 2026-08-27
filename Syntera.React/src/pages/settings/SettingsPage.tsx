import { useAuthStore } from "../../store/authStore";
import { useThemeStore } from "../../store/themeStore";
import { User, Moon, Sun, Shield } from "lucide-react";

export default function SettingsPage() {
  const profile = useAuthStore((s) => s.profile);
  const theme = useAuthStore((s) => s.theme);
  const { isDark, toggleMode } = useThemeStore();

  if (!profile) return null;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold">Settings</h1>
        <p className="text-sm" style={{ color: "var(--color-muted)" }}>
          User profile, theme preferences, and session info.
        </p>
      </div>

      {/* Profile */}
      <section className="rounded-xl p-6"
        style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}>
        <h3 className="mb-4 flex items-center gap-2 text-lg font-semibold">
          <User size={18} /> Profile
        </h3>
        <dl className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <Field label="Display Name" value={profile.displayName} />
          <Field label="Email" value={profile.email} />
          <Field label="Scope" value={profile.scope === "platform" ? "Platform Admin" : (profile.siteCode ?? "Site User")} />
          {profile.siteDisplayName && <Field label="Site" value={profile.siteDisplayName} />}
          <Field label="Roles" value={profile.roles.join(", ") || "—"} />
          <Field label="Permissions" value={`${profile.permissions.length} keys`} />
        </dl>
      </section>

      {/* Theme */}
      <section className="rounded-xl p-6"
        style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}>
        <h3 className="mb-4 flex items-center gap-2 text-lg font-semibold">
          <Sun size={18} /> Appearance
        </h3>
        <div className="flex items-center justify-between">
          <div>
            <div className="text-sm font-medium flex items-center gap-2">
              {isDark ? <Moon size={14} /> : <Sun size={14} />}
              {isDark ? "Dark Mode" : "Light Mode"}
            </div>
            <div className="text-xs mt-0.5" style={{ color: "var(--color-muted)" }}>
              {theme ? `Brand palette: ${theme.themeKey}` : "Default palette"}
            </div>
          </div>
          <button onClick={toggleMode} className="px-3 py-2 rounded-md text-sm"
            style={{ border: "1px solid var(--color-border)" }}>
            Switch to {isDark ? "Light" : "Dark"}
          </button>
        </div>
        <p className="text-xs mt-3" style={{ color: "var(--color-muted)" }}>
          The brand palette is determined by your site (set by the Platform Admin).
          You can override light/dark mode here — preference is saved per-browser.
        </p>
      </section>

      {/* Session */}
      <section className="rounded-xl p-6"
        style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}>
        <h3 className="mb-4 flex items-center gap-2 text-lg font-semibold">
          <Shield size={18} /> Session
        </h3>
        <p className="text-sm" style={{ color: "var(--color-muted)" }}>
          Your session is managed by a short-lived JWT (15 minutes) backed by a rotating
          refresh token (24 hours). All authentication events are recorded in the audit log.
        </p>
      </section>
    </div>
  );
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs font-medium uppercase tracking-wide" style={{ color: "var(--color-muted)" }}>
        {label}
      </dt>
      <dd className="mt-1 text-sm font-medium">{value}</dd>
    </div>
  );
}
