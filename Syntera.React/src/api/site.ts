/**
 * Site Admin API — user management, role assignment, permission grants.
 * All endpoints require site-business-admin JWT claim and route to the user's site.
 *
 * Note: LDAP sync is NOT available (no service account). Business Admin must
 * create user rows manually before users can log in.
 */

import { get, post, put } from "./client";
import type {
  UserDto,
  UserUpsertDto,
  AssignRoleDto,
  RevokeRoleDto,
  GrantDirectPermissionDto,
  RevokeDirectPermissionDto,
  RoleDto,
} from "../types";

const BASE = "/site";

export const usersApi = {
  list: () => get<UserDto[]>(`${BASE}/users`),
  get: (id: string) => get<UserDto>(`${BASE}/users/${id}`),
  create: (dto: UserUpsertDto) => post<UserDto>(`${BASE}/users`, dto),
  update: (id: string, dto: UserUpsertDto) => put<UserDto>(`${BASE}/users/${id}`, dto),
  disable: (id: string) => post<{ success: boolean }>(`${BASE}/users/${id}/disable`),

  listRoles: () => get<RoleDto[]>(`${BASE}/users/roles`),

  assignRole: (dto: AssignRoleDto) => post<UserDto>(`${BASE}/users/assign-role`, dto),
  revokeRole: (dto: RevokeRoleDto) => post<{ success: boolean }>(`${BASE}/users/revoke-role`, dto),

  grantPermission: (dto: GrantDirectPermissionDto) =>
    post<UserDto>(`${BASE}/users/grant-permission`, dto),
  revokePermission: (dto: RevokeDirectPermissionDto) =>
    post<{ success: boolean }>(`${BASE}/users/revoke-permission`, dto),
};

// Helper: extract roles from a user's role assignments (for dropdown source).
export function extractRoles(users: UserDto[]): RoleDto[] {
  const seen = new Map<string, RoleDto>();
  for (const u of users) {
    for (const r of u.roles) {
      if (!seen.has(r.roleId)) {
        seen.set(r.roleId, {
          id: r.roleId,
          key: r.roleKey,
          displayName: r.roleDisplayName,
          description: null,
          isSiteAdminRole: r.roleKey === "site-business-admin",
        });
      }
    }
  }
  return Array.from(seen.values());
}
