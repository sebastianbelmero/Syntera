import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import {
  Building2,
  Users,
  ScrollText,
  ClipboardCheck,
  KeyRound,
  ShieldCheck,
  CheckCircle2,
  XCircle,
  ChevronRight,
} from "lucide-react";
import { useAuthStore } from "../../store/authStore";
import { sitesApi, roleTemplatesApi } from "../../api/platform";
import { usersApi } from "../../api/site";
import { auditApi } from "../../api/audit";
import type {
  AuditLogDto,
  RoleTemplateApprovalDto,
  RoleTemplateDto,
  SiteDto,
  UserDto,
} from "../../types";

/**
 * Dashboard — live, role-aware landing page.
 *
 * Every stat card is backed by an endpoint the CURRENT user is authorized
 * to call (queries are enabled conditionally on effective permissions, and
 * Platform Admin bypasses permission checks exactly like the backend's
 * HasPermission filter):
 *
 *   • Platform Admin → sites, role templates, pending two-person approvals,
 *     platform-wide recent activity.
 *   • Site user with user.read (Biz Admin / Eng Manager) → site user roster
 *     stats + site recent activity.
 *   • Viewers (Supervisor / QO Manager / Eng Planner) → recent activity.
 *   • Technician → profile summary only (no audit.read / user.read).
 *
 * Hooks are declared unconditionally (rules-of-hooks); the early return
 * for a missing profile happens AFTER all hooks.
 */
export default function DashboardPage() {
  const profile = useAuthStore((s) => s.profile);
  const navigate = useNavigate();

  const isPlatform = profile?.roles.includes("platform-admin") ?? false;
  const isSystemAdmin = profile?.roles.includes("system-admin") ?? false;
  const has = (perm: string) => isPlatform || !!profile?.permissions.includes(perm);

  // ── Platform Admin stats (platform-wide) ────────────────
  const { data: sites } = useQuery<SiteDto[]>({
    queryKey: ["platform-sites"],
    queryFn: () => sitesApi.list(),
    enabled: isPlatform,
  });
  const { data: templates } = useQuery<RoleTemplateDto[]>({
    queryKey: ["platform-role-templates"],
    queryFn: () => roleTemplatesApi.list(),
    enabled: isPlatform,
  });
  const { data: approvals } = useQuery<RoleTemplateApprovalDto[]>({
    queryKey: ["platform-approvals"],
    queryFn: () => roleTemplatesApi.listApprovals(),
    enabled: isPlatform,
  });

  // ── Site roster stats (user.read holders, site-scoped) ──
  // System Admin deliberately excluded: their role has no user.read, so
  // /api/site/users would 403 for them (they manage admins via Sites).
  const { data: siteUsers } = useQuery<UserDto[]>({
    queryKey: ["site-users"],
    queryFn: () => usersApi.list(),
    enabled: !!profile && !isPlatform && has("user.read"),
  });

  // ── Recent activity (audit.read holders) ────────────────
  // Backend scopes automatically: Platform Admin → platform-wide,
  // site users → their own site.
  const { data: recentAudit } = useQuery<AuditLogDto[]>({
    queryKey: ["dashboard-recent-audit"],
    queryFn: () => auditApi.query({ take: 8 }),
    enabled: has("audit.read"),
  });

  if (!profile) return null;

  // ── Derived numbers ─────────────────────────────────────
  const pendingApprovals = approvals?.filter((a) => a.status === "pending").length ?? 0;
  const enabledSites = sites?.filter((s) => s.isEnabled).length ?? 0;
  const publishedTemplates = templates?.filter((t) => t.isPublished).length ?? 0;

  const totalUsers = siteUsers?.length ?? 0;
  const enabledUsers = siteUsers?.filter((u) => u.isEnabled).length ?? 0;
  const roleAssignments = siteUsers?.reduce((n, u) => n + u.roles.length, 0) ?? 0;
  const activeGrants =
    siteUsers?.reduce((n, u) => n + u.directPermissions.filter((p) => !p.isRevoked).length, 0) ?? 0;

  const showRoster = !isPlatform && has("user.read");
  const showActivity = has("audit.read");

  return (
    <div className="space-y-6">
      {/* Hero */}
      <div>
        <h1 className="text-2xl font-bold">Welcome, {profile.displayName}</h1>
        <p className="text-sm mt-1" style={{ color: "var(--color-muted)" }}>
          You are signed in as <strong>{profile.email}</strong>
          {profile.siteDisplayName && <> at <strong>{profile.siteDisplayName}</strong></>}.
        </p>
      </div>

      {/* Stat cards — content depends on the viewer's authorization scope */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        {isPlatform && (
          <>
            <StatCard
              icon={<Building2 size={20} />}
              title="Sites"
              value={sites ? String(sites.length) : "…"}
              subtitle={`${enabledSites} enabled · ${sites ? sites.length - enabledSites : 0} disabled`}
              onClick={() => navigate("/platform/sites")}
            />
            <StatCard
              icon={<KeyRound size={20} />}
              title="Role Templates"
              value={templates ? String(templates.length) : "…"}
              subtitle={`${publishedTemplates} published`}
              onClick={() => navigate("/platform/role-templates")}
            />
            <StatCard
              icon={<ClipboardCheck size={20} />}
              title="Pending Approvals"
              value={approvals ? String(pendingApprovals) : "…"}
              subtitle={
                pendingApprovals > 0
                  ? "Awaiting second-person approval"
                  : "Two-person rule queue is clear"
              }
              accent={pendingApprovals > 0 ? "var(--color-warning)" : undefined}
              onClick={() => navigate("/platform/approvals")}
            />
          </>
        )}

        {showRoster && (
          <>
            <StatCard
              icon={<Users size={20} />}
              title="Users"
              value={siteUsers ? String(totalUsers) : "…"}
              subtitle={`${enabledUsers} enabled · ${totalUsers - enabledUsers} disabled`}
              onClick={() => navigate("/site/users")}
            />
            <StatCard
              icon={<ShieldCheck size={20} />}
              title="Role Assignments"
              value={siteUsers ? String(roleAssignments) : "…"}
              subtitle={`across ${totalUsers} users`}
            />
            <StatCard
              icon={<KeyRound size={20} />}
              title="Direct Grants"
              value={siteUsers ? String(activeGrants) : "…"}
              subtitle="active time-limited grants (≤90 days)"
            />
          </>
        )}

        {/* Viewer / technician summary (no user.read): identity scope cards */}
        {!isPlatform && !showRoster && (
          <>
            <StatCard
              icon={<ShieldCheck size={20} />}
              title="Your Roles"
              value={profile.roles.length.toString()}
              subtitle={profile.roles.join(", ") || "No roles assigned"}
            />
            <StatCard
              icon={<ScrollText size={20} />}
              title="Your Permissions"
              value={profile.permissions.length.toString()}
              subtitle="Effective permission keys"
            />
            <StatCard
              icon={<Building2 size={20} />}
              title="Scope"
              value={profile.scope === "platform" ? "Platform" : (profile.siteCode ?? "Site")}
              subtitle={profile.siteDisplayName ?? ""}
            />
          </>
        )}
      </div>

      {/* Quick actions */}
      {(isPlatform || isSystemAdmin || showRoster || showActivity) && (
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
                <ActionLink onClick={() => navigate("/platform/approvals")} label="Approval Queue" />
              </>
            )}
            {isSystemAdmin && !isPlatform && (
              <ActionLink onClick={() => navigate("/platform/sites")} label="Manage Admins" />
            )}
            {showRoster && (
              <ActionLink onClick={() => navigate("/site/users")} label="Manage Users" />
            )}
            {showActivity && (
              <ActionLink onClick={() => navigate("/audit/logs")} label="Audit Logs" />
            )}
          </div>
        </div>
      )}

      {/* Recent activity — the live feed every audit.read holder sees */}
      {showActivity && (
        <div
          className="rounded-xl p-6"
          style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}
        >
          <div className="flex items-center justify-between mb-3">
            <h2 className="text-lg font-semibold flex items-center gap-2">
              <ScrollText size={18} /> Recent Activity
            </h2>
            <button
              type="button"
              onClick={() => navigate("/audit/logs")}
              className="inline-flex items-center gap-1 text-sm font-medium"
              style={{ color: "var(--color-primary)" }}
            >
              View all <ChevronRight size={14} />
            </button>
          </div>
          {!recentAudit || recentAudit.length === 0 ? (
            <p className="text-sm" style={{ color: "var(--color-muted)" }}>
              No audit events recorded yet.
            </p>
          ) : (
            <ul className="m-0 list-none space-y-1 p-0">
              {recentAudit.map((l) => (
                <li key={l.id}>
                  <button
                    type="button"
                    onClick={() => navigate("/audit/logs")}
                    className="flex w-full items-center gap-3 rounded-md p-2 text-left text-sm transition-colors hover:opacity-80"
                    style={{ backgroundColor: "var(--color-background)" }}
                    aria-label={`Open audit log: ${l.action}`}
                  >
                    {l.outcome === "success" ? (
                      <CheckCircle2
                        size={14}
                        className="shrink-0"
                        style={{ color: "var(--color-success)" }}
                        aria-hidden
                      />
                    ) : (
                      <XCircle
                        size={14}
                        className="shrink-0"
                        style={{ color: "var(--color-danger)" }}
                        aria-hidden
                      />
                    )}
                    <span className="font-mono text-xs">{l.action}</span>
                    <span className="text-xs truncate" style={{ color: "var(--color-muted)" }}>
                      {l.actorEmail ?? "anonymous"}
                    </span>
                    <span
                      className="ml-auto whitespace-nowrap text-xs"
                      style={{ color: "var(--color-muted)" }}
                    >
                      {new Date(l.timestamp).toLocaleString()}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}

function StatCard({
  icon,
  title,
  value,
  subtitle,
  onClick,
  accent,
}: {
  icon: React.ReactNode;
  title: string;
  value: string;
  subtitle: string;
  onClick?: () => void;
  accent?: string;
}) {
  const inner = (
    <>
      <div className="flex items-center gap-2 mb-2" style={{ color: accent ?? "var(--color-accent)" }}>
        {icon}
        <span
          className="text-xs font-medium uppercase tracking-wide"
          style={{ color: "var(--color-muted)" }}
        >
          {title}
        </span>
      </div>
      <div className="text-2xl font-bold">{value}</div>
      <div className="text-xs mt-1" style={{ color: "var(--color-muted)" }}>
        {subtitle}
      </div>
    </>
  );

  if (onClick) {
    return (
      <button
        type="button"
        onClick={onClick}
        className="rounded-xl p-5 text-left transition-opacity hover:opacity-85"
        style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}
        aria-label={`${title}: ${value}`}
      >
        {inner}
      </button>
    );
  }
  return (
    <div
      className="rounded-xl p-5"
      style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}
    >
      {inner}
    </div>
  );
}

function ActionLink({ onClick, label }: { onClick: () => void; label: string }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="rounded-lg px-3 py-2.5 text-sm font-medium min-h-[44px] transition-opacity hover:opacity-80"
      style={{
        backgroundColor: "var(--color-background)",
        border: "1px solid var(--color-border)",
      }}
    >
      {label}
    </button>
  );
}
