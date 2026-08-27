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
} from "../types";

const BASE = "/platform";

// ── Sites ──────────────────────────────────────────────
export const sitesApi = {
  list: () => get<SiteDto[]>(`${BASE}/sites`),
  get: (id: string) => get<SiteDto>(`${BASE}/sites/${id}`),
  update: (id: string, dto: SiteUpdateDto) => put<SiteDto>(`${BASE}/sites/${id}`, dto),

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
  publish: (id: string) => post<{ success: boolean }>(`${BASE}/role-templates/${id}/publish`),
  permissionCatalog: () => get<PermissionCatalogDto>(`${BASE}/role-templates/permission-catalog`),
};
