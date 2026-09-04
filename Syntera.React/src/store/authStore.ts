/**
 * Auth store (Zustand + persist) — holds tokens, profile, and theme bundle.
 *
 * SECURITY (H7): refresh token is no longer stored client-side. The backend
 * sets it as an httpOnly cookie (syntera_refresh) that JavaScript cannot
 * read — XSS cannot exfiltrate it. The cookie is auto-sent on /api/auth/*
 * requests via `withCredentials: true` on axios.
 *
 * Access token stays in localStorage for now (TTL-bounded to 15 min — XSS
 * blast radius is small and CSP blocks external script injection). A full
 * migration to silent-refresh-on-app-start would remove the access token
 * from localStorage too, at the cost of an extra roundtrip on every page
 * load. That's a Sprint 3 candidate if you want stricter defense.
 *
 * Theme application:
 *   After login, the theme bundle (light + dark palettes) is applied to
 *   CSS variables by the ThemeProvider component. User's preferred mode
 *   (light/dark) is stored separately in themeStore.ts so the user can
 *   override the site default.
 */

import { create } from "zustand";
import { persist, createJSONStorage } from "zustand/middleware";
import type { LoginResponse, UserProfile, ThemeBundle } from "../types";

interface AuthState {
  accessToken: string | null;
  /**
   * SECURITY (H7): kept for backward compat with old refresh logic, but
   * no longer populated from login responses and NOT persisted to
   * localStorage. The backend's httpOnly cookie is the source of truth.
   */
  refreshToken: string | null;
  expiresAt: string | null;
  profile: UserProfile | null;
  theme: ThemeBundle | null;

  login: (payload: LoginResponse) => void;
  setTokens: (payload: {
    accessToken: string;
    expiresAt: string;
  }) => void;
  updateProfile: (profile: UserProfile) => void;
  updateTheme: (theme: ThemeBundle) => void;
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

      login(payload) {
        set({
          accessToken: payload.accessToken,
          // H7: don't store refreshToken in JS — backend manages it via
          // httpOnly cookie. Set to null so any legacy code reading this
          // field doesn't throw, but the value is always null.
          refreshToken: null,
          expiresAt: payload.expiresAt,
          profile: payload.profile,
          theme: payload.theme,
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

      logout() {
        set({
          accessToken: null,
          refreshToken: null,
          expiresAt: null,
          profile: null,
          theme: null,
        });
      },

      clear() {
        set({
          accessToken: null,
          refreshToken: null,
          expiresAt: null,
          profile: null,
          theme: null,
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
      partialize: (state) => ({
        // H7: only persist accessToken, expiresAt, profile, theme.
        // refreshToken is intentionally NOT persisted — backend cookie
        // is the source of truth and JS never needs to read it.
        accessToken: state.accessToken,
        expiresAt: state.expiresAt,
        profile: state.profile,
        theme: state.theme,
      }),
    },
  ),
);
