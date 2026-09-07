import { useEffect, useMemo, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { ClipboardCheck, ScrollText, Check, X } from "lucide-react";
import { roleTemplatesApi } from "../../api/platform";
import { ApiError } from "../../api/client";
import type { RoleTemplateApprovalDto } from "../../types";

const APPROVALS_KEY = ["role-template-approvals"] as const;
const TEMPLATES_KEY = ["role-templates"] as const;

/**
 * Default 21 CFR Part 11 §11.50 signature meaning for the approve action.
 * The approver can edit it freely (min 10 chars, ≤ 500 per backend validator).
 */
const DEFAULT_APPROVAL_SIGNATURE =
  "I approve this role template for publication per 21 CFR Part 11 §11.10";

/** Min length enforced client-side for the approval / rejection text fields. */
const MIN_TEXT_LENGTH = 10;

/**
 * Pretty-print a JSON string. If the input is not valid JSON (snapshot
 * schema drift, corrupted payload, etc.) the raw string is returned so the
 * reviewer can still see what was recorded. Mirrors the AuditLogsPage helper.
 */
function prettyJson(json: string): string {
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}

/**
 * Platform Admin → Approval Queue (Sprint 2.7 — Two-Person Rule)
 *
 * Lists all `pending` role template publish approvals. A Platform Admin
 * (other than the requester) reviews the request — the requested snapshot
 * JSON is rendered inline so the reviewer can see exactly what the
 * requester asked for at request time (defends against a requester editing
 * the template between request and approval).
 *
 * The backend rejects self-approval (`SELF_APPROVAL_FORBIDDEN` → 409);
 * the backend message surfaces in a toast.
 *
 * Empty state: only renders when TwoPerson:Enabled=true on the backend AND
 * no requests are currently pending. When the two-person rule is OFF the
 * queue stays empty (immediate-publish path bypasses approvals entirely).
 */
export default function ApprovalQueuePage() {
  const { data: approvals = [], isLoading: loading } = useQuery<RoleTemplateApprovalDto[]>({
    queryKey: APPROVALS_KEY,
    queryFn: () => roleTemplatesApi.listApprovals(),
  });

  // Filter to pending only — the backend returns ALL statuses (including
  // historical approved/rejected) so the queue UI focuses on actionable rows.
  const pending = useMemo(
    () => approvals.filter((a) => a.status === "pending"),
    [approvals],
  );

  const [reviewingId, setReviewingId] = useState<string | null>(null);
  const reviewing = useMemo(
    () => pending.find((a) => a.id === reviewingId) ?? null,
    [pending, reviewingId],
  );

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-2xl font-bold">Approval Queue</h1>
        <p className="text-sm" style={{ color: "var(--color-muted)" }}>
          Pending role-template publish requests. A second Platform Admin must
          approve each request before it is cloned to all sites (21 CFR Part 11
          §11.10(g) — two-person rule).
        </p>
      </div>

      {loading ? (
        <div className="text-center py-8" style={{ color: "var(--color-muted)" }}>
          Loading...
        </div>
      ) : pending.length === 0 ? (
        <div
          className="rounded-xl p-8 text-center"
          style={{
            backgroundColor: "var(--color-surface)",
            border: "1px solid var(--color-border)",
          }}
        >
          <ClipboardCheck
            size={32}
            className="mx-auto mb-3"
            style={{ color: "var(--color-muted)" }}
            aria-hidden
          />
          <p className="text-sm" style={{ color: "var(--color-muted)" }}>
            No pending approvals. New approval requests will appear here when a
            Platform Admin requests a role template publish (when two-person
            rule is enabled).
          </p>
        </div>
      ) : (
        <div className="space-y-3">
          {pending.map((a) => (
            <ApprovalRow key={a.id} approval={a} onReview={() => setReviewingId(a.id)} />
          ))}
        </div>
      )}

      {reviewing && (
        <ReviewDrawer approval={reviewing} onClose={() => setReviewingId(null)} />
      )}
    </div>
  );
}

function ApprovalRow({
  approval,
  onReview,
}: {
  approval: RoleTemplateApprovalDto;
  onReview: () => void;
}) {
  return (
    <div
      className="rounded-xl p-4"
      style={{
        backgroundColor: "var(--color-surface)",
        border: "1px solid var(--color-border)",
      }}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <h3 className="font-semibold">{approval.roleTemplateDisplayName}</h3>
            <span
              className="text-xs px-2 py-0.5 rounded-full"
              style={{
                backgroundColor: "var(--color-warning)",
                color: "white",
              }}
            >
              Pending
            </span>
          </div>
          <div
            className="text-xs font-mono mt-0.5 break-all"
            style={{ color: "var(--color-muted)" }}
          >
            {approval.roleTemplateKey}
          </div>
          <div className="text-xs mt-1" style={{ color: "var(--color-muted)" }}>
            Requested by{" "}
            <span style={{ color: "var(--color-text)" }}>{approval.requestedByEmail}</span>{" "}
            on {new Date(approval.requestedAt).toLocaleString()}
          </div>
          {approval.requesterSignatureMeaning && (
            <div
              className="text-xs italic mt-1 flex items-start gap-1.5"
              style={{ color: "var(--color-muted)" }}
            >
              <ScrollText
                size={12}
                className="mt-0.5 shrink-0"
                style={{ color: "var(--color-primary)" }}
                aria-hidden
              />
              <span>{approval.requesterSignatureMeaning}</span>
            </div>
          )}
        </div>
        <button
          type="button"
          onClick={onReview}
          className="px-3 py-2 rounded-lg text-sm font-medium shrink-0 min-h-[44px]"
          style={{
            backgroundColor: "var(--color-primary)",
            color: "var(--color-primary-foreground)",
          }}
        >
          Review
        </button>
      </div>
    </div>
  );
}

function ReviewDrawer({
  approval,
  onClose,
}: {
  approval: RoleTemplateApprovalDto;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const [action, setAction] = useState<"approve" | "reject" | null>(null);
  const [signatureMeaning, setSignatureMeaning] = useState(DEFAULT_APPROVAL_SIGNATURE);
  const [reason, setReason] = useState("");

  const invalidateAll = () => {
    void queryClient.invalidateQueries({ queryKey: APPROVALS_KEY });
    void queryClient.invalidateQueries({ queryKey: TEMPLATES_KEY });
  };

  const approveMutation = useMutation({
    mutationFn: () =>
      roleTemplatesApi.approve(approval.roleTemplateId, { signatureMeaning }),
    onSuccess: () => {
      toast.success("Approved — template published to all sites.");
      invalidateAll();
      onClose();
    },
    onError: (err) => {
      // Backend rejects self-approval (SELF_APPROVAL_FORBIDDEN → 409). The
      // ApiError envelope already carries a user-friendly message that
      // instructs the requester to ask another Platform Admin. Other 400/409
      // codes (validation, APPROVAL_NOT_PENDING, already-acted-on) also flow
      // through ApiError — display verbatim.
      toast.error(err instanceof ApiError ? err.message : "Failed to approve.");
    },
  });

  const rejectMutation = useMutation({
    mutationFn: () =>
      roleTemplatesApi.reject(approval.roleTemplateId, { reason }),
    onSuccess: () => {
      toast.success("Rejected — the publish request was cancelled.");
      invalidateAll();
      onClose();
    },
    onError: (err) => {
      toast.error(err instanceof ApiError ? err.message : "Failed to reject.");
    },
  });

  // Escape key + body scroll lock (matches SitesPage drawer pattern).
  useEffect(() => {
    const handleEsc = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", handleEsc);
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", handleEsc);
      document.body.style.overflow = "";
    };
  }, [onClose]);

  const canApprove = signatureMeaning.trim().length >= MIN_TEXT_LENGTH;
  const canReject = reason.trim().length >= MIN_TEXT_LENGTH;
  const submitting = approveMutation.isPending || rejectMutation.isPending;

  return (
    <div
      className="fixed inset-0 z-50 flex justify-end syntera-drawer-backdrop"
      style={{ backgroundColor: "rgba(0,0,0,0.4)" }}
      onClick={onClose}
    >
      <div
        className="w-full max-w-2xl h-full flex flex-col syntera-drawer-panel"
        style={{ backgroundColor: "var(--color-surface)" }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Sticky header */}
        <div
          className="flex items-center justify-between px-6 pt-6 pb-3 shrink-0 sticky top-0 z-10"
          style={{
            backgroundColor: "var(--color-surface)",
            borderBottom: "1px solid var(--color-border)",
          }}
        >
          <div>
            <h2 className="text-lg font-semibold">Review Publish Request</h2>
            <div
              className="text-xs font-mono mt-0.5"
              style={{ color: "var(--color-muted)" }}
            >
              {approval.roleTemplateKey}
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="text-2xl leading-none p-2 rounded-lg hover:opacity-70 transition-opacity min-h-[40px] min-w-[40px] flex items-center justify-center"
            aria-label="Close"
          >
            ×
          </button>
        </div>

        {/* Scrollable content */}
        <div className="flex-1 overflow-y-auto p-6 space-y-4">
          {/* Summary */}
          <div
            className="rounded-lg p-3 text-sm space-y-1"
            style={{
              backgroundColor: "var(--color-background)",
              border: "1px solid var(--color-border)",
            }}
          >
            <div>
              <span style={{ color: "var(--color-muted)" }}>Template: </span>
              <span className="font-medium">{approval.roleTemplateDisplayName}</span>
            </div>
            <div>
              <span style={{ color: "var(--color-muted)" }}>Requested by: </span>
              <span>{approval.requestedByEmail}</span>
            </div>
            <div>
              <span style={{ color: "var(--color-muted)" }}>Requested at: </span>
              <span>{new Date(approval.requestedAt).toLocaleString()}</span>
            </div>
            {approval.requesterSignatureMeaning && (
              <div className="italic" style={{ color: "var(--color-muted)" }}>
                “{approval.requesterSignatureMeaning}”
              </div>
            )}
          </div>

          {/* Requested snapshot — pretty-printed JSON, on render only (not on
              the list view) per the perf constraint. */}
          <details
            className="rounded-md"
            style={{ border: "1px solid var(--color-border)" }}
            open
          >
            <summary
              className="cursor-pointer px-3 py-2 text-sm font-medium select-none"
              style={{ color: "var(--color-primary)" }}
            >
              Requested snapshot (template state at request time)
            </summary>
            <div className="px-3 pb-3 pt-1">
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
                {prettyJson(approval.requestedSnapshotJson)}
              </pre>
            </div>
          </details>

          {/* Action picker — two buttons. Picking one swaps in a small inline
              form with a textarea + Submit/Cancel buttons. Self-approval is
              caught by the backend; the backend message surfaces in a toast. */}
          {action === null ? (
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => setAction("approve")}
                disabled={submitting}
                className="flex items-center gap-1.5 px-3 py-2 rounded-lg text-sm font-medium disabled:opacity-50 min-h-[44px]"
                style={{
                  backgroundColor: "var(--color-success)",
                  color: "white",
                }}
              >
                <Check size={16} /> Approve
              </button>
              <button
                type="button"
                onClick={() => setAction("reject")}
                disabled={submitting}
                className="flex items-center gap-1.5 px-3 py-2 rounded-lg text-sm font-medium disabled:opacity-50 min-h-[44px]"
                style={{
                  backgroundColor: "var(--color-danger)",
                  color: "white",
                }}
              >
                <X size={16} /> Reject
              </button>
            </div>
          ) : action === "approve" ? (
            <div
              className="rounded-lg p-3 space-y-2"
              style={{ border: "1px solid var(--color-border)" }}
            >
              <label
                className="text-xs font-medium block"
                style={{ color: "var(--color-muted)" }}
              >
                Signature meaning (21 CFR Part 11 §11.50) — min {MIN_TEXT_LENGTH}{" "}
                chars, max 500
              </label>
              <textarea
                className="input"
                rows={3}
                maxLength={500}
                value={signatureMeaning}
                onChange={(e) => setSignatureMeaning(e.target.value)}
                placeholder="I approve this role template for publication per 21 CFR Part 11 §11.10"
              />
              <div className="flex justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setAction(null)}
                  disabled={submitting}
                  className="px-4 py-2.5 rounded-lg text-sm min-h-[44px]"
                  style={{ border: "1px solid var(--color-border)" }}
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={() => approveMutation.mutate()}
                  disabled={!canApprove || submitting}
                  className="px-4 py-2.5 rounded-lg text-sm disabled:opacity-50 min-h-[44px]"
                  style={{
                    backgroundColor: "var(--color-success)",
                    color: "white",
                  }}
                >
                  {approveMutation.isPending ? "Approving..." : "Confirm Approve"}
                </button>
              </div>
            </div>
          ) : (
            <div
              className="rounded-lg p-3 space-y-2"
              style={{ border: "1px solid var(--color-border)" }}
            >
              <label
                className="text-xs font-medium block"
                style={{ color: "var(--color-muted)" }}
              >
                Reason for rejection — min {MIN_TEXT_LENGTH} chars, max 2000
                (visible to the requester and the auditor)
              </label>
              <textarea
                className="input"
                rows={3}
                maxLength={2000}
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                placeholder="Explain why this publish request is being rejected..."
              />
              <div className="flex justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setAction(null)}
                  disabled={submitting}
                  className="px-4 py-2.5 rounded-lg text-sm min-h-[44px]"
                  style={{ border: "1px solid var(--color-border)" }}
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={() => rejectMutation.mutate()}
                  disabled={!canReject || submitting}
                  className="px-4 py-2.5 rounded-lg text-sm disabled:opacity-50 min-h-[44px]"
                  style={{
                    backgroundColor: "var(--color-danger)",
                    color: "white",
                  }}
                >
                  {rejectMutation.isPending ? "Rejecting..." : "Confirm Reject"}
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
