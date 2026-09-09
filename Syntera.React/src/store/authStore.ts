/**
 * Auth store (Zustand) — holds tokens, profile, and theme bundle.
 *
 * SECURITY (H7 full — cookie-only transport, ACTIVATED):
 *   Neither access token NOR refresh token is persisted to localStorage.
 *   - Refresh token: httpOnly cookie set by backend (syntera_refresh).
 *     JS cannot read it; XSS cannot exfiltrate it. The backend ALSO blanks
 *     the RefreshToken field in every JSON response (Auth:CookieOnlyRefreshToken
 *     = true) so an XSS reading fetch/XHR response bodies sees an empty
 *     string. The former localStorage/body fallback (Vite dev proxy) was
 *     removed — the cookie round-trips correctly through the proxy since
 *     the Sprint 2.5 fix (cookie Domain defaults to the Host header).
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
          // COOKIE-ONLY (H7): always null on the client — the refresh token
          // travels exclusively in the httpOnly `syntera_refresh` cookie set
          // by the backend. In strict mode the backend also blanks it in the
          // JSON body, so `payload.refreshToken` is "" anyway; we null it here
          // so no code path can ever resurrect the body transport.
          refreshToken: null,
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
      // COOKIE-ONLY (H7 hardening): persist ONLY the theme bundle. The
      // refresh token is NO LONGER persisted — the former localStorage
      // fallback existed for a Vite dev proxy cookie bug that was fixed in
      // Sprint 2.5 (cookie Domain now defaults to the Host header, so the
      // browser stores + replays it through the proxy correctly). With the
      // token out of localStorage, an XSS cannot exfiltrate it even across
      // page reloads; the httpOnly cookie is the single transport.
      //
      // accessToken + profile + expiresAt + refreshToken stay in-memory
      // only — access material is re-acquired via /api/auth/refresh from
      // the cookie on every page load (see initAuth in api/auth.ts).
      //
      // NOTE: users with an old `syntera.auth` localStorage entry (from the
      // dual-transport era) will have the stale refreshToken key ignored —
      // it's simply never read anymore.
      partialize: (state) => ({
        theme: state.theme,
      }),
    },
  ),
);
