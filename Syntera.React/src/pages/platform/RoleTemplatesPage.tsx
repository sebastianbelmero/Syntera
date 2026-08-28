import { useEffect, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Plus, Pencil, Send } from "lucide-react";
import { roleTemplatesApi } from "../../api/platform";
import { ApiError } from "../../api/client";
import type { RoleTemplateDto, RoleTemplateUpsertDto, PermissionCatalogDto, PermissionGroupDto } from "../../types";

const TEMPLATES_KEY = ["role-templates"] as const;

export default function RoleTemplatesPage() {
  const [editing, setEditing] = useState<RoleTemplateDto | null>(null);
  const [creating, setCreating] = useState(false);

  const { data: templates = [], isLoading: loading } = useQuery<RoleTemplateDto[]>({
    queryKey: TEMPLATES_KEY,
    queryFn: () => roleTemplatesApi.list(),
  });

  const { data: catalog } = useQuery<PermissionCatalogDto>({
    queryKey: ["permission-catalog"],
    queryFn: () => roleTemplatesApi.permissionCatalog(),
  });

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">Role Templates</h1>
          <p className="text-sm" style={{ color: "var(--color-muted)" }}>
            Define role templates. Publish clones them into every enabled site.
          </p>
        </div>
        <button onClick={() => setCreating(true)} className="flex items-center gap-1.5 px-3 py-2 rounded-lg text-sm font-medium"
          style={{ backgroundColor: "var(--color-primary)", color: "white" }}>
          <Plus size={16} /> New Template
        </button>
      </div>

      {loading ? (
        <div className="text-center py-8" style={{ color: "var(--color-muted)" }}>Loading...</div>
      ) : templates.length === 0 ? (
        <div className="rounded-xl p-8 text-center"
          style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}>
          <p className="text-sm" style={{ color: "var(--color-muted)" }}>
            No role templates yet. Click "New Template" to create one.
          </p>
        </div>
      ) : (
        <div className="space-y-3">
          {templates.map((t) => (
            <TemplateRow key={t.id} template={t} onEdit={() => setEditing(t)} />
          ))}
        </div>
      )}

      {(editing || creating) && catalog && (
        <TemplateDrawer
          template={editing}
          catalog={catalog}
          onClose={() => { setEditing(null); setCreating(false); }}
        />
      )}
    </div>
  );
}

function TemplateRow({ template, onEdit }: { template: RoleTemplateDto; onEdit: () => void }) {
  const queryClient = useQueryClient();

  const publishMutation = useMutation({
    mutationFn: () => roleTemplatesApi.publish(template.id),
    onSuccess: () => {
      toast.success("Template published");
      void queryClient.invalidateQueries({ queryKey: TEMPLATES_KEY });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  const handlePublish = () => {
    if (!confirm(`Publish role template "${template.key}"? This will clone/update the role in every enabled site.`)) return;
    publishMutation.mutate();
  };

  return (
    <div className="rounded-xl p-4"
      style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}>
      <div className="flex items-start justify-between">
        <div>
          <div className="flex items-center gap-2">
            <h3 className="font-semibold">{template.displayName}</h3>
            {template.isSiteAdminRole && (
              <span className="text-xs px-2 py-0.5 rounded-full"
                style={{ backgroundColor: "var(--color-warning)", color: "white" }}>Site Admin</span>
            )}
            {template.isPublished ? (
              <span className="text-xs px-2 py-0.5 rounded-full"
                style={{ backgroundColor: "var(--color-success)", color: "white" }}>Published v{template.version}</span>
            ) : (
              <span className="text-xs px-2 py-0.5 rounded-full"
                style={{ backgroundColor: "var(--color-muted)", color: "white" }}>Draft</span>
            )}
          </div>
          <div className="text-xs font-mono mt-0.5" style={{ color: "var(--color-muted)" }}>{template.key}</div>
          {template.description && <p className="text-xs mt-1" style={{ color: "var(--color-muted)" }}>{template.description}</p>}
        </div>
        <div className="flex gap-2">
          <button onClick={onEdit} className="p-1.5 rounded-md" style={{ border: "1px solid var(--color-border)" }}>
            <Pencil size={14} />
          </button>
          {!template.isPublished && (
            <button onClick={handlePublish} disabled={publishMutation.isPending}
              className="p-1.5 rounded-md disabled:opacity-50"
              style={{ backgroundColor: "var(--color-primary)", color: "white" }} title="Publish">
              <Send size={14} />
            </button>
          )}
        </div>
      </div>
      {template.permissionKeys.length > 0 && (
        <div className="flex flex-wrap gap-1 mt-3">
          {template.permissionKeys.map((p) => (
            <span key={p} className="text-xs px-2 py-0.5 rounded"
              style={{ backgroundColor: "var(--color-background)" }}>{p}</span>
          ))}
        </div>
      )}
    </div>
  );
}

function TemplateDrawer({ template, catalog, onClose }: {
  template: RoleTemplateDto | null;
  catalog: PermissionCatalogDto;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const isNew = !template;
  const [form, setForm] = useState<RoleTemplateUpsertDto>({
    key: template?.key ?? "",
    displayName: template?.displayName ?? "",
    description: template?.description ?? null,
    isSiteAdminRole: template?.isSiteAdminRole ?? false,
    permissionKeys: template?.permissionKeys ?? [],
  });

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (isNew) return roleTemplatesApi.create(form);
      return roleTemplatesApi.update(template!.id, form);
    },
    onSuccess: () => {
      toast.success("Saved");
      void queryClient.invalidateQueries({ queryKey: TEMPLATES_KEY });
      onClose();
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : "Failed"),
  });

  // Escape key + body scroll lock
  useEffect(() => {
    const handleEsc = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    document.addEventListener("keydown", handleEsc);
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", handleEsc);
      document.body.style.overflow = "";
    };
  }, [onClose]);

  const togglePerm = (key: string) => {
    setForm((f) => ({
      ...f,
      permissionKeys: f.permissionKeys.includes(key)
        ? f.permissionKeys.filter((k) => k !== key)
        : [...f.permissionKeys, key],
    }));
  };

  return (
    <div className="fixed inset-0 z-50 flex justify-end syntera-drawer-backdrop" style={{ backgroundColor: "rgba(0,0,0,0.4)" }} onClick={onClose}>
      <div className="w-full max-w-2xl h-full flex flex-col syntera-drawer-panel"
        style={{ backgroundColor: "var(--color-surface)" }}
        onClick={(e) => e.stopPropagation()}>
        {/* Sticky header */}
        <div className="flex items-center justify-between px-6 pt-6 pb-3 shrink-0 sticky top-0 z-10"
          style={{ backgroundColor: "var(--color-surface)", borderBottom: "1px solid var(--color-border)" }}>
          <h2 className="text-lg font-semibold">{isNew ? "New Role Template" : `Edit ${template?.displayName}`}</h2>
          <button onClick={onClose} className="text-2xl leading-none p-1 rounded hover:opacity-70" aria-label="Close">×</button>
        </div>

        {/* Scrollable content */}
        <div className="flex-1 overflow-y-auto p-6 space-y-4">
          <Field label="Key"><input className="input" value={form.key} disabled={!isNew}
            onChange={(e) => setForm({ ...form, key: e.target.value })} placeholder="viewer" /></Field>
          <Field label="Display Name"><input className="input" value={form.displayName}
            onChange={(e) => setForm({ ...form, displayName: e.target.value })} placeholder="Viewer" /></Field>
          <Field label="Description"><input className="input" value={form.description ?? ""}
            onChange={(e) => setForm({ ...form, description: e.target.value })} /></Field>
          <label className="flex items-center gap-2">
            <input type="checkbox" checked={form.isSiteAdminRole}
              onChange={(e) => setForm({ ...form, isSiteAdminRole: e.target.checked })} />
            <span className="text-sm">Site Admin Role (grants site-business-admin privileges)</span>
          </label>

          <div>
            <h4 className="text-sm font-semibold mb-2">Permissions ({form.permissionKeys.length} selected)</h4>
            <div className="space-y-3 max-h-96 overflow-y-auto">
              {catalog.groups.map((g: PermissionGroupDto) => (
                <div key={g.group}>
                  <div className="text-xs font-medium uppercase tracking-wide mb-1" style={{ color: "var(--color-muted)" }}>
                    {g.group}
                  </div>
                  <div className="grid grid-cols-2 gap-1">
                    {g.permissions.map((p) => (
                      <label key={p.key} className="flex items-start gap-2 p-1.5 rounded text-xs cursor-pointer hover:opacity-80">
                        <input type="checkbox" checked={form.permissionKeys.includes(p.key)}
                          onChange={() => togglePerm(p.key)} />
                        <div>
                          <div className="font-mono">{p.key}</div>
                          <div style={{ color: "var(--color-muted)" }}>{p.displayName}</div>
                        </div>
                      </label>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Sticky footer with Cancel + Save */}
          <div className="flex justify-end gap-2 pt-4 sticky bottom-0" style={{ backgroundColor: "var(--color-surface)" }}>
            <button onClick={onClose} className="px-4 py-2 rounded-md text-sm" style={{ border: "1px solid var(--color-border)" }}>Cancel</button>
            <button onClick={() => saveMutation.mutate()} disabled={saveMutation.isPending}
              className="px-4 py-2 rounded-md text-sm disabled:opacity-50"
              style={{ backgroundColor: "var(--color-primary)", color: "white" }}>
              {saveMutation.isPending ? "Saving..." : "Save"}
            </button>
          </div>
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
