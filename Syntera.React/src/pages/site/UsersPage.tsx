import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Plus, Power, Key, Shield, Clock } from "lucide-react";
import { usersApi } from "../../api/site";
import { roleTemplatesApi } from "../../api/platform";
import { ApiError } from "../../api/client";
import { useAuthStore } from "../../store/authStore";
import type { UserDto, UserUpsertDto, RoleDto, AssignRoleDto, GrantDirectPermissionDto, PermissionCatalogDto } from "../../types";

const USERS_KEY = ["site-users"] as const;

export default function UsersPage() {
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<UserDto | null>(null);
  const currentUserId = useAuthStore((s) => s.profile?.userId);
  const isPlatformAdmin = useAuthStore((s) => s.profile?.roles.includes("platform-admin") ?? false);

  const { data: users = [], isLoading: loading } = useQuery<UserDto[]>({
    queryKey: USERS_KEY,
    queryFn: () => usersApi.list(),
  });

  // Fetch roles from dedicated endpoint (auto-clones from templates).
  const { data: roles = [] } = useQuery<RoleDto[]>({
    queryKey: ["site-roles"],
    queryFn: () => usersApi.listRoles(),
  });

  const { data: catalog } = useQuery<PermissionCatalogDto>({
    queryKey: ["permission-catalog"],
    queryFn: () => roleTemplatesApi.permissionCatalog(),
    enabled: isPlatformAdmin, // Only fetch for platform admin (endpoint is PlatformAdminOnly)
    retry: false,
  });

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">Users</h1>
          <p className="text-sm" style={{ color: "var(--color-muted)" }}>
            Manage users, assign roles, grant direct permissions.
            Users must be created here before they can log in via LDAP.
          </p>
        </div>
        <button onClick={() => setCreating(true)} className="flex items-center gap-1.5 px-3 py-2 rounded-lg text-sm font-medium"
          style={{ backgroundColor: "var(--color-primary)", color: "white" }}>
          <Plus size={16} /> New User
        </button>
      </div>

      {loading ? (
        <div className="text-center py-8" style={{ color: "var(--color-muted)" }}>Loading...</div>
      ) : (
        <div className="space-y-2">
          {users.map((u) => (
            <div key={u.id} className="rounded-lg p-3 flex items-center justify-between"
              style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}>
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 rounded-full flex items-center justify-center text-white text-sm font-semibold"
                  style={{ backgroundColor: "var(--color-primary)" }}>
                  {u.displayName.charAt(0).toUpperCase()}
                </div>
                <div>
                  <div className="flex items-center gap-2">
                    <span className="font-medium">{u.displayName}</span>
                    {!u.isEnabled && (
                      <span className="text-xs px-2 py-0.5 rounded-full"
                        style={{ backgroundColor: "var(--color-danger)", color: "white" }}>Disabled</span>
                    )}
                  </div>
                  <div className="text-xs" style={{ color: "var(--color-muted)" }}>{u.email}</div>
                </div>
              </div>
              <div className="flex items-center gap-2">
                <div className="flex flex-wrap gap-1 justify-end max-w-md">
                  {u.roles.map((r) => (
                    <span key={r.roleId} className="text-xs px-2 py-0.5 rounded"
                      style={{ backgroundColor: "var(--color-accent)", color: "white" }}>{r.roleDisplayName}</span>
                  ))}
                  {u.directPermissions.filter(p => !p.isRevoked).length > 0 && (
                    <span className="text-xs px-2 py-0.5 rounded flex items-center gap-1"
                      style={{ backgroundColor: "var(--color-warning)", color: "white" }}>
                      <Clock size={10} /> {u.directPermissions.filter(p => !p.isRevoked).length} direct
                    </span>
                  )}
                </div>
                {u.id !== currentUserId && (
                  <button onClick={() => setEditing(u)} className="p-1.5 rounded-md" style={{ border: "1px solid var(--color-border)" }}>
                    <Key size={14} />
                  </button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {creating && (
        <UserDrawer onClose={() => setCreating(false)} />
      )}
      {editing && (
        <UserDrawer user={editing} roles={roles} catalog={catalog}
          isPlatformAdmin={isPlatformAdmin}
          onClose={() => setEditing(null)} />
      )}
    </div>
  );
}

function UserDrawer({ user, roles, catalog, isPlatformAdmin, onClose }: {
  user?: UserDto;
  roles?: RoleDto[];
  catalog?: PermissionCatalogDto | null;
  isPlatformAdmin?: boolean;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const isNew = !user;
  const [form, setForm] = useState<UserUpsertDto>({
    email: user?.email ?? "",
    displayName: user?.displayName ?? "",
    isEnabled: user?.isEnabled ?? true,
  });
  const [assignRoleId, setAssignRoleId] = useState("");
  const [grantPermId, setGrantPermId] = useState("");
  const [grantReason, setGrantReason] = useState("");
  const [grantExpiry, setGrantExpiry] = useState("");

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: USERS_KEY });
  };

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (isNew) return usersApi.create(form);
      if (!user) throw new Error("No user selected");
      return usersApi.update(user.id, form);
    },
    onSuccess: () => {
      toast.success("Saved");
      invalidate();
      onClose();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  const disableMutation = useMutation({
    mutationFn: () => {
      if (!user) throw new Error("No user selected");
      return usersApi.disable(user.id);
    },
    onSuccess: () => {
      toast.success("User disabled");
      invalidate();
      onClose();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  const assignRoleMutation = useMutation({
    mutationFn: (dto: AssignRoleDto) => usersApi.assignRole(dto),
    onSuccess: () => {
      toast.success("Role assigned");
      invalidate();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  const revokeRoleMutation = useMutation({
    mutationFn: (roleId: string) => usersApi.revokeRole({ userId: user?.id ?? "", roleId }),
    onSuccess: () => {
      toast.success("Role revoked");
      invalidate();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  const grantPermissionMutation = useMutation({
    mutationFn: (dto: GrantDirectPermissionDto) => usersApi.grantPermission(dto),
    onSuccess: () => {
      toast.success("Permission granted");
      setGrantPermId(""); setGrantReason(""); setGrantExpiry("");
      invalidate();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  const revokePermissionMutation = useMutation({
    mutationFn: (id: string) => usersApi.revokePermission({ userPermissionId: id }),
    onSuccess: () => {
      toast.success("Permission revoked");
      invalidate();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  const handleDisable = () => {
    if (!user) return;
    if (!confirm(`Disable user ${user.email}?`)) return;
    disableMutation.mutate();
  };

  const handleAssignRole = () => {
    if (!assignRoleId || !user) return;
    assignRoleMutation.mutate({ userId: user.id, roleId: assignRoleId });
  };

  const handleRevokeRole = (roleId: string) => {
    if (!confirm("Revoke this role?")) return;
    revokeRoleMutation.mutate(roleId);
  };

  const handleGrantPermission = () => {
    if (!grantPermId || !grantReason || !grantExpiry || !user) {
      toast.error("Permission, reason, and expiry are required");
      return;
    }
    const expiry = new Date(grantExpiry);
    const maxExpiry = new Date(Date.now() + 90 * 24 * 60 * 60 * 1000);
    if (expiry > maxExpiry) {
      toast.error("Direct permission cannot exceed 90 days");
      return;
    }
    grantPermissionMutation.mutate({
      userId: user.id, permissionId: grantPermId,
      reason: grantReason, expiresAt: expiry.toISOString(),
    });
  };

  const handleRevokePermission = (id: string) => {
    if (!confirm("Revoke this direct permission?")) return;
    revokePermissionMutation.mutate(id);
  };

  return (
    <div className="fixed inset-0 z-50 flex justify-end syntera-drawer-backdrop" style={{ backgroundColor: "rgba(0,0,0,0.4)" }} onClick={onClose}>
      <div className="w-full max-w-2xl h-full overflow-y-auto p-6 syntera-drawer-panel"
        style={{ backgroundColor: "var(--color-surface)" }}
        onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold">{isNew ? "New User" : `Edit ${user?.displayName ?? ""}`}</h2>
          <button onClick={onClose} className="text-2xl leading-none">×</button>
        </div>

        <div className="space-y-4">
          <Field label="Email"><input className="input" value={form.email} disabled={!isNew}
            onChange={(e) => setForm({ ...form, email: e.target.value })} placeholder="user@kalventis.com" /></Field>
          <Field label="Display Name"><input className="input" value={form.displayName}
            onChange={(e) => setForm({ ...form, displayName: e.target.value })} /></Field>
          <label className="flex items-center gap-2">
            <input type="checkbox" checked={form.isEnabled}
              onChange={(e) => setForm({ ...form, isEnabled: e.target.checked })} />
            <span className="text-sm">Enabled</span>
          </label>

          <div className="flex justify-end gap-2">
            {!isNew && user?.isEnabled && (
              <button onClick={handleDisable} disabled={disableMutation.isPending}
                className="px-3 py-2 rounded-md text-sm flex items-center gap-1 disabled:opacity-50"
                style={{ color: "var(--color-danger)", border: "1px solid var(--color-danger)" }}>
                <Power size={14} /> Disable
              </button>
            )}
            <button onClick={() => saveMutation.mutate()} disabled={saveMutation.isPending}
              className="px-4 py-2 rounded-md text-sm disabled:opacity-50"
              style={{ backgroundColor: "var(--color-primary)", color: "white" }}>
              {saveMutation.isPending ? "Saving..." : "Save"}
            </button>
          </div>

          {!isNew && user && (
            <>
              {/* Role Assignment */}
              <div className="pt-4 border-t" style={{ borderColor: "var(--color-border)" }}>
                <h4 className="text-sm font-semibold mb-2 flex items-center gap-2"><Shield size={14} /> Roles</h4>
                <div className="space-y-1 mb-3">
                  {user.roles.length === 0 && <div className="text-xs" style={{ color: "var(--color-muted)" }}>No roles assigned</div>}
                  {user.roles.map((r) => {
                    const canRevoke = isPlatformAdmin || r.roleKey !== "site-business-admin";
                    return (
                      <div key={r.roleId} className="flex items-center justify-between p-2 rounded-md"
                        style={{ backgroundColor: "var(--color-background)" }}>
                        <div>
                          <div className="text-sm font-medium">{r.roleDisplayName}</div>
                          <div className="text-xs" style={{ color: "var(--color-muted)" }}>
                            assigned {new Date(r.assignedAt).toLocaleDateString()}
                            {r.expiresAt && <> · expires {new Date(r.expiresAt).toLocaleDateString()}</>}
                          </div>
                        </div>
                        {canRevoke ? (
                          <button onClick={() => handleRevokeRole(r.roleId)} disabled={revokeRoleMutation.isPending}
                            className="text-xs disabled:opacity-50"
                            style={{ color: "var(--color-danger)" }}>Revoke</button>
                        ) : (
                          <span className="text-xs" style={{ color: "var(--color-muted)" }}>Platform Admin only</span>
                        )}
                      </div>
                    );
                  })}
                </div>
                {roles && roles.length > 0 && (
                  <div className="flex gap-2">
                    <select className="input" value={assignRoleId} onChange={(e) => setAssignRoleId(e.target.value)}>
                      <option value="">Select role...</option>
                      {roles
                        .filter((r) => isPlatformAdmin || r.key !== "site-business-admin")
                        .map((r) => <option key={r.id} value={r.id}>{r.displayName}</option>)}
                    </select>
                    <button onClick={handleAssignRole} disabled={assignRoleMutation.isPending}
                      className="px-3 py-2 rounded-md text-sm whitespace-nowrap disabled:opacity-50"
                      style={{ backgroundColor: "var(--color-primary)", color: "white" }}>Assign</button>
                  </div>
                )}
              </div>

              {/* Direct Permissions */}
              {catalog && (
                <div className="pt-4 border-t" style={{ borderColor: "var(--color-border)" }}>
                  <h4 className="text-sm font-semibold mb-2 flex items-center gap-2"><Key size={14} /> Direct Permissions (≤90 days)</h4>
                  <div className="space-y-1 mb-3">
                    {user.directPermissions.length === 0 && <div className="text-xs" style={{ color: "var(--color-muted)" }}>No direct permissions</div>}
                    {user.directPermissions.map((p) => (
                      <div key={p.id} className="p-2 rounded-md"
                        style={{ backgroundColor: "var(--color-background)", opacity: p.isRevoked ? 0.5 : 1 }}>
                        <div className="flex items-center justify-between">
                          <span className="font-mono text-xs">{p.permissionKey}</span>
                          {!p.isRevoked && (
                            <button onClick={() => handleRevokePermission(p.id)} disabled={revokePermissionMutation.isPending}
                              className="text-xs disabled:opacity-50"
                              style={{ color: "var(--color-danger)" }}>Revoke</button>
                          )}
                        </div>
                        <div className="text-xs" style={{ color: "var(--color-muted)" }}>
                          {p.isRevoked ? "REVOKED · " : ""}expires {new Date(p.expiresAt).toLocaleDateString()} · "{p.reason}"
                        </div>
                      </div>
                    ))}
                  </div>
                  <div className="space-y-2">
                    <select className="input" value={grantPermId} onChange={(e) => setGrantPermId(e.target.value)}>
                      <option value="">Select permission...</option>
                      {catalog.groups.flatMap(g => g.permissions).map((p) => (
                        <option key={p.key} value={p.id}>{p.key} — {p.displayName}</option>
                      ))}
                    </select>
                    <input className="input" placeholder="Reason (min 10 chars)" value={grantReason}
                      onChange={(e) => setGrantReason(e.target.value)} />
                    <div className="flex gap-2">
                      <input type="date" className="input" value={grantExpiry}
                        onChange={(e) => setGrantExpiry(e.target.value)} />
                      <button onClick={handleGrantPermission} disabled={grantPermissionMutation.isPending}
                        className="px-3 py-2 rounded-md text-sm disabled:opacity-50"
                        style={{ backgroundColor: "var(--color-warning)", color: "white" }}>Grant</button>
                    </div>
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1">
      <label className="text-xs font-medium" style={{ color: "var(--color-muted)" }}>{label}</label>
      {children}
    </div>
  );
}
