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

/**
 * Sprint 3.2 — MFA + forced password-change support.
 *
 * The four new optional fields default to `false` / `null` on the backend,
 * so existing JSON clients (pre-MFA) continue to deserialize the response
 * without any client-side change. A forward-compatible client (this one)
 * reads them to drive a multi-step login flow:
 *
 *   1. `requiresMfa === true`         → step = "mfa"          (loginMfa)
 *   2. `requiresPasswordChange === true` → step = "passwordChange" (changePassword)
 *   3. otherwise                       → normal dashboard navigation
 *
 * IMPORTANT: when either `requiresMfa` or `requiresPasswordChange` is true,
 * the response DOES NOT contain a usable access token — `accessToken` is
 * empty/omitted, `profile` may be empty, and `useAuthStore.login(data)`
 * must NOT be called. The backend instead hands back a short-lived
 * single-use challenge token (`mfaChallengeToken` / `passwordChangeChallengeToken`)
 * that the client must replay with the corresponding `/api/auth/login-mfa`
 * or `/api/auth/change-password` call to obtain a real access token.
 */
export interface LoginResponse {
  accessToken: string;
  expiresAt: string;
  refreshToken: string;
  profile: UserProfile;
  theme: ThemeBundle;
  /** True when the user has MFA enabled and must supply a TOTP code. */
  requiresMfa?: boolean;
  /** Single-use token to send to /api/auth/login-mfa; null when MFA not required. */
  mfaChallengeToken?: string | null;
  /** True when the user's password has expired and must be rotated before login. */
  requiresPasswordChange?: boolean;
  /** Single-use token to send to /api/auth/change-password; null when not required. */
  passwordChangeChallengeToken?: string | null;
}

/** Body for POST /api/auth/login-mfa — completes login after MFA challenge. */
export interface LoginMfaRequest {
  mfaChallengeToken: string;
  code: string;
}

/** Response from POST /api/auth/mfa/setup — contains QR URL + plaintext secret. */
export interface MfaSetupResponse {
  qrCodeUrl: string;
  plaintextSecret: string;
}

/** Body for POST /api/auth/mfa/confirm — verifies TOTP + enables MFA. */
export interface ConfirmMfaRequest {
  code: string;
}

/** Body for POST /api/auth/mfa/disable — verifies TOTP + disables MFA. */
export interface DisableMfaRequest {
  code: string;
}

/**
 * DEV-ONLY response from GET /api/auth/dev-auth — reports the state of the
 * backend DevAuth login override (active ONLY in the Development env).
 * When active, whatever email is typed at the login screen, the LDAP
 * password check binds as `ldapEmail` instead — letting the developer log
 * in as any pre-provisioned user with one known password. The endpoint
 * returns 404 when the override is off, so any fetch error = "off".
 */
export interface DevAuthMode {
  ldapEmail: string;
}

/**
 * Body for POST /api/auth/change-password.
 *
 * Two paths:
 *   - Normal authenticated change (logged in user): send `currentPassword` only.
 *   - Forced change on login (challenge token path): send
 *     `passwordChangeChallengeToken` instead of `currentPassword`.
 *
 * `newPassword` is always required and must satisfy PasswordPolicy
 * (12-256 chars, ≥1 upper, ≥1 lower, ≥1 digit, ≥1 symbol).
 */
export interface ChangePasswordRequest {
  currentPassword?: string;
  newPassword: string;
  passwordChangeChallengeToken?: string;
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

// ── Role Template Publish Approvals (Sprint 2.7 — Two-Person Rule) ────
// When TwoPerson:Enabled=true on the backend, POST /role-templates/{id}/publish
// creates a pending approval instead of publishing immediately. A second
// Platform Admin must call /approve (or /reject) to complete the workflow.
// Per 21 CFR Part 11 §11.10(g) the requester cannot self-approve.

/**
 * Result of POST /api/platform/role-templates/{id}/publish.
 *
 * - `status === "published"` (or, for backward compat, no status field at
 *   all): the publish completed immediately (TwoPerson:Enabled=false, the
 *   default). `approvalId` is undefined.
 * - `status === "pending"`: a pending approval row was created. `approvalId`
 *   points to it; the frontend should redirect to the approval review page.
 */
export interface PublishResultDto {
  success?: boolean; // Present in the legacy shape; ignored by the frontend.
  status: "published" | "pending";
  approvalId?: string;
}

/**
 * Denormalized view of a RoleTemplateApproval row joined with the role
 * template (key + display name) and platform users (requester/approver
 * emails). `requestedSnapshotJson` is the template's state at request
 * time, so the reviewer can detect if the requester edited the template
 * between request and approval.
 */
export interface RoleTemplateApprovalDto {
  id: string;
  roleTemplateId: string;
  roleTemplateKey: string;
  roleTemplateDisplayName: string;
  requestedBy: string;
  requestedByEmail: string;
  requestedAt: string;
  requestedSnapshotJson: string;
  status: "pending" | "approved" | "rejected" | "superseded";
  actionBy: string | null;
  actionByEmail: string | null;
  actionAt: string | null;
  rejectionReason: string | null;
  requesterSignatureMeaning: string | null;
  approverSignatureMeaning: string | null;
}

/** Body for POST /api/platform/role-templates/{id}/approve. 21 CFR Part 11 §11.50. */
export interface ApprovePublishRequest {
  signatureMeaning: string;
}

/** Body for POST /api/platform/role-templates/{id}/reject. Self-rejection allowed. */
export interface RejectPublishRequest {
  reason: string;
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
  /** Previous state snapshot (for update events). NULL for create/disable events. */
  beforeJson: string | null;
  /** Post-state snapshot (or new entity representation). NULL for delete/disable events. */
  afterJson: string | null;
  /** 21 CFR Part 11 §11.50 signature meaning (e.g., "I approve this role grant"). NULL → "action performed". */
  signatureMeaning: string | null;
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
