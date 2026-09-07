/**
 * Platform Admin API — site management, LDAP config, theme, role templates.
 * All endpoints require platform-admin JWT claim.
 *
 * Sites are PRE-DEFINED in backend config. Only DisplayName, LdapDomains,
 * LDAP config, and Theme are editable. Code & ConnectionString are locked.
 */

import { get, post, put } from "./client";
import type {
  SiteDto,
  SiteUpdateDto,
  LdapConfigDto,
  LdapConfigUpsertDto,
  LdapTestRequest,
  LdapTestResult,
  ThemeUpsertDto,
  ThemeBundle,
  RoleTemplateDto,
  RoleTemplateUpsertDto,
  PermissionCatalogDto,
  UserDto,
  PublishResultDto,
  RoleTemplateApprovalDto,
  ApprovePublishRequest,
  RejectPublishRequest,
} from "../types";

const BASE = "/platform";

// ── Sites ──────────────────────────────────────────────
export const sitesApi = {
  list: () => get<SiteDto[]>(`${BASE}/sites`),
  get: (id: string) => get<SiteDto>(`${BASE}/sites/${id}`),
  update: (id: string, dto: SiteUpdateDto) => put<SiteDto>(`${BASE}/sites/${id}`, dto),

  /** Platform Admin → bootstrap first business admin for a site (chicken-and-egg fix). */
  assignBusinessAdmin: (siteId: string, email: string, displayName?: string) =>
    post<UserDto>(`${BASE}/sites/${siteId}/business-admin`, { email, displayName }),

  /** List all business admins for a site. */
  listBusinessAdmins: (siteId: string) =>
    get<UserDto[]>(`${BASE}/sites/${siteId}/business-admins`),

  /** Revoke business admin role from a user. */
  revokeBusinessAdmin: async (siteId: string, userId: string) => {
    const { api } = await import("./client");
    await api.delete(`${BASE}/sites/${siteId}/business-admin/${userId}`);
  },

  // ─── System Admin management ──────────────────────────────────

  /** Platform Admin → assign System Admin for a site. */
  assignSystemAdmin: (siteId: string, email: string, displayName?: string) =>
    post<UserDto>(`${BASE}/sites/${siteId}/system-admin`, { email, displayName }),

  /** List all System Admins for a site. */
  listSystemAdmins: (siteId: string) =>
    get<UserDto[]>(`${BASE}/sites/${siteId}/system-admins`),

  /** Revoke System Admin role from a user. */
  revokeSystemAdmin: async (siteId: string, userId: string) => {
    const { api } = await import("./client");
    await api.delete(`${BASE}/sites/${siteId}/system-admin/${userId}`);
  },

  getLdapConfig: (siteId: string) => get<LdapConfigDto>(`${BASE}/sites/${siteId}/ldap-config`),
  upsertLdapConfig: (siteId: string, dto: LdapConfigUpsertDto) =>
    put<LdapConfigDto>(`${BASE}/sites/${siteId}/ldap-config`, dto),
  testLdap: (req: LdapTestRequest) => post<LdapTestResult>(`${BASE}/sites/ldap-test`, req),

  getTheme: (siteId: string) => get<ThemeBundle>(`${BASE}/sites/${siteId}/theme`),
  upsertTheme: (siteId: string, dto: ThemeUpsertDto) =>
    put<ThemeBundle>(`${BASE}/sites/${siteId}/theme`, dto),
};

// ── Role Templates ─────────────────────────────────────
export const roleTemplatesApi = {
  list: () => get<RoleTemplateDto[]>(`${BASE}/role-templates`),
  get: (id: string) => get<RoleTemplateDto>(`${BASE}/role-templates/${id}`),
  create: (dto: RoleTemplateUpsertDto) => post<RoleTemplateDto>(`${BASE}/role-templates`, dto),
  update: (id: string, dto: RoleTemplateUpsertDto) =>
    put<RoleTemplateDto>(`${BASE}/role-templates/${id}`, dto),

  /**
   * Publish a role template. Behavior branches on backend config:
   *   - TwoPerson:Enabled=false (default) → `{ status: "published" }`
   *     (or the legacy shape `{ success: true }` — backward compat).
   *   - TwoPerson:Enabled=true            → `{ status: "pending", approvalId }`
   *     (a second Platform Admin must call `approve` to complete).
   */
  publish: (id: string) => post<PublishResultDto>(`${BASE}/role-templates/${id}/publish`),
  permissionCatalog: () => get<PermissionCatalogDto>(`${BASE}/role-templates/permission-catalog`),

  // ── Sprint 2.7: Two-person approval workflow ─────────────────────────
  /** List ALL approvals (any status). Frontend filters `status === "pending"`. */
  listApprovals: () =>
    get<RoleTemplateApprovalDto[]>(`${BASE}/role-templates/approvals`),
  /** Get one approval by id (any status). */
  getApproval: (id: string) =>
    get<RoleTemplateApprovalDto>(`${BASE}/role-templates/approvals/${id}`),
  /** Approve a pending publish request. Service rejects self-approval (409 SELF_APPROVAL_FORBIDDEN). */
  approve: (templateId: string, req: ApprovePublishRequest) =>
    post<{ success: true; status: "approved" }>(`${BASE}/role-templates/${templateId}/approve`, req),
  /** Reject a pending publish request. Self-rejection IS allowed (requester can withdraw). */
  reject: (templateId: string, req: RejectPublishRequest) =>
    post<{ success: true; status: "rejected" }>(`${BASE}/role-templates/${templateId}/reject`, req),
};
