import { useEffect, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Pencil, UserPlus, Users, Shield } from "lucide-react";
import { sitesApi } from "../../api/platform";
import { ApiError } from "../../api/client";
import type { SiteDto } from "../../types";

const SITES_KEY = ["sites"] as const;

/**
 * Platform Admin → Site Management.
 *
 * The 6 sites (Kalventis, Kalbe, Fima, GOF, Dankos, Hexpharm) are
 * PRE-DEFINED in backend configuration (appsettings.json Sites[]).
 *
 * From the frontend, Platform Admin can:
 *   - Edit Display Name + Email Domains (per site)
 *   - Assign Business Admin (bootstrap first admin per site)
 *   - Manage Business Admins (list + revoke)
 *
 * Code, ConnectionString, IsEnabled, LDAP config, and Theme palette
 * are managed via backend config — NOT editable from UI.
 */
export default function SitesPage() {
  const [editSite, setEditSite] = useState<SiteDto | null>(null);
  const [adminSite, setAdminSite] = useState<SiteDto | null>(null);
  const [manageAdminSite, setManageAdminSite] = useState<SiteDto | null>(null);
  const [sysAdminSite, setSysAdminSite] = useState<SiteDto | null>(null);
  const [manageSysAdminSite, setManageSysAdminSite] = useState<SiteDto | null>(null);

  const { data: sites = [], isLoading: loading } = useQuery<SiteDto[]>({
    queryKey: SITES_KEY,
    queryFn: () => sitesApi.list(),
  });

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-2xl font-bold">Sites</h1>
        <p className="text-sm" style={{ color: "var(--color-muted)" }}>
          6 fixed sites — edit name, email domains, and manage business admins.
          LDAP config and theme are managed via backend configuration.
        </p>
      </div>

      {loading ? (
        <div className="text-center py-8" style={{ color: "var(--color-muted)" }}>Loading...</div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {sites.map((s) => (
            <SiteCard
              key={s.id}
              site={s}
              onEdit={() => setEditSite(s)}
              onAssignAdmin={() => setAdminSite(s)}
              onManageAdmins={() => setManageAdminSite(s)}
              onAssignSysAdmin={() => setSysAdminSite(s)}
              onManageSysAdmins={() => setManageSysAdminSite(s)}
            />
          ))}
        </div>
      )}

      {editSite && <SiteEditDrawer site={editSite} onClose={() => setEditSite(null)} />}
      {adminSite && <AdminDrawer site={adminSite} onClose={() => setAdminSite(null)} />}
      {manageAdminSite && <ManageAdminsDrawer site={manageAdminSite} onClose={() => setManageAdminSite(null)} />}
      {sysAdminSite && <SysAdminDrawer site={sysAdminSite} onClose={() => setSysAdminSite(null)} />}
      {manageSysAdminSite && <ManageSysAdminsDrawer site={manageSysAdminSite} onClose={() => setManageSysAdminSite(null)} />}
    </div>
  );
}

// ─── Site Card ──────────────────────────────────────────────────────

const THEME_SWATCH: Record<string, string> = {
  kalventis: "#007A4D",
  kalbe: "#E2231A",
  fima: "#6B46C1",
  gof: "#C2410C",
  dankos: "#0054A6",
  hexpharm: "#00796B",
  syntera: "#0B3D6F",
};

function SiteCard({ site, onEdit, onAssignAdmin, onManageAdmins, onAssignSysAdmin, onManageSysAdmins }: {
  site: SiteDto;
  onEdit: () => void;
  onAssignAdmin: () => void;
  onManageAdmins: () => void;
  onAssignSysAdmin: () => void;
  onManageSysAdmins: () => void;
}) {
  const swatchColor = THEME_SWATCH[site.code] ?? "#0B3D6F";

  return (
    <div className="rounded-xl p-5"
      style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}>
      <div className="flex items-start gap-3 mb-3">
        <div className="w-10 h-10 rounded-lg flex-shrink-0" style={{ backgroundColor: swatchColor }} />
        <div className="min-w-0 flex-1">
          <h3 className="font-semibold text-lg truncate">{site.displayName}</h3>
          <div className="text-xs mt-0.5 font-mono" style={{ color: "var(--color-muted)" }}>
            {site.code}
          </div>
        </div>
      </div>

      <div className="text-xs space-y-1 mb-4">
        <div>
          <strong>Domains:</strong>{" "}
          {site.ldapDomains.length > 0 ? site.ldapDomains.join(", ") : "—"}
        </div>
      </div>

      <div className="grid grid-cols-1 gap-2">
        <button onClick={onEdit} type="button"
          className="flex items-center justify-center gap-1.5 px-2 py-2.5 rounded-lg text-sm min-h-[44px] transition hover:opacity-80"
          style={{ border: "1px solid var(--color-border)" }}>
          <Pencil size={16} /> Edit Name &amp; Domains
        </button>
        <button onClick={onManageSysAdmins} type="button"
          className="flex items-center justify-center gap-1.5 px-2 py-2.5 rounded-lg text-sm min-h-[44px] transition hover:opacity-80"
          style={{ border: "1px solid var(--color-border)" }}>
          <Shield size={16} /> System Admins
        </button>
        <button onClick={onAssignSysAdmin} type="button"
          className="flex items-center justify-center gap-1.5 px-2 py-2.5 rounded-lg text-sm min-h-[44px] transition hover:opacity-80"
          style={{ backgroundColor: "var(--color-primary)", color: "var(--color-primary-foreground)" }}>
          <Shield size={16} /> Add System Admin
        </button>
        <button onClick={onManageAdmins} type="button"
          className="flex items-center justify-center gap-1.5 px-2 py-2.5 rounded-lg text-sm min-h-[44px] transition hover:opacity-80"
          style={{ border: "1px solid var(--color-border)" }}>
          <Users size={16} /> Business Admins
        </button>
        <button onClick={onAssignAdmin} type="button"
          className="flex items-center justify-center gap-1.5 px-2 py-2.5 rounded-lg text-sm min-h-[44px] transition hover:opacity-80"
          style={{ backgroundColor: "var(--color-accent)", color: "var(--color-accent-foreground)" }}>
          <UserPlus size={16} /> Add Business Admin
        </button>
      </div>
    </div>
  );
}

// ─── Edit Site (DisplayName + Email Domains) ────────────────────────

function SiteEditDrawer({ site, onClose }: { site: SiteDto; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [displayName, setDisplayName] = useState(site.displayName);
  const [domains, setDomains] = useState<string[]>(site.ldapDomains);
  const [domainInput, setDomainInput] = useState("");

  const saveMutation = useMutation({
    mutationFn: () => sitesApi.update(site.id, { displayName, ldapDomains: domains }),
    onSuccess: () => {
      toast.success("Site updated");
      void queryClient.invalidateQueries({ queryKey: SITES_KEY });
      onClose();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  return (
    <Drawer title={`Edit — ${site.code}`} onClose={onClose}>
      <div className="space-y-4">
        <div className="p-3 rounded-md text-xs" style={{ backgroundColor: "var(--color-background)" }}>
          <strong>Code:</strong> {site.code} (locked)<br />
          <strong>Connection:</strong> managed via backend config
        </div>

        <Field label="Display Name">
          <input className="input" value={displayName}
            onChange={(e) => setDisplayName(e.target.value)} />
        </Field>

        <Field label="Email Domains">
          <div className="flex flex-wrap gap-2 mb-2">
            {domains.map((d, i) => (
              <span key={i} className="px-2 py-1 rounded-md text-xs flex items-center gap-1"
                style={{ backgroundColor: "var(--color-background)", border: "1px solid var(--color-border)" }}>
                {d}
                <button type="button" onClick={() => setDomains(domains.filter((_, j) => j !== i))}
                  aria-label={`Remove domain ${d}`}
                  className="ml-1 inline-flex items-center justify-center rounded-full hover:opacity-70 transition-opacity"
                  style={{ minWidth: "24px", minHeight: "24px" }}>×</button>
              </span>
            ))}
          </div>
          <div className="flex gap-2">
            <input className="input" value={domainInput}
              onChange={(e) => setDomainInput(e.target.value.toLowerCase())}
              placeholder={`${site.code}.com`} />
            <button type="button" onClick={() => {
              const d = domainInput.trim();
              if (d && !domains.includes(d)) {
                setDomains([...domains, d]);
                setDomainInput("");
              }
            }} className="px-3 py-2.5 rounded-lg text-sm min-h-[44px]"
              style={{ backgroundColor: "var(--color-primary)", color: "var(--color-primary-foreground)" }}>
              Add
            </button>
          </div>
        </Field>

        <div className="flex justify-end gap-2 pt-4 sticky bottom-0" style={{ backgroundColor: "var(--color-surface)" }}>
          <button type="button" onClick={onClose} className="px-4 py-2.5 rounded-lg text-sm min-h-[44px]"
            style={{ border: "1px solid var(--color-border)" }}>Cancel</button>
          <button type="button" onClick={() => saveMutation.mutate()} disabled={saveMutation.isPending}
            className="px-4 py-2.5 rounded-lg text-sm min-h-[44px] disabled:opacity-50"
            style={{ backgroundColor: "var(--color-primary)", color: "var(--color-primary-foreground)" }}>
            {saveMutation.isPending ? "Saving..." : "Save"}
          </button>
        </div>
      </div>
    </Drawer>
  );
}

// ─── Manage Business Admins (list + revoke) ─────────────────────────

function ManageAdminsDrawer({ site, onClose }: { site: SiteDto; onClose: () => void }) {
  const queryClient = useQueryClient();

  const { data: admins = [], isLoading } = useQuery({
    queryKey: ["business-admins", site.id],
    queryFn: () => sitesApi.listBusinessAdmins(site.id),
  });

  const revokeMutation = useMutation({
    mutationFn: (userId: string) => sitesApi.revokeBusinessAdmin(site.id, userId),
    onSuccess: () => {
      toast.success("Business admin revoked");
      void queryClient.invalidateQueries({ queryKey: ["business-admins", site.id] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  const handleRevoke = (userId: string, email: string) => {
    if (!confirm(`Revoke business admin role from ${email}?\n\nThe user will still exist but lose admin privileges.`)) return;
    revokeMutation.mutate(userId);
  };

  return (
    <Drawer title={`Business Admins — ${site.displayName}`} onClose={onClose}>
      <div className="space-y-3">
        <div className="p-3 rounded-md text-xs" style={{ backgroundColor: "var(--color-background)" }}>
          <strong>Site:</strong> {site.displayName} ({site.code})<br />
          <strong>Domains:</strong> {site.ldapDomains.join(", ")}
        </div>

        <div>
          <h4 className="text-sm font-semibold mb-2">
            Current Business Admins ({admins.length})
          </h4>

          {isLoading ? (
            <div className="text-center py-4 text-sm" style={{ color: "var(--color-muted)" }}>
              Loading...
            </div>
          ) : admins.length === 0 ? (
            <div className="p-4 rounded-md text-center text-sm"
              style={{ backgroundColor: "var(--color-background)", color: "var(--color-muted)" }}>
              No business admins yet. Use "Add Business Admin" button to assign one.
            </div>
          ) : (
            <div className="space-y-2">
              {admins.map((admin) => (
                <div key={admin.id}
                  className="p-3 rounded-md flex items-center justify-between"
                  style={{ backgroundColor: "var(--color-background)" }}>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2">
                      <div className="w-8 h-8 rounded-full flex items-center justify-center text-white text-xs font-semibold flex-shrink-0"
                        style={{ backgroundColor: "var(--color-primary)" }}>
                        {admin.displayName.charAt(0).toUpperCase()}
                      </div>
                      <div className="min-w-0">
                        <div className="text-sm font-medium truncate">{admin.displayName}</div>
                        <div className="text-xs truncate" style={{ color: "var(--color-muted)" }}>
                          {admin.email}
                        </div>
                      </div>
                    </div>
                    <div className="text-xs mt-1" style={{ color: "var(--color-muted)" }}>
                      {admin.isEnabled ? "✓ Active" : "✗ Disabled"}
                      {admin.lastLoginAt && ` · Last login: ${new Date(admin.lastLoginAt).toLocaleDateString()}`}
                    </div>
                  </div>
                  <button
                    type="button"
                    onClick={() => handleRevoke(admin.id, admin.email)}
                    disabled={revokeMutation.isPending}
                    className="px-2 py-1.5 rounded-md text-xs flex-shrink-0 ml-2 disabled:opacity-50 min-h-[36px]"
                    style={{ color: "var(--color-danger)", border: "1px solid var(--color-danger)" }}
                  >
                    Revoke
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="pt-4" style={{ borderTop: "1px solid var(--color-border)" }}>
          <p className="text-xs" style={{ color: "var(--color-muted)" }}>
            Revoking only removes the business admin role. The user account remains
            and can still log in (if they have other roles or are a viewer).
          </p>
        </div>
      </div>
    </Drawer>
  );
}

// ─── Assign Business Admin (Platform Admin bootstrap) ───────────────

function AdminDrawer({ site, onClose }: { site: SiteDto; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");

  const domainHint = site.ldapDomains[0] ?? "example.com";

  const assignMutation = useMutation({
    mutationFn: () => sitesApi.assignBusinessAdmin(site.id, email, displayName || undefined),
    onSuccess: (user) => {
      toast.success(`Assigned business admin: ${user.email}`);
      void queryClient.invalidateQueries({ queryKey: SITES_KEY });
      onClose();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  return (
    <Drawer title={`Business Admin — ${site.displayName}`} onClose={onClose}>
      <div className="space-y-3">
        <div className="p-3 rounded-md text-xs" style={{ backgroundColor: "var(--color-background)" }}>
          <strong>Site:</strong> {site.displayName} ({site.code})<br />
          <strong>Domains:</strong> {site.ldapDomains.join(", ")}
        </div>

        <div className="p-3 rounded-md text-xs" style={{ backgroundColor: "var(--color-warning)", color: "white", opacity: 0.9 }}>
          This creates the user (if not exists) and assigns the
          <code> site-business-admin</code> role. The user must already exist
          in the site's LDAP directory — they will set their password via LDAP,
          not via this app.
        </div>

        <Field label="Email (must match LDAP userPrincipalName)">
          <input className="input" type="email" value={email}
            onChange={(e) => setEmail(e.target.value.toLowerCase())}
            placeholder={`admin.user@${domainHint}`} />
        </Field>

        <Field label="Display Name (optional — will fetch from LDAP on first login)">
          <input className="input" value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            placeholder="Budi Santoso" />
        </Field>

        <div className="flex justify-end gap-2 pt-4 sticky bottom-0" style={{ backgroundColor: "var(--color-surface)" }}>
          <button type="button" onClick={onClose} className="px-4 py-2.5 rounded-lg text-sm min-h-[44px]"
            style={{ border: "1px solid var(--color-border)" }}>Cancel</button>
          <button
            type="button"
            onClick={() => assignMutation.mutate()}
            disabled={assignMutation.isPending || !email}
            className="px-4 py-2.5 rounded-lg text-sm disabled:opacity-50 min-h-[44px]"
            style={{ backgroundColor: "var(--color-primary)", color: "var(--color-primary-foreground)" }}
          >
            {assignMutation.isPending ? "Assigning..." : "Assign Business Admin"}
          </button>
        </div>
      </div>
    </Drawer>
  );
}

// ─── System Admin Drawers ──────────────────────────────────────────

function SysAdminDrawer({ site, onClose }: { site: SiteDto; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const domainHint = site.ldapDomains[0] ?? "example.com";

  const assignMutation = useMutation({
    mutationFn: () => sitesApi.assignSystemAdmin(site.id, email, displayName || undefined),
    onSuccess: (user) => {
      toast.success(`Assigned System Admin: ${user.email}`);
      void queryClient.invalidateQueries({ queryKey: SITES_KEY });
      onClose();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  return (
    <Drawer title={`System Admin — ${site.displayName}`} onClose={onClose}>
      <div className="space-y-3">
        <div className="p-3 rounded-md text-xs" style={{ backgroundColor: "var(--color-background)" }}>
          <strong>Site:</strong> {site.displayName} ({site.code})<br />
          <strong>Domains:</strong> {site.ldapDomains.join(", ")}
        </div>

        <div className="p-3 rounded-md text-xs" style={{ backgroundColor: "var(--color-primary)", color: "var(--color-primary-foreground)", opacity: 0.9 }}>
          System Admin can assign Business Admins for this site.
          The user must already exist in the site's LDAP directory.
        </div>

        <Field label="Email (must match LDAP userPrincipalName)">
          <input className="input" type="email" value={email}
            onChange={(e) => setEmail(e.target.value.toLowerCase())}
            placeholder={`admin.user@${domainHint}`} />
        </Field>

        <Field label="Display Name (optional)">
          <input className="input" value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            placeholder="Budi Santoso" />
        </Field>

        <div className="flex justify-end gap-2 pt-4 sticky bottom-0" style={{ backgroundColor: "var(--color-surface)" }}>
          <button type="button" onClick={onClose} className="px-4 py-2.5 rounded-lg text-sm min-h-[44px]"
            style={{ border: "1px solid var(--color-border)" }}>Cancel</button>
          <button type="button" onClick={() => assignMutation.mutate()}
            disabled={assignMutation.isPending || !email}
            className="px-4 py-2.5 rounded-lg text-sm disabled:opacity-50 min-h-[44px]"
            style={{ backgroundColor: "var(--color-primary)", color: "var(--color-primary-foreground)" }}>
            {assignMutation.isPending ? "Assigning..." : "Assign System Admin"}
          </button>
        </div>
      </div>
    </Drawer>
  );
}

function ManageSysAdminsDrawer({ site, onClose }: { site: SiteDto; onClose: () => void }) {
  const queryClient = useQueryClient();

  const { data: admins = [], isLoading } = useQuery({
    queryKey: ["system-admins", site.id],
    queryFn: () => sitesApi.listSystemAdmins(site.id),
  });

  const revokeMutation = useMutation({
    mutationFn: (userId: string) => sitesApi.revokeSystemAdmin(site.id, userId),
    onSuccess: () => {
      toast.success("System Admin revoked");
      void queryClient.invalidateQueries({ queryKey: ["system-admins", site.id] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  return (
    <Drawer title={`System Admins — ${site.displayName}`} onClose={onClose}>
      <div className="space-y-3">
        <div className="p-3 rounded-md text-xs" style={{ backgroundColor: "var(--color-background)" }}>
          <strong>Site:</strong> {site.displayName} ({site.code})<br />
          <strong>Domains:</strong> {site.ldapDomains.join(", ")}
        </div>

        <div>
          <h4 className="text-sm font-semibold mb-2">
            System Administrators ({admins.length})
          </h4>

          {isLoading ? (
            <div className="text-center py-4 text-sm" style={{ color: "var(--color-muted)" }}>Loading...</div>
          ) : admins.length === 0 ? (
            <div className="p-4 rounded-md text-center text-sm"
              style={{ backgroundColor: "var(--color-background)", color: "var(--color-muted)" }}>
              No System Admins yet. Use "Add System Admin" to assign one.
            </div>
          ) : (
            <div className="space-y-2">
              {admins.map((admin) => (
                <div key={admin.id} className="p-3 rounded-md flex items-center justify-between"
                  style={{ backgroundColor: "var(--color-background)" }}>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2">
                      <div className="w-8 h-8 rounded-full flex items-center justify-center text-white text-xs font-semibold flex-shrink-0"
                        style={{ backgroundColor: "var(--color-primary)" }}>
                        {admin.displayName.charAt(0).toUpperCase()}
                      </div>
                      <div className="min-w-0">
                        <div className="text-sm font-medium truncate">{admin.displayName}</div>
                        <div className="text-xs truncate" style={{ color: "var(--color-muted)" }}>
                          {admin.email}
                        </div>
                      </div>
                    </div>
                  </div>
                  <button type="button"
                    onClick={() => {
                      if (confirm(`Revoke System Admin from ${admin.email}?`)) revokeMutation.mutate(admin.id);
                    }}
                    disabled={revokeMutation.isPending}
                    className="px-2 py-1.5 rounded-md text-xs flex-shrink-0 ml-2 disabled:opacity-50 min-h-[36px]"
                    style={{ color: "var(--color-danger)", border: "1px solid var(--color-danger)" }}>
                    Revoke
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </Drawer>
  );
}

// ─── Helpers ────────────────────────────────────────────────────────

function Drawer({ title, onClose, children }: { title: string; onClose: () => void; children: React.ReactNode }) {
  useEffect(() => {
    const handleEsc = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    document.addEventListener("keydown", handleEsc);
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", handleEsc);
      document.body.style.overflow = "";
    };
  }, [onClose]);

  return (
    <div className="fixed inset-0 z-50 flex justify-end syntera-drawer-backdrop" style={{ backgroundColor: "rgba(0,0,0,0.4)" }} onClick={onClose}>
      <div className="w-full max-w-md h-full flex flex-col syntera-drawer-panel"
        style={{ backgroundColor: "var(--color-surface)" }}
        onClick={(e) => e.stopPropagation()}>
        {/* Sticky header */}
        <div className="flex items-center justify-between px-6 pt-6 pb-3 shrink-0 sticky top-0 z-10"
          style={{ backgroundColor: "var(--color-surface)", borderBottom: "1px solid var(--color-border)" }}>
          <h2 className="text-lg font-semibold">{title}</h2>
          <button onClick={onClose} className="text-2xl leading-none p-2 rounded-lg hover:opacity-70 transition-opacity min-h-[40px] min-w-[40px] flex items-center justify-center" aria-label="Close">×</button>
        </div>
        {/* Scrollable content */}
        <div className="flex-1 overflow-y-auto px-6 pb-6">
          {children}
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
