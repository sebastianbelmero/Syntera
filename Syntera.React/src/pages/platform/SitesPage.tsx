import { useEffect, useRef, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Pencil, UserPlus, Users, Shield, MoreVertical, KeyRound } from "lucide-react";
import { sitesApi } from "../../api/platform";
import { ApiError } from "../../api/client";
import { useAuthStore } from "../../store/authStore";
import type { SiteDto, UserDto, LdapConfigDto, LdapConfigUpsertDto, LdapTestRequest, LdapTestResult } from "../../types";

const SITES_KEY = ["sites"] as const;

const THEME_SWATCH: Record<string, string> = {
  kalventis: "#007A4D",
  kalbe: "#E2231A",
  fima: "#6B46C1",
  gof: "#C2410C",
  dankos: "#0054A6",
  hexpharm: "#00796B",
  syntera: "#0B3D6F",
};

/**
 * Platform Admin → Site Management (Table Layout)
 *
 * Role-based visibility:
 *   - Platform Admin sees: Edit, System Admins, Add System Admin, LDAP, Business Admins (view only)
 *   - System Admin sees: Business Admins, Add Business Admin (their site only)
 *
 * Responsive: Desktop table → Tablet condensed → Mobile stacked cards
 */
export default function SitesPage() {
  const [editSite, setEditSite] = useState<SiteDto | null>(null);
  const [adminSite, setAdminSite] = useState<SiteDto | null>(null);
  const [manageAdminSite, setManageAdminSite] = useState<SiteDto | null>(null);
  const [sysAdminSite, setSysAdminSite] = useState<SiteDto | null>(null);
  const [manageSysAdminSite, setManageSysAdminSite] = useState<SiteDto | null>(null);
  const [ldapSite, setLdapSite] = useState<SiteDto | null>(null);
  const [actionMenu, setActionMenu] = useState<string | null>(null);

  const profile = useAuthStore((s) => s.profile);
  const isPlatformAdmin = profile?.roles.includes("platform-admin") ?? false;
  const isSystemAdmin = profile?.roles.includes("system-admin") ?? false;

  const { data: allSites = [], isLoading: loading } = useQuery<SiteDto[]>({
    queryKey: SITES_KEY,
    queryFn: () => sitesApi.list(),
  });

  // System Admin only sees their own site; Platform Admin sees all
  const sites = isPlatformAdmin
    ? allSites
    : allSites.filter((s) => s.id === profile?.siteId);

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-2xl font-bold">Sites</h1>
        <p className="text-sm mt-1" style={{ color: "var(--color-muted)" }}>
          {isPlatformAdmin
            ? "6 fixed sites — manage System Admins, LDAP config, and view Business Admins."
            : "Manage Business Admins for your site."}
        </p>
      </div>

      {loading ? (
        <div className="text-center py-12" style={{ color: "var(--color-muted)" }}>Loading...</div>
      ) : (
        <>
          {/* ─── Desktop/Tablet: Table ─── */}
          <div className="hidden sm:block overflow-x-auto rounded-xl"
            style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}>
            <table className="w-full text-sm">
              <thead>
                <tr style={{ borderBottom: "1px solid var(--color-border)" }}>
                  <th className="text-left px-4 py-3 font-semibold" style={{ color: "var(--color-muted)" }}>Site</th>
                  <th className="text-left px-4 py-3 font-semibold hidden md:table-cell" style={{ color: "var(--color-muted)" }}>Code</th>
                  <th className="text-left px-4 py-3 font-semibold hidden lg:table-cell" style={{ color: "var(--color-muted)" }}>Email Domains</th>
                  <th className="text-left px-4 py-3 font-semibold hidden lg:table-cell" style={{ color: "var(--color-muted)" }}>Status</th>
                  <th className="text-right px-4 py-3 font-semibold" style={{ color: "var(--color-muted)" }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {sites.map((s) => {
                  // System Admin can only see their own site
                  const canManageThisSite = isPlatformAdmin || (isSystemAdmin && s.id === profile?.siteId);

                  return (
                    <tr key={s.id} className="transition-colors hover:opacity-80"
                      style={{ borderBottom: "1px solid var(--color-border)" }}>
                      <td className="px-4 py-3">
                        <div className="flex items-center gap-3">
                          <div className="w-9 h-9 rounded-lg flex-shrink-0"
                            style={{ backgroundColor: THEME_SWATCH[s.code] ?? "#0B3D6F" }} />
                          <span className="font-medium truncate">{s.displayName}</span>
                        </div>
                      </td>
                      <td className="px-4 py-3 hidden md:table-cell">
                        <span className="font-mono text-xs" style={{ color: "var(--color-muted)" }}>{s.code}</span>
                      </td>
                      <td className="px-4 py-3 hidden lg:table-cell">
                        <span className="text-xs" style={{ color: "var(--color-muted)" }}>
                          {s.ldapDomains.length > 0 ? s.ldapDomains.join(", ") : "—"}
                        </span>
                      </td>
                      <td className="px-4 py-3 hidden lg:table-cell">
                        {s.isEnabled ? (
                          <span className="text-xs px-2 py-0.5 rounded-full"
                            style={{ backgroundColor: "var(--color-success)", color: "white" }}>Active</span>
                        ) : (
                          <span className="text-xs px-2 py-0.5 rounded-full"
                            style={{ backgroundColor: "var(--color-danger)", color: "white" }}>Disabled</span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-right">
                        <div className="flex items-center justify-end gap-1.5">
                          {/* Platform Admin buttons */}
                          {isPlatformAdmin && (
                            <>
                              <button type="button" onClick={() => setEditSite(s)}
                                className="hidden md:flex items-center justify-center rounded-lg p-2 min-h-[40px] min-w-[40px] transition hover:opacity-80"
                                style={{ border: "1px solid var(--color-border)" }}
                                title="Edit Name & Domains" aria-label="Edit site">
                                <Pencil size={16} />
                              </button>
                              <button type="button" onClick={() => setLdapSite(s)}
                                className="hidden md:flex items-center justify-center rounded-lg p-2 min-h-[40px] min-w-[40px] transition hover:opacity-80"
                                style={{ border: "1px solid var(--color-border)" }}
                                title="LDAP Configuration" aria-label="LDAP config">
                                <KeyRound size={16} />
                              </button>
                              <button type="button" onClick={() => setManageSysAdminSite(s)}
                                className="hidden lg:flex items-center justify-center rounded-lg p-2 min-h-[40px] min-w-[40px] transition hover:opacity-80"
                                style={{ border: "1px solid var(--color-border)" }}
                                title="Manage System Admins" aria-label="System admins">
                                <Shield size={16} />
                              </button>
                              <button type="button" onClick={() => setManageAdminSite(s)}
                                className="hidden lg:flex items-center justify-center rounded-lg p-2 min-h-[40px] min-w-[40px] transition hover:opacity-80"
                                style={{ border: "1px solid var(--color-border)" }}
                                title="View Business Admins" aria-label="Business admins">
                                <Users size={16} />
                              </button>
                              <button type="button" onClick={() => setSysAdminSite(s)}
                                className="hidden md:flex items-center justify-center gap-1 rounded-lg px-3 py-2 min-h-[40px] text-xs font-medium transition hover:opacity-80"
                                style={{ backgroundColor: "var(--color-primary)", color: "var(--color-primary-foreground)" }}>
                                <Shield size={14} /> Sys Admin
                              </button>
                            </>
                          )}

                          {/* System Admin buttons (only for their own site) */}
                          {isSystemAdmin && canManageThisSite && (
                            <>
                              <button type="button" onClick={() => setManageAdminSite(s)}
                                className="hidden md:flex items-center justify-center rounded-lg p-2 min-h-[40px] min-w-[40px] transition hover:opacity-80"
                                style={{ border: "1px solid var(--color-border)" }}
                                title="Manage Business Admins" aria-label="Business admins">
                                <Users size={16} />
                              </button>
                              <button type="button" onClick={() => setAdminSite(s)}
                                className="hidden md:flex items-center justify-center gap-1 rounded-lg px-3 py-2 min-h-[40px] text-xs font-medium transition hover:opacity-80"
                                style={{ backgroundColor: "var(--color-accent)", color: "var(--color-accent-foreground)" }}>
                                <UserPlus size={14} /> Biz Admin
                              </button>
                            </>
                          )}

                          {/* Dropdown menu for tablet/mobile */}
                          <div className="relative md:hidden">
                            <button type="button" onClick={() => setActionMenu(actionMenu === s.id ? null : s.id)}
                              className="flex items-center justify-center rounded-lg p-2 min-h-[40px] min-w-[40px] transition hover:opacity-80"
                              style={{ border: "1px solid var(--color-border)" }}
                              aria-label="More actions">
                              <MoreVertical size={16} />
                            </button>
                            {actionMenu === s.id && (
                              <>
                                <div className="fixed inset-0 z-40" onClick={() => setActionMenu(null)} />
                                <div className="absolute right-0 mt-1 z-50 rounded-lg shadow-xl py-1 min-w-[200px]"
                                  style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}>
                                  {isPlatformAdmin && (
                                    <>
                                      <MenuItem icon={Pencil} label="Edit Name & Domains" onClick={() => { setEditSite(s); setActionMenu(null); }} />
                                      <MenuItem icon={KeyRound} label="LDAP Config" onClick={() => { setLdapSite(s); setActionMenu(null); }} />
                                      <MenuItem icon={Shield} label="System Admins" onClick={() => { setManageSysAdminSite(s); setActionMenu(null); }} />
                                      <MenuItem icon={Shield} label="Add System Admin" onClick={() => { setSysAdminSite(s); setActionMenu(null); }} primary />
                                      <MenuItem icon={Users} label="View Business Admins" onClick={() => { setManageAdminSite(s); setActionMenu(null); }} />
                                    </>
                                  )}
                                  {isSystemAdmin && canManageThisSite && (
                                    <>
                                      <MenuItem icon={Users} label="Business Admins" onClick={() => { setManageAdminSite(s); setActionMenu(null); }} />
                                      <MenuItem icon={UserPlus} label="Add Business Admin" onClick={() => { setAdminSite(s); setActionMenu(null); }} accent />
                                    </>
                                  )}
                                </div>
                              </>
                            )}
                          </div>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          {/* ─── Mobile: Stacked Cards ─── */}
          <div className="sm:hidden space-y-3">
            {sites.map((s) => {
              const canManageThisSite = isPlatformAdmin || (isSystemAdmin && s.id === profile?.siteId);
              if (!canManageThisSite) return null;

              return (
                <div key={s.id} className="rounded-xl p-4"
                  style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}>
                  <div className="flex items-center gap-3 mb-3">
                    <div className="w-10 h-10 rounded-lg flex-shrink-0"
                      style={{ backgroundColor: THEME_SWATCH[s.code] ?? "#0B3D6F" }} />
                    <div className="min-w-0 flex-1">
                      <h3 className="font-semibold truncate">{s.displayName}</h3>
                      <div className="text-xs font-mono" style={{ color: "var(--color-muted)" }}>
                        {s.code} · {s.ldapDomains.join(", ") || "—"}
                      </div>
                    </div>
                    <span className="text-xs px-2 py-0.5 rounded-full flex-shrink-0"
                      style={{
                        backgroundColor: s.isEnabled ? "var(--color-success)" : "var(--color-danger)",
                        color: "white",
                      }}>
                      {s.isEnabled ? "Active" : "Disabled"}
                    </span>
                  </div>

                  <div className="grid grid-cols-2 gap-2">
                    {isPlatformAdmin && (
                      <>
                        <button type="button" onClick={() => setEditSite(s)}
                          className="flex items-center justify-center gap-1.5 px-2 py-2.5 rounded-lg text-xs min-h-[44px] transition hover:opacity-80"
                          style={{ border: "1px solid var(--color-border)" }}>
                          <Pencil size={16} /> Edit
                        </button>
                        <button type="button" onClick={() => setLdapSite(s)}
                          className="flex items-center justify-center gap-1.5 px-2 py-2.5 rounded-lg text-xs min-h-[44px] transition hover:opacity-80"
                          style={{ border: "1px solid var(--color-border)" }}>
                          <KeyRound size={16} /> LDAP
                        </button>
                        <button type="button" onClick={() => setManageSysAdminSite(s)}
                          className="flex items-center justify-center gap-1.5 px-2 py-2.5 rounded-lg text-xs min-h-[44px] transition hover:opacity-80"
                          style={{ border: "1px solid var(--color-border)" }}>
                          <Shield size={16} /> Sys Admins
                        </button>
                        <button type="button" onClick={() => setSysAdminSite(s)}
                          className="flex items-center justify-center gap-1.5 px-2 py-2.5 rounded-lg text-xs min-h-[44px] transition hover:opacity-80"
                          style={{ backgroundColor: "var(--color-primary)", color: "var(--color-primary-foreground)" }}>
                          <Shield size={16} /> Add Sys Admin
                        </button>
                        <button type="button" onClick={() => setManageAdminSite(s)}
                          className="flex items-center justify-center gap-1.5 px-2 py-2.5 rounded-lg text-xs min-h-[44px] col-span-2 transition hover:opacity-80"
                          style={{ border: "1px solid var(--color-border)" }}>
                          <Users size={16} /> View Business Admins
                        </button>
                      </>
                    )}
                    {isSystemAdmin && canManageThisSite && (
                      <>
                        <button type="button" onClick={() => setManageAdminSite(s)}
                          className="flex items-center justify-center gap-1.5 px-2 py-2.5 rounded-lg text-xs min-h-[44px] transition hover:opacity-80"
                          style={{ border: "1px solid var(--color-border)" }}>
                          <Users size={16} /> Biz Admins
                        </button>
                        <button type="button" onClick={() => setAdminSite(s)}
                          className="flex items-center justify-center gap-1.5 px-2 py-2.5 rounded-lg text-xs min-h-[44px] transition hover:opacity-80"
                          style={{ backgroundColor: "var(--color-accent)", color: "var(--color-accent-foreground)" }}>
                          <UserPlus size={16} /> Add Biz Admin
                        </button>
                      </>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        </>
      )}

      {/* Drawers */}
      {editSite && <SiteEditDrawer site={editSite} onClose={() => setEditSite(null)} />}
      {ldapSite && <LdapDrawer site={ldapSite} onClose={() => setLdapSite(null)} />}
      {adminSite && <AdminDrawer site={adminSite} onClose={() => setAdminSite(null)} />}
      {manageAdminSite && <ManageAdminsDrawer site={manageAdminSite} onClose={() => setManageAdminSite(null)} />}
      {sysAdminSite && <SysAdminDrawer site={sysAdminSite} onClose={() => setSysAdminSite(null)} />}
      {manageSysAdminSite && <ManageSysAdminsDrawer site={manageSysAdminSite} onClose={() => setManageSysAdminSite(null)} />}
    </div>
  );
}

// ─── Menu Item (for dropdown) ──────────────────────────────────────

function MenuItem({ icon: Icon, label, onClick, primary, accent }: {
  icon: React.ComponentType<{ size?: number }>;
  label: string;
  onClick: () => void;
  primary?: boolean;
  accent?: boolean;
}) {
  return (
    <button type="button" onClick={onClick}
      className="w-full flex items-center gap-2 px-4 py-2.5 text-sm text-left transition hover:opacity-80"
      style={{
        color: primary ? "var(--color-primary)" : accent ? "var(--color-accent)" : "var(--color-text)",
      }}>
      <Icon size={16} /> {label}
    </button>
  );
}

// ─── Edit Site Drawer ──────────────────────────────────────────────

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
          <input className="input" value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
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
              if (d && !domains.includes(d)) { setDomains([...domains, d]); setDomainInput(""); }
            }} className="px-3 py-2.5 rounded-lg text-sm min-h-[44px]"
              style={{ backgroundColor: "var(--color-primary)", color: "var(--color-primary-foreground)" }}>Add</button>
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

// ─── LDAP Config Drawer ───────────────────────────────────────────

function LdapDrawer({ site, onClose }: { site: SiteDto; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [cfg, setCfg] = useState<LdapConfigUpsertDto>({
    host: "", port: 389, useStartTls: false, baseDn: "", upnDomain: null,
  });
  const [testEmail, setTestEmail] = useState("");
  const [testPassword, setTestPassword] = useState("");
  const [testResult, setTestResult] = useState<LdapTestResult | null>(null);
  const [testing, setTesting] = useState(false);

  const { data: existing, isLoading } = useQuery<LdapConfigDto>({
    queryKey: ["ldap-config", site.id],
    queryFn: () => sitesApi.getLdapConfig(site.id),
    enabled: !!site.id,
    retry: false,
  });

  const lastConfigRef = useRef<string | null>(null);
  useEffect(() => {
    if (!existing) return;
    const sig = `${existing.host}|${existing.port}|${existing.baseDn}|${existing.upnDomain}`;
    if (lastConfigRef.current === sig) return;
    lastConfigRef.current = sig;
    setCfg({
      host: existing.host, port: existing.port,
      useStartTls: existing.useStartTls, baseDn: existing.baseDn,
      upnDomain: existing.upnDomain,
    });
  }, [existing]);

  const saveMutation = useMutation({
    mutationFn: () => sitesApi.upsertLdapConfig(site.id, cfg),
    onSuccess: () => {
      toast.success("LDAP config saved");
      void queryClient.invalidateQueries({ queryKey: SITES_KEY });
      onClose();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  const test = async () => {
    if (!testEmail || !testPassword) { toast.error("Email and password required"); return; }
    setTesting(true); setTestResult(null);
    try {
      const req: LdapTestRequest = { ...cfg, testEmail, testPassword };
      const result = await sitesApi.testLdap(req);
      setTestResult(result);
      if (result.success) toast.success(`LDAP OK (${result.latencyMs}ms)`);
      else toast.error(`LDAP failed: ${result.errorMessage}`);
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Test failed");
    } finally { setTesting(false); }
  };

  if (isLoading) return <Drawer title={`LDAP — ${site.displayName}`} onClose={onClose}><div>Loading...</div></Drawer>;

  return (
    <Drawer title={`LDAP — ${site.displayName}`} onClose={onClose}>
      <div className="space-y-3">
        <div className="p-3 rounded-md text-xs" style={{ backgroundColor: "var(--color-background)" }}>
          <strong>Site:</strong> {site.displayName}<br />
          <strong>Domains:</strong> {site.ldapDomains.join(", ")}
        </div>
        <Field label="Host">
          <input className="input" value={cfg.host} onChange={(e) => setCfg({ ...cfg, host: e.target.value })} placeholder="10.131.220.11" />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Port">
            <select className="input" value={cfg.port} onChange={(e) => setCfg({ ...cfg, port: parseInt(e.target.value) })}>
              <option value={389}>389</option>
              <option value={636}>636 (LDAPS)</option>
            </select>
          </Field>
          <Field label="StartTLS">
            <select className="input" value={cfg.useStartTls ? "1" : "0"} onChange={(e) => setCfg({ ...cfg, useStartTls: e.target.value === "1" })}>
              <option value="0">No</option>
              <option value="1">Yes</option>
            </select>
          </Field>
        </div>
        <Field label="Base DN">
          <input className="input" value={cfg.baseDn} onChange={(e) => setCfg({ ...cfg, baseDn: e.target.value })} placeholder="DC=KALVENTIS,DC=DOM" />
        </Field>
        <Field label="UPN Domain (optional)">
          <input className="input" value={cfg.upnDomain ?? ""} onChange={(e) => setCfg({ ...cfg, upnDomain: e.target.value || null })} placeholder="kalventis.dom" />
        </Field>
        {cfg.port === 389 && !cfg.useStartTls && (
          <div className="p-2 rounded-md text-xs" style={{ backgroundColor: "var(--color-warning)", color: "white", opacity: 0.9 }}>
            ⚠ Plain LDAP: password transmitted cleartext.
          </div>
        )}

        <div className="pt-4" style={{ borderTop: "1px solid var(--color-border)" }}>
          <h4 className="text-sm font-semibold mb-2">Test Login</h4>
          <input className="input mb-2" value={testEmail}
            onChange={(e) => setTestEmail(e.target.value)}
            placeholder={`test.user@${site.ldapDomains[0] ?? "example.com"}`} />
          <input type="password" className="input mb-2" value={testPassword}
            onChange={(e) => setTestPassword(e.target.value)} placeholder="AD password" />
          <button type="button" onClick={test} disabled={testing}
            className="w-full px-3 py-2.5 rounded-lg text-sm disabled:opacity-50 min-h-[44px]"
            style={{ border: "1px solid var(--color-border)" }}>
            {testing ? "Testing..." : "Test Login"}
          </button>
          {testResult && (
            <div className="mt-2 p-3 rounded-md text-xs" style={{
              backgroundColor: testResult.success ? "var(--color-success)" : "var(--color-danger)",
              color: "white", opacity: 0.9,
            }}>
              {testResult.success ? (
                <>{testResult.displayName} ({testResult.latencyMs}ms)</>
              ) : (testResult.errorMessage)}
            </div>
          )}
        </div>

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

// ─── Admin List Drawer (shared) ───────────────────────────────────

function AdminListDrawer({ title, site, admins, isLoading, onRevoke, revokePending, canRevoke, emptyMessage, onClose }: {
  title: string; site: SiteDto; admins: UserDto[]; isLoading: boolean;
  onRevoke: (userId: string, email: string) => void; revokePending: boolean;
  canRevoke: boolean; emptyMessage: string; onClose: () => void;
}) {
  return (
    <Drawer title={title} onClose={onClose}>
      <div className="space-y-3">
        <div className="p-3 rounded-md text-xs" style={{ backgroundColor: "var(--color-background)" }}>
          <strong>Site:</strong> {site.displayName} ({site.code})<br />
          <strong>Domains:</strong> {site.ldapDomains.join(", ")}
        </div>
        <div>
          <h4 className="text-sm font-semibold mb-2">Administrators ({admins.length})</h4>
          {isLoading ? (
            <div className="text-center py-4 text-sm" style={{ color: "var(--color-muted)" }}>Loading...</div>
          ) : admins.length === 0 ? (
            <div className="p-4 rounded-md text-center text-sm"
              style={{ backgroundColor: "var(--color-background)", color: "var(--color-muted)" }}>{emptyMessage}</div>
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
                        <div className="text-xs truncate" style={{ color: "var(--color-muted)" }}>{admin.email}</div>
                      </div>
                    </div>
                    <div className="text-xs mt-1" style={{ color: "var(--color-muted)" }}>
                      {admin.isEnabled ? "✓ Active" : "✗ Disabled"}
                      {admin.lastLoginAt && ` · ${new Date(admin.lastLoginAt).toLocaleDateString()}`}
                    </div>
                  </div>
                  {canRevoke && (
                    <button type="button" onClick={() => onRevoke(admin.id, admin.email)}
                      disabled={revokePending}
                      className="px-2 py-1.5 rounded-md text-xs flex-shrink-0 ml-2 disabled:opacity-50 min-h-[36px]"
                      style={{ color: "var(--color-danger)", border: "1px solid var(--color-danger)" }}>
                      Revoke
                    </button>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </Drawer>
  );
}

function ManageAdminsDrawer({ site, onClose }: { site: SiteDto; onClose: () => void }) {
  const queryClient = useQueryClient();
  const profile = useAuthStore((s) => s.profile);
  const isSystemAdmin = profile?.roles.includes("system-admin") ?? false;
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

  return (
    <AdminListDrawer
      title={`Business Admins — ${site.displayName}`}
      site={site} admins={admins} isLoading={isLoading}
      onRevoke={(id, email) => { if (confirm(`Revoke from ${email}?`)) revokeMutation.mutate(id); }}
      revokePending={revokeMutation.isPending}
      canRevoke={isSystemAdmin}
      emptyMessage="No business admins yet."
      onClose={onClose}
    />
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
    <AdminListDrawer
      title={`System Admins — ${site.displayName}`}
      site={site} admins={admins} isLoading={isLoading}
      onRevoke={(id, email) => { if (confirm(`Revoke from ${email}?`)) revokeMutation.mutate(id); }}
      revokePending={revokeMutation.isPending}
      canRevoke={true}
      emptyMessage="No System Admins yet."
      onClose={onClose}
    />
  );
}

// ─── Assign Admin Drawer (shared) ────────────────────────────────

function AssignAdminDrawer({ title, site, assignFn, buttonText, infoColor, infoText, onClose }: {
  title: string; site: SiteDto;
  assignFn: (siteId: string, email: string, displayName?: string) => Promise<UserDto>;
  buttonText: string; infoColor: string; infoText: string; onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const domainHint = site.ldapDomains[0] ?? "example.com";

  const assignMutation = useMutation({
    mutationFn: () => assignFn(site.id, email, displayName || undefined),
    onSuccess: (user) => {
      toast.success(`Assigned: ${user.email}`);
      void queryClient.invalidateQueries({ queryKey: SITES_KEY });
      onClose();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  return (
    <Drawer title={title} onClose={onClose}>
      <div className="space-y-3">
        <div className="p-3 rounded-md text-xs" style={{ backgroundColor: "var(--color-background)" }}>
          <strong>Site:</strong> {site.displayName} ({site.code})<br />
          <strong>Domains:</strong> {site.ldapDomains.join(", ")}
        </div>
        <div className="p-3 rounded-md text-xs" style={{ backgroundColor: infoColor, color: "white", opacity: 0.9 }}>{infoText}</div>
        <Field label="Email (must match LDAP userPrincipalName)">
          <input className="input" type="email" value={email}
            onChange={(e) => setEmail(e.target.value.toLowerCase())} placeholder={`admin.user@${domainHint}`} />
        </Field>
        <Field label="Display Name (optional)">
          <input className="input" value={displayName} onChange={(e) => setDisplayName(e.target.value)} placeholder="Budi Santoso" />
        </Field>
        <div className="flex justify-end gap-2 pt-4 sticky bottom-0" style={{ backgroundColor: "var(--color-surface)" }}>
          <button type="button" onClick={onClose} className="px-4 py-2.5 rounded-lg text-sm min-h-[44px]"
            style={{ border: "1px solid var(--color-border)" }}>Cancel</button>
          <button type="button" onClick={() => assignMutation.mutate()} disabled={assignMutation.isPending || !email}
            className="px-4 py-2.5 rounded-lg text-sm disabled:opacity-50 min-h-[44px]"
            style={{ backgroundColor: "var(--color-primary)", color: "var(--color-primary-foreground)" }}>
            {assignMutation.isPending ? "Assigning..." : buttonText}
          </button>
        </div>
      </div>
    </Drawer>
  );
}

function AdminDrawer({ site, onClose }: { site: SiteDto; onClose: () => void }) {
  return <AssignAdminDrawer title={`Business Admin — ${site.displayName}`} site={site}
    assignFn={sitesApi.assignBusinessAdmin} buttonText="Assign Business Admin"
    infoColor="var(--color-warning)"
    infoText="Creates user (if not exists) and assigns Business Admin role. User must exist in LDAP."
    onClose={onClose} />;
}

function SysAdminDrawer({ site, onClose }: { site: SiteDto; onClose: () => void }) {
  return <AssignAdminDrawer title={`System Admin — ${site.displayName}`} site={site}
    assignFn={sitesApi.assignSystemAdmin} buttonText="Assign System Admin"
    infoColor="var(--color-primary)"
    infoText="System Admin can assign Business Admins for this site. User must exist in LDAP."
    onClose={onClose} />;
}

// ─── Helpers ────────────────────────────────────────────────────────

function Drawer({ title, onClose, children }: { title: string; onClose: () => void; children: React.ReactNode }) {
  useEffect(() => {
    const handleEsc = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    document.addEventListener("keydown", handleEsc);
    document.body.style.overflow = "hidden";
    return () => { document.removeEventListener("keydown", handleEsc); document.body.style.overflow = ""; };
  }, [onClose]);

  return (
    <div className="fixed inset-0 z-50 flex justify-end syntera-drawer-backdrop" style={{ backgroundColor: "rgba(0,0,0,0.4)" }} onClick={onClose}>
      <div className="w-full max-w-md h-full flex flex-col syntera-drawer-panel"
        style={{ backgroundColor: "var(--color-surface)" }}
        onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between px-6 pt-6 pb-3 shrink-0 sticky top-0 z-10"
          style={{ backgroundColor: "var(--color-surface)", borderBottom: "1px solid var(--color-border)" }}>
          <h2 className="text-lg font-semibold">{title}</h2>
          <button onClick={onClose} className="text-2xl leading-none p-2 rounded-lg hover:opacity-70 transition-opacity min-h-[40px] min-w-[40px] flex items-center justify-center" aria-label="Close">×</button>
        </div>
        <div className="flex-1 overflow-y-auto px-6 pb-6">{children}</div>
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
