import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  CheckCircle2,
  XCircle,
  ChevronDown,
  ChevronUp,
  ScrollText,
  Globe,
} from "lucide-react";
import { auditApi } from "../../api/audit";
import { ApiError } from "../../api/client";
import type { AuditLogDto } from "../../types";

/**
 * Pretty-print a JSON string. If the input is not valid JSON (corrupted
 * audit snapshot, schema drift, etc.) the raw string is returned so the
 * reviewer can still see what was recorded.
 */
function prettyJson(json: string): string {
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}

export default function AuditLogsPage() {
  const [filterAction, setFilterAction] = useState("");
  const [filterOutcome, setFilterOutcome] = useState("");
  const [filterSignature, setFilterSignature] = useState("");
  const [expandedId, setExpandedId] = useState<number | null>(null);
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

  // Signature-meaning filter is client-side: the backend AuditQuery contract
  // does not (yet) expose a SignatureMeaning parameter, so we filter the
  // already-fetched page. Case-insensitive substring match.
  const displayLogs = useMemo(() => {
    const all = logs ?? [];
    const needle = filterSignature.trim().toLowerCase();
    if (!needle) return all;
    return all.filter((l) => (l.signatureMeaning ?? "").toLowerCase().includes(needle));
  }, [logs, filterSignature]);

  const toggleRow = (id: number) =>
    setExpandedId((cur) => (cur === id ? null : id));

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
        <input
          className="input"
          placeholder="Filter by action (e.g., auth.login)"
          value={filterAction}
          onChange={(e) => setFilterAction(e.target.value)}
        />
        <select
          className="input"
          value={filterOutcome}
          onChange={(e) => setFilterOutcome(e.target.value)}
        >
          <option value="">All outcomes</option>
          <option value="success">Success</option>
          <option value="failure">Failure</option>
        </select>
        <input
          className="input"
          placeholder="Filter by signature meaning"
          value={filterSignature}
          onChange={(e) => setFilterSignature(e.target.value)}
        />
      </div>

      {loading ? (
        <div className="text-center py-8" style={{ color: "var(--color-muted)" }}>
          Loading...
        </div>
      ) : displayLogs.length === 0 ? (
        <div className="text-center py-8" style={{ color: "var(--color-muted)" }}>
          {error ?? "No audit logs found."}
        </div>
      ) : (
        <div className="space-y-1">
          {displayLogs.map((l) => {
            const isExpanded = expandedId === l.id;
            return (
              <div
                key={l.id}
                className="rounded-md text-sm"
                style={{
                  backgroundColor: "var(--color-surface)",
                  border: "1px solid var(--color-border)",
                }}
              >
                {/* Clickable row header — keyboard accessible (Enter / Space toggles). */}
                <div
                  role="button"
                  tabIndex={0}
                  aria-expanded={isExpanded}
                  aria-label={`Audit log ${l.id}: ${l.action}. Press Enter or Space to ${isExpanded ? "collapse" : "expand"} details.`}
                  onClick={() => toggleRow(l.id)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                      e.preventDefault();
                      toggleRow(l.id);
                    }
                  }}
                  className="flex items-start gap-3 p-3 cursor-pointer focus-visible:outline"
                  style={{
                    outlineOffset: "-1px",
                  }}
                >
                  {l.outcome === "success" ? (
                    <CheckCircle2
                      size={16}
                      className="mt-0.5 shrink-0"
                      style={{ color: "var(--color-success)" }}
                    />
                  ) : (
                    <XCircle
                      size={16}
                      className="mt-0.5 shrink-0"
                      style={{ color: "var(--color-danger)" }}
                    />
                  )}
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
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
                      <div className="text-xs mt-1" style={{ color: "var(--color-danger)" }}>
                        {l.errorMessage}
                      </div>
                    )}
                  </div>
                  {isExpanded ? (
                    <ChevronUp
                      size={16}
                      className="mt-0.5 shrink-0"
                      style={{ color: "var(--color-muted)" }}
                      aria-hidden
                    />
                  ) : (
                    <ChevronDown
                      size={16}
                      className="mt-0.5 shrink-0"
                      style={{ color: "var(--color-muted)" }}
                      aria-hidden
                    />
                  )}
                </div>

                {/* Expanded section: signature meaning + user agent + before/after JSON. */}
                {isExpanded && (
                  <div
                    className="px-3 pb-3 pt-1"
                    style={{
                      borderTop: "1px solid var(--color-border)",
                      borderLeft: "3px solid var(--color-primary)",
                    }}
                  >
                    {/* Signature Meaning — 21 CFR Part 11 §11.50 manifestation. */}
                    <div
                      className="flex items-start gap-2 py-2 text-xs italic"
                      style={{ color: "var(--color-muted)" }}
                    >
                      <ScrollText
                        size={14}
                        className="mt-0.5 shrink-0"
                        style={{ color: "var(--color-primary)" }}
                        aria-hidden
                      />
                      <span>
                        Signature meaning:{" "}
                        {l.signatureMeaning ?? "action performed (default)"}
                      </span>
                    </div>

                    {/* User Agent — previously not displayed. */}
                    <div
                      className="flex items-start gap-2 py-1 text-xs"
                      style={{ color: "var(--color-muted)" }}
                    >
                      <Globe
                        size={14}
                        className="mt-0.5 shrink-0"
                        style={{ color: "var(--color-muted)" }}
                        aria-hidden
                      />
                      <span style={{ wordBreak: "break-word" }}>
                        User agent: {l.actorUserAgent ?? "—"}
                      </span>
                    </div>

                    {/* Before state — amber to indicate "what changed from". */}
                    <details
                      className="mt-2 rounded-md"
                      style={{ border: "1px solid var(--color-border)" }}
                    >
                      <summary
                        className="cursor-pointer px-2 py-1 text-xs font-medium select-none"
                        style={{ color: "var(--color-warning)" }}
                      >
                        Before
                      </summary>
                      <div className="px-2 pb-2 pt-1">
                        {l.beforeJson ? (
                          <pre
                            className="text-xs font-mono p-2 rounded"
                            style={{
                              backgroundColor: "var(--color-background)",
                              color: "var(--color-text)",
                              overflowX: "auto",
                              whiteSpace: "pre-wrap",
                              margin: 0,
                            }}
                          >
                            {prettyJson(l.beforeJson)}
                          </pre>
                        ) : (
                          <div
                            className="text-xs italic"
                            style={{ color: "var(--color-muted)" }}
                          >
                            No before-state recorded (create/disable event)
                          </div>
                        )}
                      </div>
                    </details>

                    {/* After state — green to indicate "what changed to". */}
                    <details
                      className="mt-2 rounded-md"
                      style={{ border: "1px solid var(--color-border)" }}
                    >
                      <summary
                        className="cursor-pointer px-2 py-1 text-xs font-medium select-none"
                        style={{ color: "var(--color-success)" }}
                      >
                        After
                      </summary>
                      <div className="px-2 pb-2 pt-1">
                        {l.afterJson ? (
                          <pre
                            className="text-xs font-mono p-2 rounded"
                            style={{
                              backgroundColor: "var(--color-background)",
                              color: "var(--color-text)",
                              overflowX: "auto",
                              whiteSpace: "pre-wrap",
                              margin: 0,
                            }}
                          >
                            {prettyJson(l.afterJson)}
                          </pre>
                        ) : (
                          <div
                            className="text-xs italic"
                            style={{ color: "var(--color-muted)" }}
                          >
                            No after-state recorded (revoke/delete event)
                          </div>
                        )}
                      </div>
                    </details>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
