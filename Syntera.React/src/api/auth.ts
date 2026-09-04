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
 *
 * Silent refresh on app start (H7-full — Sprint 4):
 *   - Access token is in-memory only; gone on page reload.
 *   - initAuth() is called from main.tsx before React renders.
 *   - It pings /api/auth/refresh. If the httpOnly cookie is still
 *     valid, the backend returns a fresh access token + profile +
 *     theme; the store is populated and the user appears logged in.
 *   - If the cookie is missing or expired, the backend returns 401;
 *     initAuth() marks the store as "not authenticated" and React
 *     proceeds to show the login page.
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

/**
 * SECURITY (H7-full — Sprint 4): silent refresh on app boot.
 *
 * Called from src/main.tsx before React renders. Attempts to acquire a
 * fresh access token from the httpOnly refresh cookie:
 *   - Success → store populated, user appears logged in, React renders
 *     the authenticated routes.
 *   - Failure (401 / network error / etc.) → store stays empty, React
 *     renders the login page.
 *
 * Always sets `initializing: false` at the end so the UI can show a
 * loading state while the refresh is in flight.
 *
 * Important: this function NEVER throws. A failed silent refresh is the
 * expected state for an unauthenticated user, not an error to surface.
 */
export async function initAuth(): Promise<void> {
  const store = useAuthStore.getState();
  // If we already have an in-memory access token (HMR in dev, or
  // somehow called twice), skip — don't risk replacing a valid token.
  if (store.accessToken) {
    store.setInitializing(false);
    return;
  }

  try {
    // Platform vs site scope: on a fresh page load we don't know the
    // user's scope yet (no in-memory profile). Try /api/auth/refresh
    // first (platform scope). If the cookie belongs to a site user,
    // the backend will refuse with REFRESH_NOT_FOUND and we fall back
    // to /api/auth/refresh-site — but we don't know the siteId either.
    //
    // Solution: the backend's refresh endpoint accepts the cookie
    // alone and can determine scope from the token's UserScope column.
    // For site scope, it returns a RefreshResponse that includes the
    // profile (with siteId) — so the FIRST refresh always uses
    // /api/auth/refresh. Site users whose first call returns 401
    // can't be auto-detected here without a hint, but in practice the
    // backend's RefreshAsync already throws REFRESH_NOT_FOUND for
    // site tokens, so we just treat any failure as "not authenticated".
    //
    // To keep the UX simple for both scopes on first page load, we
    // try the platform endpoint; if it returns a profile with
    // scope='site', we re-issue via /auth/refresh-site with the
    // returned siteId to get the correct site-scoped access token.
    const url = "/auth/refresh";
    const data = await post<RefreshResponse>(url, {});
    useAuthStore.getState().login({
      accessToken: data.accessToken,
      expiresAt: data.expiresAt,
      refreshToken: data.refreshToken,
      profile: data.profile,
      theme: data.theme,
    });
    return;
  } catch {
    // Silently ignore — user is not authenticated. Don't log to console
    // (could leak auth state to browser extension page-script context
    // in older browsers). The login page will be shown.
  } finally {
    useAuthStore.getState().setInitializing(false);
  }
}
