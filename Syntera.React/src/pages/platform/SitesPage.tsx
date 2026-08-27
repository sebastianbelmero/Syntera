import { useEffect, useRef, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { KeyRound, Server } from "lucide-react";
import { sitesApi } from "../../api/platform";
import { ApiError } from "../../api/client";
import type { SiteDto, LdapConfigDto, LdapConfigUpsertDto, LdapTestRequest, LdapTestResult } from "../../types";

const SITES_KEY = ["sites"] as const;

/**
 * Platform Admin → Site Management.
 *
 * The 6 sites (Kalventis, Kalbe, Fima, GOF, Dankos, Hexpharm) are
 * PRE-DEFINED in backend configuration (appsettings.json → Sites[]).
 * They cannot be created, disabled, or deleted from the frontend.
 *
 * The only thing editable from the frontend is the LDAP configuration
 * per site. Themes are also pre-seeded from config and not editable
 * via UI.
 */
export default function SitesPage() {
  const [ldapSite, setLdapSite] = useState<SiteDto | null>(null);

  const { data: sites = [], isLoading: loading } = useQuery<SiteDto[]>({
    queryKey: SITES_KEY,
    queryFn: () => sitesApi.list(),
  });

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-2xl font-bold">Sites</h1>
        <p className="text-sm" style={{ color: "var(--color-muted)" }}>
          6 fixed sites — configure LDAP for each. Database connection and theme
          are managed via backend configuration (appsettings.json).
        </p>
      </div>

      {loading ? (
        <div className="text-center py-8" style={{ color: "var(--color-muted)" }}>Loading...</div>
      ) : sites.length === 0 ? (
        <div className="rounded-xl p-6 text-center"
          style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}>
          <Server className="mx-auto mb-2 opacity-50" size={32} />
          <p className="text-sm" style={{ color: "var(--color-muted)" }}>
            No sites found. Run <code>dotnet run</code> in Development mode to
            trigger automatic seeding of the 6 predefined sites.
          </p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {sites.map((s) => (
            <SiteCard key={s.id} site={s} onConfigureLdap={() => setLdapSite(s)} />
          ))}
        </div>
      )}

      {ldapSite && (
        <LdapDrawer site={ldapSite} onClose={() => setLdapSite(null)} />
      )}
    </div>
  );
}

function SiteCard({ site, onConfigureLdap }: { site: SiteDto; onConfigureLdap: () => void }) {
  const swatch = site.defaultThemeKey.split("-")[0] ?? "syntera";
  const swatchColor = THEME_SWATCH[swatch] ?? "#0B3D6F";

  return (
    <div
      className="rounded-xl p-5"
      style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}
    >
      <div className="flex items-start gap-3 mb-3">
        <div
          className="w-10 h-10 rounded-lg flex-shrink-0"
          style={{ backgroundColor: swatchColor }}
        />
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <h3 className="font-semibold text-lg truncate">{site.displayName}</h3>
            {!site.isEnabled && (
              <span className="text-xs px-2 py-0.5 rounded-full flex-shrink-0"
                style={{ backgroundColor: "var(--color-danger)", color: "white" }}>
                Disabled
              </span>
            )}
          </div>
          <div className="text-xs mt-0.5 font-mono" style={{ color: "var(--color-muted)" }}>
            {site.code}
          </div>
        </div>
      </div>

      <div className="text-xs space-y-1 mb-4">
        <div>
          <strong>Email domains:</strong>{" "}
          {site.ldapDomains.length > 0 ? site.ldapDomains.join(", ") : "—"}
        </div>
        <div><strong>Theme:</strong> {site.defaultThemeKey}</div>
      </div>

      <button
        onClick={onConfigureLdap}
        className="w-full flex items-center justify-center gap-2 px-3 py-2 rounded-lg text-sm font-medium transition hover:opacity-90"
        style={{ backgroundColor: "var(--color-primary)", color: "white" }}
      >
        <KeyRound size={16} />
        Configure LDAP
      </button>
    </div>
  );
}

function LdapDrawer({ site, onClose }: { site: SiteDto; onClose: () => void }) {
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
    queryKey: ["ldap-config", site.id],
    queryFn: () => sitesApi.getLdapConfig(site.id),
    enabled: !!site.id,
    retry: false,
  });

  // Sync loaded config into local form state when data arrives.
  // Using a ref to track the last-seen config id so we only setState when
  // the query actually returns new data (avoids redundant renders).
  const lastConfigRef = useRef<string | null>(null);
  useEffect(() => {
    if (!existing) return;
    const sig = `${existing.host}|${existing.port}|${existing.baseDn}`;
    if (lastConfigRef.current === sig) return;
    lastConfigRef.current = sig;
    setCfg({
      host: existing.host, port: existing.port, useStartTls: existing.useStartTls,
      baseDn: existing.baseDn, emailAttribute: existing.emailAttribute,
      bindDn: existing.bindDn, bindPassword: null,
      userFilterTemplate: existing.userFilterTemplate,
      timeoutSeconds: existing.timeoutSeconds, searchSubtree: existing.searchSubtree,
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

  if (loading) return <Drawer title={`LDAP Configuration — ${site.displayName}`} onClose={onClose}><div>Loading...</div></Drawer>;

  return (
    <Drawer title={`LDAP Configuration — ${site.displayName}`} onClose={onClose}>
      <div className="space-y-3">
        <div className="p-3 rounded-md text-xs" style={{ backgroundColor: "var(--color-background)" }}>
          <strong>Site:</strong> {site.displayName} ({site.code})<br />
          <strong>Email domain:</strong> {site.ldapDomains.join(", ")}
        </div>

        <Field label="Host">
          <input className="input" value={cfg.host} onChange={(e) => setCfg({ ...cfg, host: e.target.value })}
            placeholder="10.131.220.11" />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Port">
            <select className="input" value={cfg.port} onChange={(e) => setCfg({ ...cfg, port: parseInt(e.target.value) })}>
              <option value={636}>636 (LDAPS)</option>
              <option value={389}>389 (StartTLS)</option>
            </select>
          </Field>
          <Field label="Use StartTLS">
            <select className="input" value={cfg.useStartTls ? "1" : "0"}
              onChange={(e) => setCfg({ ...cfg, useStartTls: e.target.value === "1" })}>
              <option value="0">No</option>
              <option value="1">Yes</option>
            </select>
          </Field>
        </div>
        <Field label="Base DN">
          <input className="input" value={cfg.baseDn} onChange={(e) => setCfg({ ...cfg, baseDn: e.target.value })}
            placeholder="DC=KALVENTIS,DC=DOM" />
        </Field>
        <Field label="Email Attribute">
          <input className="input" value={cfg.emailAttribute}
            onChange={(e) => setCfg({ ...cfg, emailAttribute: e.target.value })} />
        </Field>
        <Field label="Bind DN (service account, optional)">
          <input className="input" value={cfg.bindDn ?? ""}
            onChange={(e) => setCfg({ ...cfg, bindDn: e.target.value || null })} />
        </Field>
        <Field label="Bind Password (leave empty to keep existing)">
          <input type="password" className="input" value={cfg.bindPassword ?? ""}
            onChange={(e) => setCfg({ ...cfg, bindPassword: e.target.value || null })} />
        </Field>
        <Field label="User Filter Template">
          <input className="input" value={cfg.userFilterTemplate}
            onChange={(e) => setCfg({ ...cfg, userFilterTemplate: e.target.value })} />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Timeout (sec)">
            <input type="number" className="input" value={cfg.timeoutSeconds}
              onChange={(e) => setCfg({ ...cfg, timeoutSeconds: parseInt(e.target.value) })} />
          </Field>
          <Field label="Search Scope">
            <select className="input" value={cfg.searchSubtree ? "1" : "0"}
              onChange={(e) => setCfg({ ...cfg, searchSubtree: e.target.value === "1" })}>
              <option value="1">Subtree</option>
              <option value="0">One Level</option>
            </select>
          </Field>
        </div>

        <div className="pt-4 border-t" style={{ borderColor: "var(--color-border)" }}>
          <h4 className="text-sm font-semibold mb-2">Test Connection</h4>
          <div className="flex gap-2">
            <input className="input" value={testEmail}
              onChange={(e) => setTestEmail(e.target.value)}
              placeholder={`test.user@${site.ldapDomains[0] ?? "example.com"}`} />
            <button onClick={test} disabled={testing}
              className="px-3 py-2 rounded-md text-sm whitespace-nowrap disabled:opacity-50"
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
          <button onClick={onClose} className="px-4 py-2 rounded-md text-sm"
            style={{ border: "1px solid var(--color-border)" }}>
            Cancel
          </button>
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

// ─── Theme swatches (matches backend seed palettes) ────────────────
const THEME_SWATCH: Record<string, string> = {
  kalventis: "#007A4D",
  kalbe: "#E2231A",
  fima: "#6B46C1",
  gof: "#C2410C",
  dankos: "#0054A6",
  hexpharm: "#00796B",
  syntera: "#0B3D6F",
};

// ─── Helpers ────────────────────────────────────────────────────────

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
