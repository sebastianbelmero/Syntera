import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Plus, RefreshCw, Power, Key, Shield, Clock } from "lucide-react";
import { usersApi, extractRoles } from "../../api/site";
import { roleTemplatesApi } from "../../api/platform";
import { ApiError } from "../../api/client";
import type { UserDto, UserUpsertDto, RoleDto, AssignRoleDto, GrantDirectPermissionDto, PermissionCatalogDto, UserSyncResultDto } from "../../types";

export default function UsersPage() {
  const [users, setUsers] = useState<UserDto[]>([]);
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [catalog, setCatalog] = useState<PermissionCatalogDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<UserDto | null>(null);
  const [syncing, setSyncing] = useState(false);

  const load = async () => {
    try {
      setLoading(true);
      const data = await usersApi.list();
      setUsers(data);
      setRoles(extractRoles(data));
      try {
        const cat = await roleTemplatesApi.permissionCatalog();
        setCatalog(cat);
      } catch { /* may not have access if not platform admin */ }
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Failed to load");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); }, []);

  const sync = async () => {
    if (!confirm("Trigger LDAP sync? This will create users found in LDAP and disable users no longer in LDAP.")) return;
    setSyncing(true);
    try {
      const result: UserSyncResultDto = await usersApi.sync();
      toast.success(`Sync ${result.status}: ${result.usersFound} found, ${result.usersCreated} created, ${result.usersUpdated} updated, ${result.usersDisabled} disabled`);
      load();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Sync failed");
    } finally {
      setSyncing(false);
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">Users</h1>
          <p className="text-sm" style={{ color: "var(--color-muted)" }}>
            Manage users, assign roles, grant direct permissions.
          </p>
        </div>
        <div className="flex gap-2">
          <button onClick={sync} disabled={syncing} className="flex items-center gap-1.5 px-3 py-2 rounded-lg text-sm"
            style={{ border: "1px solid var(--color-border)" }}>
            <RefreshCw size={16} className={syncing ? "animate-spin" : ""} /> Sync LDAP
          </button>
          <button onClick={() => setCreating(true)} className="flex items-center gap-1.5 px-3 py-2 rounded-lg text-sm font-medium"
            style={{ backgroundColor: "var(--color-primary)", color: "white" }}>
            <Plus size={16} /> New User
          </button>
        </div>
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
                <button onClick={() => setEditing(u)} className="p-1.5 rounded-md" style={{ border: "1px solid var(--color-border)" }}>
                  <Key size={14} />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {creating && (
        <UserDrawer onClose={() => setCreating(false)} onSaved={() => { setCreating(false); load(); }} />
      )}
      {editing && (
        <UserDrawer user={editing} roles={roles} catalog={catalog}
          onClose={() => setEditing(null)} onSaved={() => { setEditing(null); load(); }} />
      )}
    </div>
  );
}

function UserDrawer({ user, roles, catalog, onClose, onSaved }: {
  user?: UserDto;
  roles?: RoleDto[];
  catalog?: PermissionCatalogDto | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const isNew = !user;
  const [form, setForm] = useState<UserUpsertDto>({
    email: user?.email ?? "",
    displayName: user?.displayName ?? "",
    isEnabled: user?.isEnabled ?? true,
  });
  const [saving, setSaving] = useState(false);
  const [assignRoleId, setAssignRoleId] = useState("");
  const [grantPermId, setGrantPermId] = useState("");
  const [grantReason, setGrantReason] = useState("");
  const [grantExpiry, setGrantExpiry] = useState("");

  const save = async () => {
    setSaving(true);
    try {
      if (isNew) await usersApi.create(form);
      else await usersApi.update(user!.id, form);
      toast.success("Saved");
      onSaved();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Failed");
    } finally {
      setSaving(false);
    }
  };

  const disable = async () => {
    if (!confirm(`Disable user ${user!.email}?`)) return;
    try {
      await usersApi.disable(user!.id);
      toast.success("User disabled");
      onSaved();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Failed");
    }
  };

  const assignRole = async () => {
    if (!assignRoleId) return;
    const dto: AssignRoleDto = { userId: user!.id, roleId: assignRoleId };
    try {
      await usersApi.assignRole(dto);
      toast.success("Role assigned");
      onSaved();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Failed");
    }
  };

  const revokeRole = async (roleId: string) => {
    if (!confirm("Revoke this role?")) return;
    try {
      await usersApi.revokeRole({ userId: user!.id, roleId });
      toast.success("Role revoked");
      onSaved();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Failed");
    }
  };

  const grantPermission = async () => {
    if (!grantPermId || !grantReason || !grantExpiry) {
      toast.error("Permission, reason, and expiry are required");
      return;
    }
    const expiry = new Date(grantExpiry);
    const maxExpiry = new Date(Date.now() + 90 * 24 * 60 * 60 * 1000);
    if (expiry > maxExpiry) {
      toast.error("Direct permission cannot exceed 90 days");
      return;
    }
    const dto: GrantDirectPermissionDto = {
      userId: user!.id, permissionId: grantPermId,
      reason: grantReason, expiresAt: expiry.toISOString(),
    };
    try {
      await usersApi.grantPermission(dto);
      toast.success("Permission granted");
      setGrantPermId(""); setGrantReason(""); setGrantExpiry("");
      onSaved();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Failed");
    }
  };

  const revokePermission = async (id: string) => {
    if (!confirm("Revoke this direct permission?")) return;
    try {
      await usersApi.revokePermission({ userPermissionId: id });
      toast.success("Permission revoked");
      onSaved();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Failed");
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex justify-end" style={{ backgroundColor: "rgba(0,0,0,0.4)" }} onClick={onClose}>
      <div className="w-full max-w-2xl h-full overflow-y-auto p-6"
        style={{ backgroundColor: "var(--color-surface)" }}
        onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold">{isNew ? "New User" : `Edit ${user!.displayName}`}</h2>
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
            {!isNew && user!.isEnabled && (
              <button onClick={disable} className="px-3 py-2 rounded-md text-sm flex items-center gap-1"
                style={{ color: "var(--color-danger)", border: "1px solid var(--color-danger)" }}>
                <Power size={14} /> Disable
              </button>
            )}
            <button onClick={save} disabled={saving} className="px-4 py-2 rounded-md text-sm"
              style={{ backgroundColor: "var(--color-primary)", color: "white" }}>
              {saving ? "Saving..." : "Save"}
            </button>
          </div>

          {!isNew && (
            <>
              {/* Role Assignment */}
              <div className="pt-4 border-t" style={{ borderColor: "var(--color-border)" }}>
                <h4 className="text-sm font-semibold mb-2 flex items-center gap-2"><Shield size={14} /> Roles</h4>
                <div className="space-y-1 mb-3">
                  {user!.roles.length === 0 && <div className="text-xs" style={{ color: "var(--color-muted)" }}>No roles assigned</div>}
                  {user!.roles.map((r) => (
                    <div key={r.roleId} className="flex items-center justify-between p-2 rounded-md"
                      style={{ backgroundColor: "var(--color-background)" }}>
                      <div>
                        <div className="text-sm font-medium">{r.roleDisplayName}</div>
                        <div className="text-xs" style={{ color: "var(--color-muted)" }}>
                          assigned {new Date(r.assignedAt).toLocaleDateString()}
                          {r.expiresAt && <> · expires {new Date(r.expiresAt).toLocaleDateString()}</>}
                        </div>
                      </div>
                      <button onClick={() => revokeRole(r.roleId)} className="text-xs"
                        style={{ color: "var(--color-danger)" }}>Revoke</button>
                    </div>
                  ))}
                </div>
                {roles && roles.length > 0 && (
                  <div className="flex gap-2">
                    <select className="input" value={assignRoleId} onChange={(e) => setAssignRoleId(e.target.value)}>
                      <option value="">Select role...</option>
                      {roles.map((r) => <option key={r.id} value={r.id}>{r.displayName}</option>)}
                    </select>
                    <button onClick={assignRole} className="px-3 py-2 rounded-md text-sm whitespace-nowrap"
                      style={{ backgroundColor: "var(--color-primary)", color: "white" }}>Assign</button>
                  </div>
                )}
              </div>

              {/* Direct Permissions */}
              {catalog && (
                <div className="pt-4 border-t" style={{ borderColor: "var(--color-border)" }}>
                  <h4 className="text-sm font-semibold mb-2 flex items-center gap-2"><Key size={14} /> Direct Permissions (≤90 days)</h4>
                  <div className="space-y-1 mb-3">
                    {user!.directPermissions.length === 0 && <div className="text-xs" style={{ color: "var(--color-muted)" }}>No direct permissions</div>}
                    {user!.directPermissions.map((p) => (
                      <div key={p.id} className="p-2 rounded-md"
                        style={{ backgroundColor: "var(--color-background)", opacity: p.isRevoked ? 0.5 : 1 }}>
                        <div className="flex items-center justify-between">
                          <span className="font-mono text-xs">{p.permissionKey}</span>
                          {!p.isRevoked && (
                            <button onClick={() => revokePermission(p.id)} className="text-xs"
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
                      <button onClick={grantPermission} className="px-3 py-2 rounded-md text-sm"
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
