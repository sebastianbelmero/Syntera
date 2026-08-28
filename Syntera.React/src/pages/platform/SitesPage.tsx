import { useEffect, useRef, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Pencil, KeyRound, Palette, UserPlus, Users } from "lucide-react";
import { sitesApi } from "../../api/platform";
import { ApiError } from "../../api/client";
import type {
  SiteDto,
  LdapConfigDto, LdapConfigUpsertDto, LdapTestRequest, LdapTestResult,
  ThemePalette,
} from "../../types";

const SITES_KEY = ["sites"] as const;

/**
 * Platform Admin → Site Management.
 *
 * The 6 sites (Kalventis, Kalbe, Fima, GOF, Dankos, Hexpharm) are
 * PRE-DEFINED in backend configuration. Code, ConnectionString, and
 * IsEnabled are locked. From the frontend, you can edit:
 *   - Display Name
 *   - Email Domains (add/remove)
 *   - LDAP Config (4 fields: Host, Port, BaseDn, UseStartTls)
 *   - Theme palette (10 colors × 2 modes)
 */
export default function SitesPage() {
  const [editSite, setEditSite] = useState<SiteDto | null>(null);
  const [ldapSite, setLdapSite] = useState<SiteDto | null>(null);
  const [themeSite, setThemeSite] = useState<SiteDto | null>(null);
  const [adminSite, setAdminSite] = useState<SiteDto | null>(null);
  const [manageAdminSite, setManageAdminSite] = useState<SiteDto | null>(null);

  const { data: sites = [], isLoading: loading } = useQuery<SiteDto[]>({
    queryKey: SITES_KEY,
    queryFn: () => sitesApi.list(),
  });

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-2xl font-bold">Sites</h1>
        <p className="text-sm" style={{ color: "var(--color-muted)" }}>
          6 fixed sites — edit name, email domains, LDAP config, theme, and manage business admins.
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
              onConfigureLdap={() => setLdapSite(s)}
              onEditTheme={() => setThemeSite(s)}
              onAssignAdmin={() => setAdminSite(s)}
              onManageAdmins={() => setManageAdminSite(s)}
            />
          ))}
        </div>
      )}

      {editSite && <SiteEditDrawer site={editSite} onClose={() => setEditSite(null)} />}
      {ldapSite && <LdapDrawer site={ldapSite} onClose={() => setLdapSite(null)} />}
      {themeSite && <ThemeDrawer site={themeSite} onClose={() => setThemeSite(null)} />}
      {adminSite && <AdminDrawer site={adminSite} onClose={() => setAdminSite(null)} />}
      {manageAdminSite && <ManageAdminsDrawer site={manageAdminSite} onClose={() => setManageAdminSite(null)} />}
    </div>
  );
}

function SiteCard({ site, onEdit, onConfigureLdap, onEditTheme, onAssignAdmin, onManageAdmins }: {
  site: SiteDto;
  onEdit: () => void;
  onConfigureLdap: () => void;
  onEditTheme: () => void;
  onAssignAdmin: () => void;
  onManageAdmins: () => void;
}) {
  const swatch = site.code;
  const swatchColor = THEME_SWATCH[swatch] ?? "#0B3D6F";

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
        <div><strong>Theme:</strong> {site.defaultThemeKey}</div>
      </div>

      <div className="grid grid-cols-2 gap-2">
        <button onClick={onEdit} type="button"
          className="flex items-center justify-center gap-1.5 px-2 py-2.5 rounded-lg text-xs min-h-[44px] transition hover:opacity-80"
          style={{ border: "1px solid var(--color-border)" }}>
          <Pencil size={16} /> Edit Name
        </button>
        <button onClick={onManageAdmins} type="button"
          className="flex items-center justify-center gap-1.5 px-2 py-2.5 rounded-lg text-xs min-h-[44px] transition hover:opacity-80"
          style={{ border: "1px solid var(--color-border)" }}>
          <Users size={16} /> Admins
        </button>
        <button onClick={onAssignAdmin} type="button"
          className="flex items-center justify-center gap-1.5 px-2 py-2.5 rounded-lg text-xs min-h-[44px] transition hover:opacity-80"
          style={{ backgroundColor: "var(--color-accent)", color: "var(--color-accent-foreground)" }}>
          <UserPlus size={16} /> Add Admin
        </button>
        <button onClick={onConfigureLdap} type="button"
          className="flex items-center justify-center gap-1.5 px-2 py-2.5 rounded-lg text-xs min-h-[44px] transition hover:opacity-80"
          style={{ border: "1px solid var(--color-border)" }}>
          <KeyRound size={16} /> LDAP
        </button>
        <button onClick={onEditTheme} type="button"
          className="flex items-center justify-center gap-1.5 px-2 py-2.5 rounded-lg text-xs min-h-[44px] col-span-2 transition hover:opacity-80"
          style={{ border: "1px solid var(--color-border)" }}>
          <Palette size={16} /> Theme
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
            <button onClick={() => {
              const d = domainInput.trim();
              if (d && !domains.includes(d)) {
                setDomains([...domains, d]);
                setDomainInput("");
              }
            }} className="px-3 py-2 rounded-md text-sm"
              style={{ backgroundColor: "var(--color-primary)", color: "white" }}>
              Add
            </button>
          </div>
        </Field>

        <div className="flex justify-end gap-2 pt-4">
          <button onClick={onClose} className="px-4 py-2 rounded-md text-sm"
            style={{ border: "1px solid var(--color-border)" }}>Cancel</button>
          <button onClick={() => saveMutation.mutate()} disabled={saveMutation.isPending}
            className="px-4 py-2 rounded-md text-sm disabled:opacity-50"
            style={{ backgroundColor: "var(--color-primary)", color: "white" }}>
            {saveMutation.isPending ? "Saving..." : "Save"}
          </button>
        </div>
      </div>
    </Drawer>
  );
}

// ─── LDAP Config (simplified: 4 fields + test email/password) ───────

function LdapDrawer({ site, onClose }: { site: SiteDto; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [cfg, setCfg] = useState<LdapConfigUpsertDto>({
    host: "", port: 389, useStartTls: false, baseDn: "", upnDomain: null,
  });
  const [testEmail, setTestEmail] = useState("");
  const [testPassword, setTestPassword] = useState("");
  const [testResult, setTestResult] = useState<LdapTestResult | null>(null);
  const [testing, setTesting] = useState(false);

  const { data: existing, isLoading: loading } = useQuery<LdapConfigDto>({
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
    if (!testEmail || !testPassword) {
      toast.error("Email and password are required for test");
      return;
    }
    setTesting(true);
    setTestResult(null);
    try {
      const req: LdapTestRequest = { ...cfg, testEmail, testPassword };
      const result = await sitesApi.testLdap(req);
      setTestResult(result);
      if (result.success) toast.success(`LDAP OK — welcome, ${result.displayName}! (${result.latencyMs}ms)`);
      else toast.error(`LDAP test failed: ${result.errorMessage}`);
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Test failed");
    } finally {
      setTesting(false);
    }
  };

  if (loading) return <Drawer title={`LDAP — ${site.displayName}`} onClose={onClose}><div>Loading...</div></Drawer>;

  return (
    <Drawer title={`LDAP — ${site.displayName}`} onClose={onClose}>
      <div className="space-y-3">
        <div className="p-3 rounded-md text-xs" style={{ backgroundColor: "var(--color-background)" }}>
          <strong>Site:</strong> {site.displayName}<br />
          <strong>Domains:</strong> {site.ldapDomains.join(", ")}
        </div>

        <Field label="Host">
          <input className="input" value={cfg.host}
            onChange={(e) => setCfg({ ...cfg, host: e.target.value })}
            placeholder="10.131.220.11" />
        </Field>

        <div className="grid grid-cols-2 gap-3">
          <Field label="Port">
            <input type="number" className="input" value={cfg.port}
              onChange={(e) => setCfg({ ...cfg, port: parseInt(e.target.value) || 389 })} />
          </Field>
          <Field label="Use StartTLS">
            <select className="input" value={cfg.useStartTls ? "1" : "0"}
              onChange={(e) => setCfg({ ...cfg, useStartTls: e.target.value === "1" })}>
              <option value="0">No (plain)</option>
              <option value="1">Yes</option>
            </select>
          </Field>
        </div>

        <Field label="Base DN">
          <input className="input" value={cfg.baseDn}
            onChange={(e) => setCfg({ ...cfg, baseDn: e.target.value })}
            placeholder="DC=KALVENTIS,DC=DOM" />
        </Field>

        <Field label="UPN Domain (AD bind domain — leave empty if same as email domain)">
          <input className="input" value={cfg.upnDomain ?? ""}
            onChange={(e) => setCfg({ ...cfg, upnDomain: e.target.value || null })}
            placeholder="kalventis.dom" />
          <div className="text-xs mt-1" style={{ color: "var(--color-muted)" }}>
            When user logs in with <code>user@kalventis.com</code>, we bind to AD as
            <code> user@{cfg.upnDomain || "kalventis.dom"}</code>. Set this to the AD domain
            suffix (from Base DN: DC=KALVENTIS,DC=DOM → kalventis.dom).
          </div>
        </Field>

        {cfg.port === 389 && !cfg.useStartTls && (
          <div className="p-2 rounded-md text-xs" style={{
            backgroundColor: "var(--color-warning)", color: "white", opacity: 0.9
          }}>
            ⚠ Plain LDAP: password transmitted in cleartext. Recommended: enable StartTLS or use port 636.
          </div>
        )}

        <div className="pt-4 border-t" style={{ borderColor: "var(--color-border)" }}>
          <h4 className="text-sm font-semibold mb-2">Test Login</h4>
          <p className="text-xs mb-2" style={{ color: "var(--color-muted)" }}>
            Test with a real AD user's email + password. We bind to LDAP using these credentials.
          </p>
          <div className="space-y-2">
            <input className="input" value={testEmail}
              onChange={(e) => setTestEmail(e.target.value)}
              placeholder={`test.user@${site.ldapDomains[0] ?? "example.com"}`} />
            <input type="password" className="input" value={testPassword}
              onChange={(e) => setTestPassword(e.target.value)}
              placeholder="AD password" />
            <button onClick={test} disabled={testing}
              className="w-full px-3 py-2 rounded-md text-sm disabled:opacity-50"
              style={{ border: "1px solid var(--color-border)" }}>
              {testing ? "Testing..." : "Test Login"}
            </button>
          </div>
          {testResult && (
            <div className="mt-2 p-3 rounded-md text-xs"
              style={{
                backgroundColor: testResult.success ? "var(--color-success)" : "var(--color-danger)",
                color: "white", opacity: 0.9
              }}>
              {testResult.success ? (
                <>
                  <div>✓ Bind successful ({testResult.latencyMs}ms)</div>
                  {testResult.dn && <div><strong>DN:</strong> {testResult.dn}</div>}
                  {testResult.displayName && <div><strong>Name:</strong> {testResult.displayName}</div>}
                  {testResult.email && <div><strong>Email:</strong> {testResult.email}</div>}
                </>
              ) : (
                <div>✗ {testResult.errorMessage}</div>
              )}
            </div>
          )}
        </div>

        <div className="flex justify-end gap-2 pt-4">
          <button onClick={onClose} className="px-4 py-2 rounded-md text-sm"
            style={{ border: "1px solid var(--color-border)" }}>Cancel</button>
          <button onClick={() => saveMutation.mutate()} disabled={saveMutation.isPending}
            className="px-4 py-2 rounded-md text-sm disabled:opacity-50"
            style={{ backgroundColor: "var(--color-primary)", color: "white" }}>
            {saveMutation.isPending ? "Saving..." : "Save"}
          </button>
        </div>
      </div>
    </Drawer>
  );
}

// ─── Theme Editor (color picker × 20) ───────────────────────────────

function ThemeDrawer({ site, onClose }: { site: SiteDto; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [themeKey, setThemeKey] = useState(site.defaultThemeKey);
  const [light, setLight] = useState<ThemePalette>(DEFAULT_LIGHT);
  const [dark, setDark] = useState<ThemePalette>(DEFAULT_DARK);

  const { data: existing } = useQuery({
    queryKey: ["theme", site.id],
    queryFn: () => sitesApi.getTheme(site.id),
    enabled: !!site.id,
  });

  const lastThemeRef = useRef<string | null>(null);
  useEffect(() => {
    if (!existing) return;
    const sig = JSON.stringify(existing);
    if (lastThemeRef.current === sig) return;
    lastThemeRef.current = sig;
    setThemeKey(existing.themeKey);
    setLight(existing.light);
    setDark(existing.dark);
  }, [existing]);

  const saveMutation = useMutation({
    mutationFn: () => sitesApi.upsertTheme(site.id, { themeKey, light, dark, logoUrl: null }),
    onSuccess: () => {
      toast.success("Theme saved");
      void queryClient.invalidateQueries({ queryKey: SITES_KEY });
      onClose();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  return (
    <Drawer title={`Theme — ${site.displayName}`} onClose={onClose}>
      <div className="space-y-4">
        <Field label="Theme Key">
          <input className="input" value={themeKey} onChange={(e) => setThemeKey(e.target.value)} />
        </Field>

        <div>
          <h4 className="text-sm font-semibold mb-2">Light Mode</h4>
          <ColorGrid palette={light} onChange={setLight} />
        </div>

        <div>
          <h4 className="text-sm font-semibold mb-2">Dark Mode</h4>
          <ColorGrid palette={dark} onChange={setDark} />
        </div>

        <div className="flex justify-end gap-2 pt-4">
          <button onClick={onClose} className="px-4 py-2 rounded-md text-sm"
            style={{ border: "1px solid var(--color-border)" }}>Cancel</button>
          <button onClick={() => saveMutation.mutate()} disabled={saveMutation.isPending}
            className="px-4 py-2 rounded-md text-sm disabled:opacity-50"
            style={{ backgroundColor: "var(--color-primary)", color: "white" }}>
            {saveMutation.isPending ? "Saving..." : "Save"}
          </button>
        </div>
      </div>
    </Drawer>
  );
}

function ColorGrid({ palette, onChange }: { palette: ThemePalette; onChange: (p: ThemePalette) => void }) {
  const keys: (keyof ThemePalette)[] = ["primary", "accent", "background", "surface", "text", "muted", "border", "success", "warning", "danger"];
  return (
    <div className="grid grid-cols-2 gap-2">
      {keys.map((k) => (
        <label key={k} className="flex items-center gap-2 p-1.5 rounded text-xs cursor-pointer">
          <input type="color" value={palette[k]} onChange={(e) => onChange({ ...palette, [k]: e.target.value })}
            className="w-8 h-8 rounded cursor-pointer" style={{ border: "none", padding: 0 }} />
          <span className="font-mono">{k}</span>
        </label>
      ))}
    </div>
  );
}

// ─── Theme swatches & defaults ──────────────────────────────────────

const THEME_SWATCH: Record<string, string> = {
  kalventis: "#007A4D",
  kalbe: "#E2231A",
  fima: "#6B46C1",
  gof: "#C2410C",
  dankos: "#0054A6",
  hexpharm: "#00796B",
  syntera: "#0B3D6F",
};

const DEFAULT_LIGHT: ThemePalette = {
  primary: "#0B3D6F", accent: "#00A7B5",
  background: "#F8FAFC", surface: "#FFFFFF",
  text: "#243447", muted: "#64748B", border: "#E2E8F0",
  success: "#10B981", warning: "#F59E0B", danger: "#EF4444",
};

const DEFAULT_DARK: ThemePalette = {
  primary: "#60A5FA", accent: "#22D3EE",
  background: "#0F172A", surface: "#1E293B",
  text: "#F1F5F9", muted: "#94A3B8", border: "#334155",
  success: "#34D399", warning: "#FBBF24", danger: "#F87171",
};

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
              No business admins yet. Use "Add Admin" button to assign one.
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
                    onClick={() => handleRevoke(admin.id, admin.email)}
                    disabled={revokeMutation.isPending}
                    className="px-2 py-1 rounded-md text-xs flex-shrink-0 ml-2 disabled:opacity-50"
                    style={{ color: "var(--color-danger)", border: "1px solid var(--color-danger)" }}
                  >
                    Revoke
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="pt-4 border-t" style={{ borderColor: "var(--color-border)" }}>
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

  // Prefill email domain hint from site's first registered domain.
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

        <div className="flex justify-end gap-2 pt-4">
          <button onClick={onClose} className="px-4 py-2 rounded-md text-sm"
            style={{ border: "1px solid var(--color-border)" }}>Cancel</button>
          <button
            onClick={() => assignMutation.mutate()}
            disabled={assignMutation.isPending || !email}
            className="px-4 py-2 rounded-md text-sm disabled:opacity-50"
            style={{ backgroundColor: "var(--color-primary)", color: "white" }}
          >
            {assignMutation.isPending ? "Assigning..." : "Assign Business Admin"}
          </button>
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
        {/* Sticky header — always visible, even on mobile full-screen */}
        <div className="flex items-center justify-between mb-4 px-6 pt-6 pb-3 shrink-0 sticky top-0 z-10"
          style={{ backgroundColor: "var(--color-surface)", borderBottom: "1px solid var(--color-border)" }}>
          <h2 className="text-lg font-semibold">{title}</h2>
          <button onClick={onClose} className="text-2xl leading-none p-1 rounded hover:opacity-70" aria-label="Close">×</button>
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
