/**
 * Auth API client — login, refresh, logout, profile.
 *
 * Login accepts any email; the backend routes by domain:
 *   @syntera.com      → Platform Admin (local bcrypt)
 *   @kalventis.com    → LDAP Kalventis
 *   @kalbe.co.id      → LDAP Kalbe
 *   ... (5 more sites)
 *
 * Refresh-token handling (H7):
 *   - Refresh token lives in httpOnly cookie set by backend; JS never
 *     reads or sends it in a request body.
 *   - Platform admin: POST /api/auth/refresh        (no body needed)
 *   - Site user:      POST /api/auth/refresh-site   { siteId }
 *   - The frontend determines which endpoint to call based on profile.scope.
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
  // H7: refresh token is sent automatically by the browser via the
  // httpOnly cookie on /api/auth/logout. We don't need (and don't have)
  // the token in JS. Send an empty body — backend reads the cookie.
  try {
    await post("/auth/logout", {});
  } finally {
    useAuthStore.getState().logout();
  }
}

export async function refresh(): Promise<RefreshResponse> {
  const { profile } = useAuthStore.getState();

  // Choose endpoint based on scope.
  const url = profile?.scope === "site" && profile.siteId
    ? "/auth/refresh-site"
    : "/auth/refresh";

  // H7: refresh token comes from the httpOnly cookie automatically
  // (withCredentials=true on axios). Only siteId is sent in the body.
  const body = profile?.scope === "site" && profile.siteId
    ? { siteId: profile.siteId }
    : {};

  const data = await post<RefreshResponse>(url, body);
  // Update tokens, profile, AND theme (server may return updated theme
  // in refresh response — important for site users whose theme comes
  // from their site's SiteTheme record).
  useAuthStore.getState().setTokens({
    accessToken: data.accessToken,
    expiresAt: data.expiresAt,
  });
  if (data.profile) {
    useAuthStore.getState().updateProfile(data.profile);
  }
  if (data.theme) {
    useAuthStore.getState().updateTheme(data.theme);
  }
  return data;
}

export async function getProfile(): Promise<UserProfile> {
  return await get<UserProfile>("/auth/profile");
}
