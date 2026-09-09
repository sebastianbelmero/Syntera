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
 *
 * F5-LOGOUT FIX (2026-09): initAuth, the 401 interceptor, and refresh()
 * all funnel through refreshCoordinator.silentRefresh() — one single-flight
 * refresh per tab, serialized across tabs via Web Locks. Overlapping
 * refreshes presenting the same cookie trigger the server's family-wide
 * reuse detection and log every tab out; the coordinator makes that race
 * structurally impossible.
 */

import { post, get } from "./client";
import { silentRefresh } from "./refreshCoordinator";
import type {
  LoginRequest,
  LoginResponse,
  LoginMfaRequest,
  MfaSetupResponse,
  ChangePasswordRequest,
  RefreshResponse,
  UserProfile,
} from "../types";
import { useAuthStore } from "../store/authStore";

export async function login(req: LoginRequest): Promise<LoginResponse> {
  const data = await post<LoginResponse>("/auth/login", req);
  // Sprint 3.2: only commit tokens to the store when the response is a
  // full, usable login. If the backend signals `requiresMfa` or
  // `requiresPasswordChange`, the response carries a single-use
  // challenge token instead of an access token — the caller (LoginPage)
  // must drive the next step before we mark the user "authenticated".
  if (!data.requiresMfa && !data.requiresPasswordChange) {
    useAuthStore.getState().login(data);
  }
  return data;
}

/**
 * Sprint 3.2 — complete login after an MFA challenge.
 *
 * Call this with the `mfaChallengeToken` returned from `/auth/login`
 * (when `requiresMfa === true`) plus the 6-digit TOTP code from the
 * user's authenticator app. On success the backend returns the full
 * `LoginResponse` (with a real access token this time) and we commit
 * it to the auth store — same path as a normal `login()` success.
 */
export async function loginMfa(req: LoginMfaRequest): Promise<LoginResponse> {
  const data = await post<LoginResponse>("/auth/login-mfa", req);
  useAuthStore.getState().login(data);
  return data;
}

/**
 * Sprint 3.2 — start MFA enrollment.
 *
 * Requires an active session (Bearer token). The backend generates a
 * new TOTP secret, stores it in `UserMfaSecret` (status: Pending) and
 * returns both an otpauth:// URL (`qrCodeUrl`) and the `plaintextSecret`
 * for manual entry. The secret is NOT yet active until `confirmMfa`
 * succeeds with a valid 6-digit code.
 *
 * SECURITY: the plaintext secret must never be persisted client-side
 * (no localStorage, no console log, no error reporting). The SettingsPage
 * keeps it in component state only for the duration of the setup flow
 * and clears it on unmount / cancel.
 */
export async function setupMfa(): Promise<MfaSetupResponse> {
  return await post<MfaSetupResponse>("/auth/mfa/setup", {});
}

/**
 * Sprint 3.2 — confirm MFA enrollment.
 *
 * After `setupMfa`, the user enters a 6-digit code from their
 * authenticator app. The backend verifies it against the pending
 * secret and, on success, flips `UserMfaSecret.Status` to `Active`.
 * On failure the pending secret stays and the user can retry (or the
 * setup token times out, at which point a fresh `setupMfa` call is
 * needed).
 */
export async function confirmMfa(code: string): Promise<void> {
  await post("/auth/mfa/confirm", { code });
}

/**
 * Sprint 3.2 — disable MFA.
 *
 * Requires the user's current 6-digit TOTP code as a proof-of-possession
 * check (so an attacker who briefly borrows an unlocked browser can't
 * silently strip MFA protection). On success the backend marks the
 * secret as Disabled.
 */
export async function disableMfa(code: string): Promise<void> {
  await post("/auth/mfa/disable", { code });
}

/**
 * Sprint 3.2 — change password.
 *
 * Two call shapes:
 *   - Authenticated change (SettingsPage): send `currentPassword` +
 *     `newPassword`. The Bearer token authorises the request.
 *   - Forced change on login (LoginPage, after `requiresPasswordChange`
 *     === true): send `passwordChangeChallengeToken` + `newPassword`.
 *     No Bearer token is available — the challenge token substitutes
 *     for it.
 *
 * The new password MUST satisfy the backend's PasswordPolicy
 * (12–256 chars, upper, lower, digit, symbol). Client-side validation
 * runs first to avoid a roundtrip; the backend re-validates.
 */
export async function changePassword(req: ChangePasswordRequest): Promise<void> {
  await post("/auth/change-password", req);
}

export async function logout(): Promise<void> {
  // COOKIE-ONLY (H7): the refresh token is sent automatically by the browser
  // via the httpOnly `syntera_refresh` cookie on /api/auth/logout — no token
  // in the body. The backend revokes whatever the cookie carries; if the
  // cookie is already gone, logout is a server-side no-op (the client clears
  // its in-memory state either way).
  try {
    await post("/auth/logout", {});
  } finally {
    useAuthStore.getState().logout();
  }
}

export async function refresh(): Promise<RefreshResponse> {
  // F5-LOGOUT FIX: run through the coordinator (single-flight + Web Locks)
  // so this can never race the boot refresh or the 401 interceptor's
  // refresh — a race would trigger server-side family revocation.
  // The coordinator also commits tokens/profile/theme to the store.
  return await silentRefresh();
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
    // COOKIE-ONLY (H7): the httpOnly `syntera_refresh` cookie round-trips
    // correctly through the Vite dev proxy (Sprint 2.5 fix — cookie Domain
    // defaults to the Host header), so the silent refresh relies purely on
    // the cookie. No body token, no localStorage fallback — the store's
    // `refreshToken` field is always null.
    //
    // F5-LOGOUT FIX: via the coordinator (raw axios + Web Locks), NOT the
    // `api` instance — the boot refresh must share the single-flight with
    // the 401 interceptor's refresh and never race it.
    const data = await silentRefresh();
    useAuthStore.getState().login({
      accessToken: data.accessToken,
      expiresAt: data.expiresAt,
      // COOKIE-ONLY: ignored by the store (login() nulls it) — the rotated
      // token is already in the cookie jar via the Set-Cookie header.
      refreshToken: null,
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
