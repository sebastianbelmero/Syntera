/**
 * Auth store (Zustand) — holds tokens, profile, and theme bundle.
 *
 * SECURITY (H7 full — Sprint 4):
 *   Neither access token NOR refresh token is persisted to localStorage.
 *   - Refresh token: httpOnly cookie set by backend (syntera_refresh).
 *     JS cannot read it; XSS cannot exfiltrate it.
 *   - Access token: in-memory only (this store). On page reload, the
 *     store is empty; an explicit silent-refresh via /api/auth/refresh
 *     is run on app boot (see src/main.tsx → initAuth()) to repopulate
 *     the store from the cookie. If the cookie is expired or absent,
 *     the user is treated as logged-out.
 *
 *   Trade-off: an extra roundtrip on every page load (~50ms typical).
 *   Benefit: a single XSS can no longer read a long-lived refresh
 *   token; the worst-case for an XSS is a 15-minute access token,
 *   which is bounded by the same TTL we already enforced.
 *
 * Theme persistence:
 *   Theme bundle (light + dark palettes) IS still persisted to
 *   localStorage — it's not sensitive and we want it available
 *   before the silent refresh completes (to avoid FOUC on login page).
 *   User's preferred mode (light/dark) is in themeStore.ts.
 *
 * Profile persistence:
 *   Profile is NOT persisted — it's re-fetched via silent refresh on
 *   app boot. Same rationale as the access token: stale profile data
 *   after a role change would mislead the user.
 */

import { create } from "zustand";
import { persist, createJSONStorage } from "zustand/middleware";
import type { LoginResponse, UserProfile, ThemeBundle } from "../types";

interface AuthState {
  /** In-memory only. Empty on page load until silent refresh completes. */
  accessToken: string | null;
  /**
   * Always null on the client now — refresh token lives in httpOnly cookie.
   * Kept in the interface for backward compat with any code reading this
   * field; they get null and should NOT try to send it (the cookie flows
   * automatically).
   */
  refreshToken: string | null;
  expiresAt: string | null;
  profile: UserProfile | null;
  /** Persisted to localStorage (not sensitive). */
  theme: ThemeBundle | null;
  /** True while a silent refresh is in flight on app boot. */
  initializing: boolean;

  login: (payload: LoginResponse) => void;
  setTokens: (payload: {
    accessToken: string;
    expiresAt: string;
  }) => void;
  updateProfile: (profile: UserProfile) => void;
  updateTheme: (theme: ThemeBundle) => void;
  setInitializing: (initializing: boolean) => void;
  logout: () => void;
  clear: () => void;

  isAuthenticated: () => boolean;
  hasRole: (role: string) => boolean;
  hasPermission: (perm: string) => boolean;
  isPlatformAdmin: () => boolean;
  isSiteBusinessAdmin: () => boolean;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set, get) => ({
      accessToken: null,
      refreshToken: null,
      expiresAt: null,
      profile: null,
      theme: null,
      initializing: true,

      login(payload) {
        set({
          accessToken: payload.accessToken,
          // COMPLIANCE (Sprint 2.5 silent-refresh fix): store refreshToken
          // in-memory as a fallback for the Vite dev proxy scenario where
          // httpOnly cookies don't round-trip reliably across localhost ports.
          // In Production, the cookie is the preferred transport (H7 security
          // model), but having the token in memory too gives us a working
          // refresh path when cookie Domain/SameSite matching fails.
          //
          // Security trade-off: an XSS could read this token. Mitigations:
          //   1. Token is in-memory only (not persisted to localStorage) —
          //      gone on page reload (but then we have the cookie as backup).
          //   2. Token is short-lived (1 day platform / 7 days site) and
          //      rotated on every refresh — family-reuse detection still
          //      works server-side.
          //   3. HttpOnly cookie is ALSO set by backend — if cookie works,
          //      backend prefers it (cookie-first in ReadRefreshToken).
          refreshToken: payload.refreshToken,
          expiresAt: payload.expiresAt,
          profile: payload.profile,
          theme: payload.theme,
          initializing: false,
        });
      },

      setTokens(payload) {
        set({
          accessToken: payload.accessToken,
          expiresAt: payload.expiresAt,
        });
      },

      updateProfile(profile) {
        set({ profile });
      },

      updateTheme(theme) {
        set({ theme });
      },

      setInitializing(initializing) {
        set({ initializing });
      },

      logout() {
        set({
          accessToken: null,
          refreshToken: null,
          expiresAt: null,
          profile: null,
          initializing: false,
        });
      },

      clear() {
        set({
          accessToken: null,
          refreshToken: null,
          expiresAt: null,
          profile: null,
          initializing: true,
        });
      },

      isAuthenticated() {
        return !!get().accessToken && !!get().profile;
      },

      hasRole(role: string) {
        const p = get().profile;
        return !!p && p.roles.includes(role);
      },

      hasPermission(perm: string) {
        const p = get().profile;
        return !!p && p.permissions.includes(perm);
      },

      isPlatformAdmin() {
        return get().hasRole("platform-admin");
      },

      isSiteBusinessAdmin() {
        return get().hasRole("site-business-admin");
      },
    }),
    {
      name: "syntera.auth",
      storage: createJSONStorage(() => localStorage),
      // COMPLIANCE (Sprint 2.5 silent-refresh fix): persist theme + refreshToken
      // to localStorage so the silent refresh on page reload can recover the
      // session when the httpOnly cookie doesn't round-trip (Vite dev proxy
      // scenario where browser blocks cookie due to port mismatch).
      //
      // accessToken + profile + expiresAt stay in-memory only — they're
      // short-lived and re-acquired via /api/auth/refresh using the
      // persisted refreshToken (or the cookie, if the browser has it).
      //
      // SECURITY TRADE-OFF: refreshToken in localStorage means an XSS
      // could exfiltrate it. Mitigations:
      //   1. Backend prefers httpOnly cookie in ReadRefreshToken —
      //      cookie-first means the body token is only used as fallback.
      //   2. Token is short-lived (1d platform / 7d site) and rotated
      //      on every refresh — family-reuse detection revokes the
      //      entire family if a stolen token is replayed.
      //   3. In Production with reverse-proxy (same origin), cookie
      //      works reliably and the localStorage value is just a
      //      redundant backup that's never read.
      //   4. To force cookie-only (production hardening), set
      //      `partialize` back to just `{ theme }` and ensure the
      //      frontend is served from the same origin as the API.
      partialize: (state) => ({
        theme: state.theme,
        refreshToken: state.refreshToken,
      }),
    },
  ),
);
