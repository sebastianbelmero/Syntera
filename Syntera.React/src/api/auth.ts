/**
 * Auth API client — login, refresh, logout, profile.
 *
 * Login accepts any email; the backend routes by domain:
 *   @syntera.com      → Platform Admin (local bcrypt)
 *   @kalventis.com    → LDAP Kalventis
 *   @kalbe.co.id      → LDAP Kalbe
 *   ... (5 more sites)
 *
 * Refresh-token handling:
 *   - Platform admin: POST /api/auth/refresh
 *   - Site user:      POST /api/auth/refresh-site { refreshToken, siteId }
 *   The frontend determines which endpoint to call based on profile.scope.
 */

import { post, get } from "./client";
import type {
  LoginRequest,
  LoginResponse,
  RefreshResponse,
  UserProfile,
} from "../types";
import { useAuthStore } from "../store/authStore";

export async function login(req: LoginRequest): Promise<LoginResponse> {
  const data = await post<LoginResponse>("/auth/login", req);
  useAuthStore.getState().login(data);
  return data;
}

export async function logout(): Promise<void> {
  const refreshToken = useAuthStore.getState().refreshToken;
  try {
    if (refreshToken) {
      await post("/auth/logout", { refreshToken });
    }
  } finally {
    useAuthStore.getState().logout();
  }
}

export async function refresh(): Promise<RefreshResponse> {
  const { refreshToken, profile } = useAuthStore.getState();
  if (!refreshToken) throw new Error("NO_REFRESH_TOKEN");

  // Choose endpoint based on scope.
  const url = profile?.scope === "site" && profile.siteId
    ? "/auth/refresh-site"
    : "/auth/refresh";

  const body = profile?.scope === "site" && profile.siteId
    ? { refreshToken, siteId: profile.siteId }
    : { refreshToken };

  const data = await post<RefreshResponse>(url, body);
  // Use setTokens — preserve profile/theme from initial login (server may return updated
  // profile in refresh response, but we keep the original to avoid mismatched state).
  useAuthStore.getState().setTokens({
    accessToken: data.accessToken,
    refreshToken: data.refreshToken,
    expiresAt: data.expiresAt,
  });
  if (data.profile) {
    useAuthStore.getState().updateProfile(data.profile);
  }
  return data;
}

export async function getProfile(): Promise<UserProfile> {
  return await get<UserProfile>("/auth/profile");
}
