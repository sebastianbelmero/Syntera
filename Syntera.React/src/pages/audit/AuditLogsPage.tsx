import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { toast } from "sonner";
import { CheckCircle2, XCircle } from "lucide-react";
import { auditApi } from "../../api/audit";
import { ApiError } from "../../api/client";
import type { AuditLogDto } from "../../types";

export default function AuditLogsPage() {
  const [filterAction, setFilterAction] = useState("");
  const [filterOutcome, setFilterOutcome] = useState("");
  const [error, setError] = useState<string | null>(null);

  const { data: logs, isLoading: loading } = useQuery<AuditLogDto[]>({
    queryKey: ["audit-logs", filterAction, filterOutcome],
    queryFn: async () => {
      try {
        setError(null);
        return await auditApi.query({
          action: filterAction || undefined,
          outcome: filterOutcome || undefined,
          take: 100,
        });
      } catch (err) {
        const msg = err instanceof ApiError ? err.message : "Failed to load";
        setError(msg);
        toast.error(msg);
        return [];
      }
    },
  });

  const displayLogs = logs ?? [];

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-2xl font-bold">Audit Logs</h1>
        <p className="text-sm" style={{ color: "var(--color-muted)" }}>
          Immutable, hash-chained audit trail. Platform admins see platform-level events;
          site admins see their own site only.
        </p>
      </div>

      <div className="flex flex-col sm:flex-row gap-3">
        <input className="input" placeholder="Filter by action (e.g., auth.login)" value={filterAction}
          onChange={(e) => setFilterAction(e.target.value)} />
        <select className="input" value={filterOutcome} onChange={(e) => setFilterOutcome(e.target.value)}>
          <option value="">All outcomes</option>
          <option value="success">Success</option>
          <option value="failure">Failure</option>
        </select>
      </div>

      {loading ? (
        <div className="text-center py-8" style={{ color: "var(--color-muted)" }}>Loading...</div>
      ) : displayLogs.length === 0 ? (
        <div className="text-center py-8" style={{ color: "var(--color-muted)" }}>
          {error ?? "No audit logs found."}
        </div>
      ) : (
        <div className="space-y-1">
          {displayLogs.map((l) => (
            <div key={l.id} className="rounded-md p-3 flex items-start gap-3 text-sm"
              style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}>
              {l.outcome === "success"
                ? <CheckCircle2 size={16} className="mt-0.5 shrink-0" style={{ color: "var(--color-success)" }} />
                : <XCircle size={16} className="mt-0.5 shrink-0" style={{ color: "var(--color-danger)" }} />}
              <div className="flex-1">
                <div className="flex items-center gap-2">
                  <span className="font-mono text-xs">{l.action}</span>
                  <span className="text-xs" style={{ color: "var(--color-muted)" }}>
                    {new Date(l.timestamp).toLocaleString()}
                  </span>
                </div>
                <div className="text-xs" style={{ color: "var(--color-muted)" }}>
                  {l.actorEmail ?? "anonymous"} {l.actorIp && `· ${l.actorIp}`}
                  {l.targetType && ` · ${l.targetType}:${l.targetId}`}
                </div>
                {l.errorMessage && (
                  <div className="text-xs mt-1" style={{ color: "var(--color-danger)" }}>{l.errorMessage}</div>
                )}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
