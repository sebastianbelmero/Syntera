/**
 * Shared domain types — mirror of backend DTOs for the IAM platform.
 * All currency/decimal fields are typed as number (scale 18,2 fits JS doubles).
 */

export interface ApiResponse<T> {
  success: boolean;
  data?: T;
  message?: string;
  errorCode?: string;
  fieldErrors?: FieldError[];
}

export interface FieldError {
  field: string;
  message: string;
}

// ── Auth ────────────────────────────────────────────────
export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  expiresAt: string;
  refreshToken: string;
  profile: UserProfile;
  theme: ThemeBundle;
}

export interface RefreshResponse {
  accessToken: string;
  expiresAt: string;
  refreshToken: string;
  profile: UserProfile;
  theme: ThemeBundle;
}

export interface UserProfile {
  userId: string;
  email: string;
  displayName: string;
  title: string | null;
  scope: "platform" | "site" | "anonymous";
  siteId: string | null;
  siteCode: string | null;
  siteDisplayName: string | null;
  roles: string[];
  permissions: string[];
}

export interface ThemeBundle {
  themeKey: string;
  light: ThemePalette;
  dark: ThemePalette;
  logoUrl: string | null;
}

export interface ThemePalette {
  primary: string;
  accent: string;
  background: string;
  surface: string;
  text: string;
  muted: string;
  border: string;
  success: string;
  warning: string;
  danger: string;
}

// ── Sites ──────────────────────────────────────────────
export interface SiteDto {
  id: string;
  code: string;
  displayName: string;
  defaultThemeKey: string;
  isEnabled: boolean;
  notes: string | null;
  ldapDomains: string[];
  createdAt: string;
  updatedAt: string;
}

/** Editable fields for a site. Code & ConnectionString are locked. */
export interface SiteUpdateDto {
  displayName: string;
  ldapDomains: string[];
}

export interface LdapConfigDto {
  siteId: string;
  host: string;
  port: number;
  useStartTls: boolean;
  baseDn: string;
  upnDomain: string | null;
}

export interface LdapConfigUpsertDto {
  host: string;
  port: number;
  useStartTls: boolean;
  baseDn: string;
  upnDomain: string | null;
}

export interface LdapTestRequest {
  host: string;
  port: number;
  useStartTls: boolean;
  baseDn: string;
  upnDomain: string | null;
  testEmail: string;
  testPassword: string;
}

export interface LdapTestResult {
  success: boolean;
  dn: string | null;
  displayName: string | null;
  email: string | null;
  errorMessage: string | null;
  latencyMs: number;
}

export interface ThemeUpsertDto {
  themeKey: string;
  light: ThemePalette;
  dark: ThemePalette;
  logoUrl?: string | null;
}

// ── Role Templates ─────────────────────────────────────
export interface RoleTemplateDto {
  id: string;
  key: string;
  displayName: string;
  description: string | null;
  isSiteAdminRole: boolean;
  isPublished: boolean;
  version: number;
  permissionKeys: string[];
  createdAt: string;
  updatedAt: string;
}

export interface RoleTemplateUpsertDto {
  key: string;
  displayName: string;
  description?: string | null;
  isSiteAdminRole: boolean;
  permissionKeys: string[];
}

export interface PermissionDto {
  id: string;
  key: string;
  displayName: string;
  group: string;
  isPlatformOnly: boolean;
}

export interface PermissionCatalogDto {
  groups: PermissionGroupDto[];
}

export interface PermissionGroupDto {
  group: string;
  permissions: PermissionDto[];
}

// ── Users ──────────────────────────────────────────────
export interface UserDto {
  id: string;
  email: string;
  displayName: string;
  title: string | null;
  isEnabled: boolean;
  lastLoginAt: string | null;
  permissionsVersion: number;
  roles: RoleAssignmentDto[];
  directPermissions: DirectPermissionDto[];
  createdAt: string;
  updatedAt: string;
}

export interface RoleAssignmentDto {
  roleId: string;
  roleKey: string;
  roleDisplayName: string;
  assignedBy: string;
  assignedAt: string;
  expiresAt: string | null;
}

export interface DirectPermissionDto {
  id: string;
  permissionKey: string;
  permissionDisplayName: string;
  reason: string;
  approvedBy: string;
  approvedByEmail: string;
  grantedAt: string;
  expiresAt: string;
  isDeny: boolean;
  isRevoked: boolean;
}

export interface UserUpsertDto {
  email: string;
  displayName: string;
  title: string | null;
  isEnabled: boolean;
}

export interface AssignRoleDto {
  userId: string;
  roleId: string;
  expiresAt?: string | null;
  reason?: string | null;
}

export interface RevokeRoleDto {
  userId: string;
  roleId: string;
}

export interface GrantDirectPermissionDto {
  userId: string;
  permissionId: string;
  reason: string;
  expiresAt: string;
  isDeny?: boolean;
}

export interface RevokeDirectPermissionDto {
  userPermissionId: string;
}

export interface UserSyncResultDto {
  syncHistoryId: string;
  status: "running" | "success" | "partial" | "failed";
  usersFound: number;
  usersCreated: number;
  usersUpdated: number;
  usersDisabled: number;
  errors: string | null;
}

// ── Roles (site-level) ─────────────────────────────────
export interface RoleDto {
  id: string;
  key: string;
  displayName: string;
  description: string | null;
  isSiteAdminRole: boolean;
}

// ── Audit Log ──────────────────────────────────────────
export interface AuditLogDto {
  id: number;
  timestamp: string;
  siteId: string | null;
  actorUserId: string | null;
  actorEmail: string | null;
  actorIp: string | null;
  actorUserAgent: string | null;
  action: string;
  targetType: string | null;
  targetId: string | null;
  outcome: "success" | "failure";
  errorMessage: string | null;
}

export interface AuditLogQuery {
  from?: string;
  to?: string;
  action?: string;
  actorUserId?: string;
  outcome?: string;
  skip?: number;
  take?: number;
}

// ── Legacy PageQuery (kept for AppGrid compatibility) ──
export interface PageQuery {
  page?: number;
  pageSize?: number;
  search?: string;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
