import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  ArrowLeft,
  CheckCircle2,
  ChevronRight,
  Clock,
  KeyRound,
  Pencil,
  Power,
  ScrollText,
  Shield,
  XCircle,
} from "lucide-react";
import { usersApi } from "../../api/site";
import { roleTemplatesApi } from "../../api/platform";
import { auditApi } from "../../api/audit";
import { ApiError } from "../../api/client";
import { useAuthStore } from "../../store/authStore";
import type {
  AssignRoleDto,
  AuditLogDto,
  GrantDirectPermissionDto,
  PermissionCatalogDto,
  RoleDto,
  UserDto,
} from "../../types";

type Tab = "overview" | "roles" | "grants" | "activity";

const userDetailKey = (id: string) => ["site-user", id] as const;

/**
 * User Detail — `/site/users/:id`
 *
 * The UsersPage drawer stays for quick profile edits (name/email/title);
 * this page is the DEPTH view: role assignments with lifecycle, direct
 * permission grants with expiry, and the user's slice of the audit trail.
 *
 * Authorization mirrors the backend action-by-action:
 *   • route-level: user.read (guarded in App.tsx)
 *   • assign/revoke role forms → user_role.assign / user_role.revoke
 *   • grant form → permission.grant, but only rendered for Platform Admin
 *     because the permission-catalog endpoint is PlatformAdminOnly (the
 *     grant select needs it to offer permission choices)
 *   • activity tab → audit.read (Platform Admin bypasses all checks)
 */
export default function UserDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const profile = useAuthStore((s) => s.profile);
  const [tab, setTab] = useState<Tab>("overview");

  const isPlatform = profile?.roles.includes("platform-admin") ?? false;
  const has = (perm: string) => isPlatform || !!profile?.permissions.includes(perm);

  // ── Data ────────────────────────────────────────────────
  const { data: user, isLoading, error } = useQuery<UserDto>({
    queryKey: userDetailKey(id ?? ""),
    queryFn: () => usersApi.get(id!),
    enabled: !!id,
    retry: false,
  });

  const { data: roles } = useQuery<RoleDto[]>({
    queryKey: ["site-roles"],
    queryFn: () => usersApi.listRoles(),
    enabled: has("role.read"),
  });

  const { data: catalog } = useQuery<PermissionCatalogDto>({
    queryKey: ["permission-catalog"],
    queryFn: () => roleTemplatesApi.permissionCatalog(),
    enabled: isPlatform, // PlatformAdminOnly endpoint
    retry: false,
  });

  const { data: activity, isLoading: activityLoading } = useQuery<AuditLogDto[]>({
    queryKey: ["user-activity", id],
    queryFn: () => auditApi.query({ actorUserId: id, take: 50 }),
    enabled: has("audit.read") && !!id,
    retry: false,
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ["site-users"] });
    if (id) void queryClient.invalidateQueries({ queryKey: userDetailKey(id) });
  };

  // ── Mutations (same API calls as UsersPage, re-pointed here) ──
  const assignRoleMutation = useMutation({
    mutationFn: (dto: AssignRoleDto) => usersApi.assignRole(dto),
    onSuccess: () => {
      toast.success("Role assigned");
      invalidate();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  const revokeRoleMutation = useMutation({
    mutationFn: (roleId: string) => usersApi.revokeRole({ userId: id ?? "", roleId }),
    onSuccess: () => {
      toast.success("Role revoked");
      invalidate();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  const revokePermissionMutation = useMutation({
    mutationFn: (userPermissionId: string) =>
      usersApi.revokePermission({ userPermissionId }),
    onSuccess: () => {
      toast.success("Permission revoked");
      invalidate();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  const grantPermissionMutation = useMutation({
    mutationFn: (dto: GrantDirectPermissionDto) => usersApi.grantPermission(dto),
    onSuccess: () => {
      toast.success("Permission granted");
      invalidate();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  /** Direct-permission grant with the 90-day ceiling enforced client-side
   *  (the backend re-validates). Only Platform Admin has the catalog, so
   *  only they get a grant form — mirrors UsersPage behavior. */
  const grantPermissionFlow = (dto: GrantDirectPermissionDto) => {
    const expiry = new Date(dto.expiresAt);
    const maxExpiry = new Date(Date.now() + 90 * 24 * 60 * 60 * 1000);
    if (expiry > maxExpiry) {
      toast.error("Direct permission cannot exceed 90 days");
      return;
    }
    grantPermissionMutation.mutate(dto);
  };

  const disableMutation = useMutation({
    mutationFn: () => usersApi.disable(id!),
    onSuccess: () => {
      toast.success("User disabled");
      invalidate();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  // ── Loading / error states ──────────────────────────────
  if (isLoading) {
    return (
      <div className="py-8 text-center" style={{ color: "var(--color-muted)" }}>
        Loading user…
      </div>
    );
  }

  if (error || !user) {
    const notFound = error instanceof ApiError && error.isNotFound();
    return (
      <div className="space-y-4">
        <BackLink />
        <div
          className="rounded-xl p-8 text-center"
          style={{
            backgroundColor: "var(--color-surface)",
            border: "1px solid var(--color-border)",
          }}
        >
          <p className="text-sm" style={{ color: "var(--color-muted)" }}>
            {notFound
              ? "User not found. It may have been removed from this site."
              : error instanceof ApiError
                ? error.message
                : "Failed to load user."}
          </p>
        </div>
      </div>
    );
  }

  const activeGrants = user.directPermissions.filter((p) => !p.isRevoked);

  return (
    <div className="space-y-4">
      <BackLink />

      {/* Header card */}
      <div
        className="rounded-xl p-6 flex flex-col sm:flex-row sm:items-center gap-4"
        style={{
          backgroundColor: "var(--color-surface)",
          border: "1px solid var(--color-border)",
        }}
      >
        <div
          className="w-14 h-14 rounded-full flex items-center justify-center text-white text-lg font-semibold shrink-0"
          style={{ backgroundColor: "var(--color-primary)" }}
          aria-hidden
        >
          {user.displayName.charAt(0).toUpperCase()}
        </div>
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <h1 className="text-2xl font-bold">{user.displayName}</h1>
            {!user.isEnabled && (
              <span
                className="text-xs px-2 py-0.5 rounded-full"
                style={{ backgroundColor: "var(--color-danger)", color: "white" }}
              >
                Disabled
              </span>
            )}
            {user.title && (
              <span
                className="text-xs px-2 py-0.5 rounded"
                style={{ backgroundColor: "var(--color-muted)", color: "var(--color-surface)" }}
              >
                {user.title}
              </span>
            )}
          </div>
          <div className="text-sm mt-1" style={{ color: "var(--color-muted)" }}>
            {user.email}
          </div>
          <div className="text-xs mt-1" style={{ color: "var(--color-muted)" }}>
            {user.roles.length} role{user.roles.length === 1 ? "" : "s"} ·{" "}
            {activeGrants.length} active direct grant{activeGrants.length === 1 ? "" : "s"} ·
            last login {user.lastLoginAt ? new Date(user.lastLoginAt).toLocaleString() : "never"}
          </div>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() =>
              navigate("/site/users", { state: { editUserId: user.id } })
            }
            className="flex items-center gap-1.5 px-3 py-2.5 min-h-[44px] rounded-lg text-sm font-medium"
            style={{
              border: "1px solid var(--color-border)",
              backgroundColor: "var(--color-surface)",
            }}
          >
            <Pencil size={15} /> Edit Profile
          </button>
          {user.isEnabled && has("user.disable") && user.id !== profile?.userId && (
            <button
              type="button"
              onClick={() => {
                if (confirm(`Disable user ${user.email}?`)) disableMutation.mutate();
              }}
              disabled={disableMutation.isPending}
              className="flex items-center gap-1.5 px-3 py-2.5 min-h-[44px] rounded-lg text-sm font-medium disabled:opacity-50"
              style={{
                color: "var(--color-danger)",
                border: "1px solid var(--color-danger)",
                backgroundColor: "var(--color-surface)",
              }}
            >
              <Power size={15} /> Disable
            </button>
          )}
        </div>
      </div>

      {/* Tab bar */}
      <div
        className="flex gap-1 rounded-lg p-1"
        style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}
        role="tablist"
        aria-label="User detail sections"
      >
        <TabButton tab="overview" current={tab} onSelect={setTab} label="Overview" />
        <TabButton tab="roles" current={tab} onSelect={setTab} label="Roles" count={user.roles.length} />
        <TabButton
          tab="grants"
          current={tab}
          onSelect={setTab}
          label="Direct Permissions"
          count={user.directPermissions.length}
        />
        {has("audit.read") && (
          <TabButton tab="activity" current={tab} onSelect={setTab} label="Activity" />
        )}
      </div>

      {/* ── Tab panels ────────────────────────────────────── */}
      {tab === "overview" && <OverviewTab user={user} />}
      {tab === "roles" && (
        <RolesTab
          user={user}
          roles={roles}
          canAssign={has("user_role.assign")}
          canRevoke={has("user_role.revoke")}
          isPlatform={isPlatform}
          assignPending={assignRoleMutation.isPending}
          revokePending={revokeRoleMutation.isPending}
          onAssign={(roleId) =>
            assignRoleMutation.mutate({ userId: user.id, roleId })
          }
          onRevoke={(roleId) => {
            if (confirm("Revoke this role?")) revokeRoleMutation.mutate(roleId);
          }}
        />
      )}
      {tab === "grants" && (
        <GrantsTab
          user={user}
          catalog={catalog}
          canRevoke={has("permission.revoke")}
          isPlatform={isPlatform}
          revokePending={revokePermissionMutation.isPending}
          onRevoke={(pid) => {
            if (confirm("Revoke this direct permission?")) revokePermissionMutation.mutate(pid);
          }}
          onGrant={(dto) => {
            grantPermissionFlow(dto);
          }}
        />
      )}
      {tab === "activity" && (
        <ActivityTab logs={activity ?? []} loading={activityLoading} />
      )}
    </div>
  );
}

function BackLink() {
  return (
    <Link
      to="/site/users"
      className="inline-flex items-center gap-1.5 text-sm font-medium"
      style={{ color: "var(--color-primary)" }}
    >
      <ArrowLeft size={15} /> Back to Users
    </Link>
  );
}

function TabButton({
  tab,
  current,
  onSelect,
  label,
  count,
}: {
  tab: Tab;
  current: Tab;
  onSelect: (t: Tab) => void;
  label: string;
  count?: number;
}) {
  const active = tab === current;
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      onClick={() => onSelect(tab)}
      className="flex-1 rounded-md px-3 py-2 text-sm font-medium min-h-[40px] transition-colors whitespace-nowrap"
      style={{
        backgroundColor: active ? "var(--color-primary)" : "transparent",
        color: active ? "var(--color-primary-foreground)" : "var(--color-muted)",
      }}
    >
      {label}
      {typeof count === "number" && ` (${count})`}
    </button>
  );
}

// ── Overview tab ─────────────────────────────────────────

function OverviewTab({ user }: { user: UserDto }) {
  return (
    <div
      className="rounded-xl p-6"
      style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}
    >
      <dl className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <Field label="Email" value={user.email} />
        <Field label="Display Name" value={user.displayName} />
        <Field label="Title" value={user.title ?? "—"} />
        <Field label="Status" value={user.isEnabled ? "Enabled" : "Disabled"} />
        <Field
          label="Last Login"
          value={user.lastLoginAt ? new Date(user.lastLoginAt).toLocaleString() : "Never"}
        />
        <Field label="Permissions Version" value={String(user.permissionsVersion)} />
        <Field label="Created" value={new Date(user.createdAt).toLocaleString()} />
        <Field label="Updated" value={new Date(user.updatedAt).toLocaleString()} />
      </dl>

      <div className="mt-6 pt-4 border-t" style={{ borderColor: "var(--color-border)" }}>
        <h3 className="text-sm font-semibold mb-2 flex items-center gap-2">
          <Shield size={14} /> Current Roles
        </h3>
        <div className="flex flex-wrap gap-1">
          {user.roles.length === 0 && (
            <span className="text-xs" style={{ color: "var(--color-muted)" }}>
              No roles assigned
            </span>
          )}
          {user.roles.map((r) => (
            <span
              key={r.roleId}
              className="text-xs px-2 py-0.5 rounded"
              style={{
                backgroundColor: "var(--color-accent)",
                color: "var(--color-accent-foreground)",
              }}
            >
              {r.roleDisplayName}
            </span>
          ))}
        </div>
      </div>
    </div>
  );
}

// ── Roles tab ────────────────────────────────────────────

function RolesTab({
  user,
  roles,
  canAssign,
  canRevoke,
  isPlatform,
  assignPending,
  revokePending,
  onAssign,
  onRevoke,
}: {
  user: UserDto;
  roles?: RoleDto[];
  canAssign: boolean;
  canRevoke: boolean;
  isPlatform: boolean;
  assignPending: boolean;
  revokePending: boolean;
  onAssign: (roleId: string) => void;
  onRevoke: (roleId: string) => void;
}) {
  const [assignRoleId, setAssignRoleId] = useState("");
  // Snapshot of "now" captured once at mount — keeps the render pure for
  // React Compiler (Date.now() directly in render is flagged impure).
  const [now] = useState(Date.now);

  const expiringSoon = (expiresAt: string | null) => {
    if (!expiresAt) return false;
    const ms = new Date(expiresAt).getTime() - now;
    return ms > 0 && ms < 30 * 24 * 60 * 60 * 1000;
  };
  const expired = (expiresAt: string | null) =>
    !!expiresAt && new Date(expiresAt).getTime() < now;

  return (
    <div
      className="rounded-xl p-6 space-y-4"
      style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}
    >
      <div className="space-y-1">
        <h3 className="text-sm font-semibold flex items-center gap-2">
          <Shield size={14} /> Role Assignments
        </h3>
        <p className="text-xs" style={{ color: "var(--color-muted)" }}>
          Role assignments can carry an expiry (max 90 days on direct grants; role
          expiries are set by the admin who assigns). Revoking is recorded in the
          audit trail.
        </p>
      </div>

      <div className="space-y-1">
        {user.roles.length === 0 && (
          <div className="text-sm" style={{ color: "var(--color-muted)" }}>
            No roles assigned.
          </div>
        )}
        {user.roles.map((r) => {
          // Only Platform Admin may revoke the site-business-admin role
          // (mirrors UsersPage guard rails).
          const revokeAllowed = canRevoke && (isPlatform || r.roleKey !== "site-business-admin");
          return (
            <div
              key={r.roleId}
              className="flex items-center justify-between p-3 rounded-md gap-3"
              style={{ backgroundColor: "var(--color-background)" }}
            >
              <div className="min-w-0">
                <div className="text-sm font-medium">{r.roleDisplayName}</div>
                <div className="text-xs font-mono" style={{ color: "var(--color-muted)" }}>
                  {r.roleKey}
                </div>
                <div className="text-xs mt-0.5" style={{ color: "var(--color-muted)" }}>
                  assigned by {r.assignedBy} ·{" "}
                  {new Date(r.assignedAt).toLocaleDateString()}
                  {r.expiresAt && <> · expires {new Date(r.expiresAt).toLocaleDateString()}</>}
                </div>
              </div>
              <div className="flex items-center gap-2 shrink-0">
                {expired(r.expiresAt) && (
                  <span
                    className="text-xs px-2 py-0.5 rounded"
                    style={{ backgroundColor: "var(--color-danger)", color: "white" }}
                  >
                    Expired
                  </span>
                )}
                {!expired(r.expiresAt) && expiringSoon(r.expiresAt) && (
                  <span
                    className="text-xs px-2 py-0.5 rounded flex items-center gap-1"
                    style={{ backgroundColor: "var(--color-warning)", color: "white" }}
                  >
                    <Clock size={10} /> Expiring
                  </span>
                )}
                {revokeAllowed ? (
                  <button
                    type="button"
                    onClick={() => onRevoke(r.roleId)}
                    disabled={revokePending}
                    className="text-xs disabled:opacity-50 min-h-[36px] px-2"
                    style={{ color: "var(--color-danger)" }}
                  >
                    Revoke
                  </button>
                ) : (
                  canRevoke && (
                    <span className="text-xs" style={{ color: "var(--color-muted)" }}>
                      Platform Admin only
                    </span>
                  )
                )}
              </div>
            </div>
          );
        })}
      </div>

      {canAssign && roles && roles.length > 0 && (
        <div className="pt-4 border-t" style={{ borderColor: "var(--color-border)" }}>
          <div className="flex gap-2">
            <select
              className="input"
              value={assignRoleId}
              onChange={(e) => setAssignRoleId(e.target.value)}
              aria-label="Role to assign"
            >
              <option value="">Select role…</option>
              {roles
                .filter((r) => isPlatform || r.key !== "site-business-admin")
                .map((r) => (
                  <option key={r.id} value={r.id}>
                    {r.displayName}
                  </option>
                ))}
            </select>
            <button
              type="button"
              onClick={() => {
                if (assignRoleId) onAssign(assignRoleId);
              }}
              disabled={assignPending || !assignRoleId}
              className="px-4 py-2.5 min-h-[44px] rounded-lg text-sm disabled:opacity-50 whitespace-nowrap"
              style={{
                backgroundColor: "var(--color-primary)",
                color: "var(--color-primary-foreground)",
              }}
            >
              Assign
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

// ── Direct grants tab ────────────────────────────────────

function GrantsTab({
  user,
  catalog,
  canRevoke,
  isPlatform,
  revokePending,
  onRevoke,
  onGrant,
}: {
  user: UserDto;
  catalog?: PermissionCatalogDto | null;
  canRevoke: boolean;
  isPlatform: boolean;
  revokePending: boolean;
  onRevoke: (userPermissionId: string) => void;
  onGrant: (dto: GrantDirectPermissionDto) => void;
}) {
  const [grantPermId, setGrantPermId] = useState("");
  const [grantReason, setGrantReason] = useState("");
  const [grantExpiry, setGrantExpiry] = useState("");

  const handleGrant = () => {
    if (!grantPermId || !grantReason || !grantExpiry) {
      toast.error("Permission, reason, and expiry are required");
      return;
    }
    onGrant({
      userId: user.id,
      permissionId: grantPermId,
      reason: grantReason,
      expiresAt: new Date(grantExpiry).toISOString(),
    });
    setGrantPermId("");
    setGrantReason("");
    setGrantExpiry("");
  };

  return (
    <div
      className="rounded-xl p-6 space-y-4"
      style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}
    >
      <div className="space-y-1">
        <h3 className="text-sm font-semibold flex items-center gap-2">
          <KeyRound size={14} /> Direct Permissions (≤ 90 days)
        </h3>
        <p className="text-xs" style={{ color: "var(--color-muted)" }}>
          Time-limited, reason-required grants layered on top of role
          permissions. Effective set = roles ∪ grants − deny.
        </p>
      </div>

      <div className="space-y-1">
        {user.directPermissions.length === 0 && (
          <div className="text-sm" style={{ color: "var(--color-muted)" }}>
            No direct permissions.
          </div>
        )}
        {user.directPermissions.map((p) => (
          <div
            key={p.id}
            className="p-3 rounded-md"
            style={{
              backgroundColor: "var(--color-background)",
              opacity: p.isRevoked ? 0.5 : 1,
            }}
          >
            <div className="flex items-center justify-between gap-2">
              <span className="font-mono text-xs">{p.permissionKey}</span>
              <span className="flex items-center gap-2">
                {p.isDeny && !p.isRevoked && (
                  <span
                    className="text-xs px-2 py-0.5 rounded"
                    style={{ backgroundColor: "var(--color-danger)", color: "white" }}
                  >
                    DENY
                  </span>
                )}
                {!p.isRevoked && canRevoke && (
                  <button
                    type="button"
                    onClick={() => onRevoke(p.id)}
                    disabled={revokePending}
                    className="text-xs disabled:opacity-50 min-h-[36px] px-1"
                    style={{ color: "var(--color-danger)" }}
                  >
                    Revoke
                  </button>
                )}
              </span>
            </div>
            <div className="text-xs mt-1" style={{ color: "var(--color-muted)" }}>
              {p.isRevoked ? "REVOKED · " : ""}
              {p.permissionDisplayName} · expires{" "}
              {new Date(p.expiresAt).toLocaleDateString()} · granted by{" "}
              {p.approvedByEmail}
            </div>
            <div className="text-xs italic mt-0.5" style={{ color: "var(--color-muted)" }}>
              “{p.reason}”
            </div>
          </div>
        ))}
      </div>

      {/* Grant form — Platform Admin only (the permission catalog needed
          to populate the select is a PlatformAdminOnly endpoint). */}
      {isPlatform && catalog && (
        <div className="pt-4 border-t space-y-2" style={{ borderColor: "var(--color-border)" }}>
          <h4 className="text-sm font-semibold">Grant New Permission</h4>
          <select
            className="input"
            value={grantPermId}
            onChange={(e) => setGrantPermId(e.target.value)}
            aria-label="Permission to grant"
          >
            <option value="">Select permission…</option>
            {catalog.groups.flatMap((g) => g.permissions).map((p) => (
              <option key={p.key} value={p.id}>
                {p.key} — {p.displayName}
              </option>
            ))}
          </select>
          <input
            className="input"
            placeholder="Reason (min 10 chars — recorded in the audit trail)"
            value={grantReason}
            onChange={(e) => setGrantReason(e.target.value)}
          />
          <div className="flex gap-2">
            <input
              type="date"
              className="input"
              value={grantExpiry}
              onChange={(e) => setGrantExpiry(e.target.value)}
              aria-label="Grant expiry date"
            />
            <button
              type="button"
              onClick={handleGrant}
              className="px-4 py-2.5 min-h-[44px] rounded-lg text-sm whitespace-nowrap"
              style={{ backgroundColor: "var(--color-warning)", color: "white" }}
            >
              Grant
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

// ── Activity tab ─────────────────────────────────────────

function ActivityTab({ logs, loading }: { logs: AuditLogDto[]; loading: boolean }) {
  const navigate = useNavigate();
  return (
    <div
      className="rounded-xl p-6"
      style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}
    >
      <div className="flex items-center justify-between mb-3">
        <h3 className="text-sm font-semibold flex items-center gap-2">
          <ScrollText size={14} /> User Activity (last {Math.min(logs.length, 50)})
        </h3>
        <button
          type="button"
          onClick={() => navigate("/audit/logs")}
          className="inline-flex items-center gap-1 text-sm font-medium"
          style={{ color: "var(--color-primary)" }}
        >
          Full audit log <ChevronRight size={14} />
        </button>
      </div>
      {loading ? (
        <div className="text-sm" style={{ color: "var(--color-muted)" }}>
          Loading…
        </div>
      ) : logs.length === 0 ? (
        <div className="text-sm" style={{ color: "var(--color-muted)" }}>
          No recorded activity for this user.
        </div>
      ) : (
        <ul className="m-0 list-none space-y-1 p-0">
          {logs.map((l) => (
            <li
              key={l.id}
              className="flex items-center gap-3 rounded-md p-2 text-sm"
              style={{ backgroundColor: "var(--color-background)" }}
            >
              {l.outcome === "success" ? (
                <CheckCircle2
                  size={14}
                  className="shrink-0"
                  aria-hidden
                  style={{ color: "var(--color-success)" }}
                />
              ) : (
                <XCircle
                  size={14}
                  className="shrink-0"
                  aria-hidden
                  style={{ color: "var(--color-danger)" }}
                />
              )}
              <span className="font-mono text-xs">{l.action}</span>
              <span className="text-xs truncate" style={{ color: "var(--color-muted)" }}>
                {l.targetType ? `${l.targetType}:${l.targetId?.slice(0, 8)}` : ""}
              </span>
              <span
                className="ml-auto whitespace-nowrap text-xs"
                style={{ color: "var(--color-muted)" }}
              >
                {new Date(l.timestamp).toLocaleString()}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

// ── Shared helpers ───────────────────────────────────────

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt
        className="text-xs font-medium uppercase tracking-wide"
        style={{ color: "var(--color-muted)" }}
      >
        {label}
      </dt>
      <dd className="mt-1 text-sm font-medium" style={{ wordBreak: "break-word" }}>
        {value}
      </dd>
    </div>
  );
}
