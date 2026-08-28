import { useNavigate } from "react-router-dom";
import { useAuthStore } from "../../store/authStore";
import { ShieldCheck, Building2, Users, ScrollText } from "lucide-react";

/**
 * Dashboard — shows quick actions + summary based on the user's role.
 * Platform Admin sees platform-wide stats; Site Admin sees site stats;
 * End Users see a personalized welcome.
 */
export default function DashboardPage() {
  const profile = useAuthStore((s) => s.profile);
  const navigate = useNavigate();
  if (!profile) return null;

  const isPlatform = profile.roles.includes("platform-admin");
  const isSiteAdmin = profile.roles.includes("site-business-admin")
    || profile.roles.includes("eng-manager")
    || profile.roles.includes("supervisor")
    || profile.roles.includes("qo-manager");

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold">Welcome, {profile.displayName}</h1>
        <p className="text-sm mt-1" style={{ color: "var(--color-muted)" }}>
          You are signed in as <strong>{profile.email}</strong>
          {profile.siteDisplayName && <> at <strong>{profile.siteDisplayName}</strong></>}.
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <Card
          icon={<ShieldCheck size={20} />}
          title="Your Roles"
          value={profile.roles.length.toString()}
          subtitle={profile.roles.join(", ") || "No roles assigned"}
        />
        <Card
          icon={<ScrollText size={20} />}
          title="Your Permissions"
          value={profile.permissions.length.toString()}
          subtitle="Effective permission keys"
        />
        <Card
          icon={<Building2 size={20} />}
          title="Scope"
          value={profile.scope === "platform" ? "Platform" : (profile.siteCode ?? "Site")}
          subtitle={profile.scope === "platform" ? "admin@syntera.com" : (profile.siteDisplayName ?? "")}
        />
      </div>

      {(isPlatform || isSiteAdmin) && (
        <div
          className="rounded-xl p-6"
          style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}
        >
          <h2 className="text-lg font-semibold mb-3 flex items-center gap-2">
            <Users size={18} /> Quick Actions
          </h2>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-3 text-sm">
            {isPlatform && (
              <>
                <ActionLink onClick={() => navigate("/platform/sites")} label="Manage Sites" />
                <ActionLink onClick={() => navigate("/platform/role-templates")} label="Role Templates" />
              </>
            )}
            {isSiteAdmin && (
              <ActionLink onClick={() => navigate("/site/users")} label="Manage Users" />
            )}
            <ActionLink onClick={() => navigate("/audit/logs")} label="Audit Logs" />
          </div>
        </div>
      )}
    </div>
  );
}

function Card({ icon, title, value, subtitle }: {
  icon: React.ReactNode; title: string; value: string; subtitle: string;
}) {
  return (
    <div
      className="rounded-xl p-5"
      style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}
    >
      <div className="flex items-center gap-2 mb-2" style={{ color: "var(--color-accent)" }}>
        {icon}
        <span className="text-xs font-medium uppercase tracking-wide" style={{ color: "var(--color-muted)" }}>
          {title}
        </span>
      </div>
      <div className="text-2xl font-bold">{value}</div>
      <div className="text-xs mt-1" style={{ color: "var(--color-muted)" }}>{subtitle}</div>
    </div>
  );
}

function ActionLink({ onClick, label }: { onClick: () => void; label: string }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="px-3 py-2 rounded-lg text-center transition hover:opacity-80 cursor-pointer"
      style={{ backgroundColor: "var(--color-primary)", color: "white" }}
    >
      {label}
    </button>
  );
}
