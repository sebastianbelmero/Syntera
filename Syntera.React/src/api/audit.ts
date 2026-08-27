/**
 * Audit Log API — query immutable, hash-chained audit trail.
 */

import { get } from "./client";
import type { AuditLogDto, AuditLogQuery } from "../types";

export const auditApi = {
  query: (q: AuditLogQuery = {}) =>
    get<AuditLogDto[]>("/audit/logs", {
      params: {
        from: q.from,
        to: q.to,
        action: q.action,
        actorUserId: q.actorUserId,
        outcome: q.outcome,
        skip: q.skip ?? 0,
        take: q.take ?? 50,
      },
    }),
};
