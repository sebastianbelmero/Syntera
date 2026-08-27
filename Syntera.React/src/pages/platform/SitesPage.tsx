import { useEffect, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Plus, Pencil, Power, KeyRound, Palette } from "lucide-react";
import { sitesApi } from "../../api/platform";
import { ApiError } from "../../api/client";
import type { SiteDto, SiteUpsertDto, LdapConfigDto, LdapConfigUpsertDto, LdapTestRequest, LdapTestResult } from "../../types";

const SITES_KEY = ["sites"] as const;

/**
 * Platform Admin → Site Management.
 * Lists all sites, allows create/update/disable, and manage LDAP config + theme per site.
 */
export default function SitesPage() {
  const [selected, setSelected] = useState<SiteDto | null>(null);

  const { data: sites = [], isLoading: loading } = useQuery<SiteDto[]>({
    queryKey: SITES_KEY,
    queryFn: () => sitesApi.list(),
  });

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">Sites</h1>
          <p className="text-sm" style={{ color: "var(--color-muted)" }}>
            Manage registered sites, LDAP configurations, and themes.
          </p>
        </div>
        <button
          onClick={() => setSelected({} as SiteDto)}
          className="flex items-center gap-1.5 px-3 py-2 rounded-lg text-sm font-medium"
          style={{ backgroundColor: "var(--color-primary)", color: "white" }}
        >
          <Plus size={16} /> New Site
        </button>
      </div>

      {loading ? (
        <div className="text-center py-8" style={{ color: "var(--color-muted)" }}>Loading...</div>
      ) : sites.length === 0 ? (
        <div className="text-center py-8" style={{ color: "var(--color-muted)" }}>No sites registered yet.</div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {sites.map((s) => (
            <SiteCard key={s.id} site={s} onEdit={() => setSelected(s)} />
          ))}
        </div>
      )}

      {selected && (
        <SiteDrawer site={selected} onClose={() => setSelected(null)} />
      )}
    </div>
  );
}

function SiteCard({ site, onEdit }: { site: SiteDto; onEdit: () => void }) {
  const queryClient = useQueryClient();
  const [showLdap, setShowLdap] = useState(false);
  const [showTheme, setShowTheme] = useState(false);

  const disableMutation = useMutation({
    mutationFn: () => sitesApi.disable(site.id),
    onSuccess: () => {
      toast.success("Site disabled");
      void queryClient.invalidateQueries({ queryKey: SITES_KEY });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  const handleDisable = () => {
    if (!confirm(`Disable site ${site.code}? Users from this site will not be able to log in.`)) return;
    disableMutation.mutate();
  };

  return (
    <div
      className="rounded-xl p-5"
      style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}
    >
      <div className="flex items-start justify-between mb-3">
        <div>
          <div className="flex items-center gap-2">
            <h3 className="font-semibold text-lg">{site.displayName}</h3>
            {!site.isEnabled && (
              <span className="text-xs px-2 py-0.5 rounded-full" style={{ backgroundColor: "var(--color-danger)", color: "white" }}>
                Disabled
              </span>
            )}
          </div>
          <div className="text-xs mt-0.5 font-mono" style={{ color: "var(--color-muted)" }}>{site.code}</div>
        </div>
      </div>

      <div className="text-xs space-y-1 mb-4">
        <div><strong>Domains:</strong> {site.ldapDomains.join(", ") || "—"}</div>
        <div><strong>Theme:</strong> {site.defaultThemeKey}</div>
      </div>

      <div className="flex flex-wrap gap-2">
        <button onClick={onEdit} className="flex items-center gap-1 px-2.5 py-1.5 rounded-md text-xs" style={{ border: "1px solid var(--color-border)" }}>
          <Pencil size={12} /> Edit
        </button>
        <button onClick={() => setShowLdap(true)} className="flex items-center gap-1 px-2.5 py-1.5 rounded-md text-xs" style={{ border: "1px solid var(--color-border)" }}>
          <KeyRound size={12} /> LDAP
        </button>
        <button onClick={() => setShowTheme(true)} className="flex items-center gap-1 px-2.5 py-1.5 rounded-md text-xs" style={{ border: "1px solid var(--color-border)" }}>
          <Palette size={12} /> Theme
        </button>
        {site.isEnabled && (
          <button onClick={handleDisable} disabled={disableMutation.isPending}
            className="flex items-center gap-1 px-2.5 py-1.5 rounded-md text-xs disabled:opacity-50"
            style={{ color: "var(--color-danger)", border: "1px solid var(--color-danger)" }}>
            <Power size={12} /> Disable
          </button>
        )}
      </div>

      {showLdap && (
        <LdapDrawer siteId={site.id} onClose={() => setShowLdap(false)} />
      )}
      {showTheme && (
        <ThemeDrawer siteId={site.id} onClose={() => setShowTheme(false)} />
      )}
    </div>
  );
}

function SiteDrawer({ site, onClose }: { site: SiteDto; onClose: () => void }) {
  const queryClient = useQueryClient();
  const isNew = !site.id;
  const [form, setForm] = useState<SiteUpsertDto>({
    code: site.code ?? "",
    displayName: site.displayName ?? "",
    defaultThemeKey: site.defaultThemeKey ?? "syntera-default",
    databaseConnectionString: "",
    notes: site.notes,
    ldapDomains: site.ldapDomains ?? [],
  });
  const [domainInput, setDomainInput] = useState("");

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (isNew) return sitesApi.create(form);
      return sitesApi.update(site.id, form);
    },
    onSuccess: () => {
      toast.success("Site saved");
      void queryClient.invalidateQueries({ queryKey: SITES_KEY });
      onClose();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  return (
    <Drawer title={isNew ? "New Site" : `Edit ${site.displayName}`} onClose={onClose}>
      <div className="space-y-4">
        <Field label="Code">
          <input value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })}
            className="input" placeholder="kalventis" disabled={!isNew} />
        </Field>
        <Field label="Display Name">
          <input value={form.displayName} onChange={(e) => setForm({ ...form, displayName: e.target.value })}
            className="input" placeholder="PT Kalventis Surya Pratama" />
        </Field>
        <Field label="Database Connection String">
          <textarea value={form.databaseConnectionString} onChange={(e) => setForm({ ...form, databaseConnectionString: e.target.value })}
            className="input" rows={3} placeholder="Server=...;Database=syntera_kalventis;..." />
        </Field>
        <Field label="Default Theme Key">
          <input value={form.defaultThemeKey} onChange={(e) => setForm({ ...form, defaultThemeKey: e.target.value })}
            className="input" placeholder="kalventis-navy" />
        </Field>
        <Field label="Email Domains">
          <div className="flex flex-wrap gap-2 mb-2">
            {form.ldapDomains.map((d, i) => (
              <span key={i} className="px-2 py-1 rounded-md text-xs flex items-center gap-1"
                style={{ backgroundColor: "var(--color-background)", border: "1px solid var(--color-border)" }}>
                {d}
                <button onClick={() => setForm({ ...form, ldapDomains: form.ldapDomains.filter((_, j) => j !== i) })}>×</button>
              </span>
            ))}
          </div>
          <div className="flex gap-2">
            <input value={domainInput} onChange={(e) => setDomainInput(e.target.value)}
              className="input" placeholder="kalventis.com" />
            <button onClick={() => {
              if (domainInput && !form.ldapDomains.includes(domainInput)) {
                setForm({ ...form, ldapDomains: [...form.ldapDomains, domainInput.toLowerCase()] });
                setDomainInput("");
              }
            }} className="px-3 py-2 rounded-md text-sm" style={{ backgroundColor: "var(--color-primary)", color: "white" }}>
              Add
            </button>
          </div>
        </Field>
        <Field label="Notes">
          <textarea value={form.notes ?? ""} onChange={(e) => setForm({ ...form, notes: e.target.value })}
            className="input" rows={2} />
        </Field>

        <div className="flex justify-end gap-2 pt-4">
          <button onClick={onClose} className="px-4 py-2 rounded-md text-sm" style={{ border: "1px solid var(--color-border)" }}>
            Cancel
          </button>
          <button onClick={() => saveMutation.mutate()} disabled={saveMutation.isPending} className="px-4 py-2 rounded-md text-sm"
            style={{ backgroundColor: "var(--color-primary)", color: "white" }}>
            {saveMutation.isPending ? "Saving..." : "Save"}
          </button>
        </div>
      </div>
    </Drawer>
  );
}

function LdapDrawer({ siteId, onClose }: { siteId: string; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [cfg, setCfg] = useState<LdapConfigUpsertDto>({
    host: "", port: 636, useStartTls: false, baseDn: "", emailAttribute: "userPrincipalName",
    bindDn: null, bindPassword: null, userFilterTemplate: "(&(objectClass=user)({emailAttribute}={email}))",
    timeoutSeconds: 10, searchSubtree: true,
  });
  const [testEmail, setTestEmail] = useState("");
  const [testResult, setTestResult] = useState<LdapTestResult | null>(null);
  const [testing, setTesting] = useState(false);

  // Load existing LDAP config (silent fail if not configured yet).
  const { data: existing, isLoading: loading } = useQuery<LdapConfigDto>({
    queryKey: ["ldap-config", siteId],
    queryFn: () => sitesApi.getLdapConfig(siteId),
    enabled: !!siteId,
    retry: false,
  });

  // Sync loaded config into local form state.
  useEffectSyncToState(existing, (c) => {
    if (!c) return;
    setCfg({
      host: c.host, port: c.port, useStartTls: c.useStartTls, baseDn: c.baseDn,
      emailAttribute: c.emailAttribute, bindDn: c.bindDn, bindPassword: null,
      userFilterTemplate: c.userFilterTemplate, timeoutSeconds: c.timeoutSeconds,
      searchSubtree: c.searchSubtree,
    });
  });

  const saveMutation = useMutation({
    mutationFn: () => sitesApi.upsertLdapConfig(siteId, cfg),
    onSuccess: () => {
      toast.success("LDAP config saved");
      void queryClient.invalidateQueries({ queryKey: SITES_KEY });
      onClose();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  const test = async () => {
    if (!testEmail) { toast.error("Enter a test email first"); return; }
    setTesting(true);
    setTestResult(null);
    try {
      const req: LdapTestRequest = { ...cfg, testEmail };
      const result = await sitesApi.testLdap(req);
      setTestResult(result);
      if (result.success) toast.success(`LDAP OK (latency: ${result.latencyMs}ms)`);
      else toast.error(`LDAP test failed: ${result.errorMessage}`);
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Test failed");
    } finally {
      setTesting(false);
    }
  };

  if (loading) return <Drawer title="LDAP Configuration" onClose={onClose}><div>Loading...</div></Drawer>;

  return (
    <Drawer title="LDAP Configuration" onClose={onClose}>
      <div className="space-y-3">
        <Field label="Host"><input className="input" value={cfg.host} onChange={(e) => setCfg({ ...cfg, host: e.target.value })} placeholder="10.131.220.11" /></Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Port">
            <select className="input" value={cfg.port} onChange={(e) => setCfg({ ...cfg, port: parseInt(e.target.value) })}>
              <option value={636}>636 (LDAPS)</option>
              <option value={389}>389 (StartTLS)</option>
            </select>
          </Field>
          <Field label="Use StartTLS">
            <select className="input" value={cfg.useStartTls ? "1" : "0"} onChange={(e) => setCfg({ ...cfg, useStartTls: e.target.value === "1" })}>
              <option value="0">No</option>
              <option value="1">Yes</option>
            </select>
          </Field>
        </div>
        <Field label="Base DN"><input className="input" value={cfg.baseDn} onChange={(e) => setCfg({ ...cfg, baseDn: e.target.value })} placeholder="DC=KALVENTIS,DC=DOM" /></Field>
        <Field label="Email Attribute"><input className="input" value={cfg.emailAttribute} onChange={(e) => setCfg({ ...cfg, emailAttribute: e.target.value })} /></Field>
        <Field label="Bind DN (service account, optional)"><input className="input" value={cfg.bindDn ?? ""} onChange={(e) => setCfg({ ...cfg, bindDn: e.target.value || null })} /></Field>
        <Field label="Bind Password (leave empty to keep existing)"><input type="password" className="input" value={cfg.bindPassword ?? ""} onChange={(e) => setCfg({ ...cfg, bindPassword: e.target.value || null })} /></Field>
        <Field label="User Filter Template"><input className="input" value={cfg.userFilterTemplate} onChange={(e) => setCfg({ ...cfg, userFilterTemplate: e.target.value })} /></Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Timeout (sec)"><input type="number" className="input" value={cfg.timeoutSeconds} onChange={(e) => setCfg({ ...cfg, timeoutSeconds: parseInt(e.target.value) })} /></Field>
          <Field label="Search Scope">
            <select className="input" value={cfg.searchSubtree ? "1" : "0"} onChange={(e) => setCfg({ ...cfg, searchSubtree: e.target.value === "1" })}>
              <option value="1">Subtree</option>
              <option value="0">One Level</option>
            </select>
          </Field>
        </div>

        <div className="pt-4 border-t" style={{ borderColor: "var(--color-border)" }}>
          <h4 className="text-sm font-semibold mb-2">Test Connection</h4>
          <div className="flex gap-2">
            <input className="input" value={testEmail} onChange={(e) => setTestEmail(e.target.value)} placeholder="test.user@kalventis.com" />
            <button onClick={test} disabled={testing} className="px-3 py-2 rounded-md text-sm whitespace-nowrap"
              style={{ border: "1px solid var(--color-border)" }}>
              {testing ? "Testing..." : "Test"}
            </button>
          </div>
          {testResult && (
            <div className="mt-2 p-3 rounded-md text-xs" style={{ backgroundColor: "var(--color-background)" }}>
              <div><strong>Success:</strong> {testResult.success ? "Yes" : "No"}</div>
              {testResult.dn && <div><strong>DN:</strong> {testResult.dn}</div>}
              {testResult.displayName && <div><strong>Name:</strong> {testResult.displayName}</div>}
              {testResult.errorMessage && <div><strong>Error:</strong> {testResult.errorMessage}</div>}
              <div><strong>Latency:</strong> {testResult.latencyMs}ms</div>
            </div>
          )}
        </div>

        <div className="flex justify-end gap-2 pt-4">
          <button onClick={onClose} className="px-4 py-2 rounded-md text-sm" style={{ border: "1px solid var(--color-border)" }}>Cancel</button>
          <button onClick={() => saveMutation.mutate()} disabled={saveMutation.isPending}
            className="px-4 py-2 rounded-md text-sm" style={{ backgroundColor: "var(--color-primary)", color: "white" }}>
            {saveMutation.isPending ? "Saving..." : "Save"}
          </button>
        </div>
      </div>
    </Drawer>
  );
}

function ThemeDrawer({ siteId, onClose }: { siteId: string; onClose: () => void }) {
  return (
    <Drawer title="Theme Configuration" onClose={onClose}>
      <div className="text-sm" style={{ color: "var(--color-muted)" }}>
        <p>Theme palette management UI is available via API at <code>PUT /api/platform/sites/{siteId}/theme</code>.</p>
        <p className="mt-2">The palette is stored as JSON in the platform database and cached in-memory on the backend.</p>
        <p className="mt-2">Future UI work: visual color picker that writes to <code>LightPaletteJson</code> and <code>DarkPaletteJson</code>.</p>
      </div>
    </Drawer>
  );
}

// Unused import removed to keep linter happy.

/** Syncs a TanStack Query result into local state when the data changes. */
function useEffectSyncToState<T>(data: T | undefined, cb: (data: T | undefined) => void) {
  useEffect(() => { cb(data); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [data]);
}

function Drawer({ title, onClose, children }: { title: string; onClose: () => void; children: React.ReactNode }) {
  return (
    <div className="fixed inset-0 z-50 flex justify-end" style={{ backgroundColor: "rgba(0,0,0,0.4)" }} onClick={onClose}>
      <div
        className="w-full max-w-md h-full overflow-y-auto p-6"
        style={{ backgroundColor: "var(--color-surface)" }}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold">{title}</h2>
          <button onClick={onClose} className="text-2xl leading-none">×</button>
        </div>
        {children}
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
