/**
 * Auth store (Zustand + persist) — holds tokens, profile, and theme bundle.
 *
 * Security trade-off (acknowledged):
 *   Storing access/refresh tokens in localStorage is more XSS-exposed than
 *   httpOnly cookies, but for an internal B2B pharmaceutical suite
 *   (no third-party scripts, CSP enforced, no user-generated HTML) the
 *   convenience of not bouncing users to /login on every F5 wins.
 *   The refresh-token rotation on the backend limits the blast radius
 *   of a stolen access token (short-lived, 15 min).
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
  refreshToken: string | null;
  expiresAt: string | null;
  profile: UserProfile | null;
  theme: ThemeBundle | null;

  login: (payload: LoginResponse) => void;
  setTokens: (payload: {
    accessToken: string;
    refreshToken: string;
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
          refreshToken: payload.refreshToken,
          expiresAt: payload.expiresAt,
          profile: payload.profile,
          theme: payload.theme,
        });
      },

      setTokens(payload) {
        set((state) => ({
          accessToken: payload.accessToken,
          refreshToken: payload.refreshToken ?? state.refreshToken,
          expiresAt: payload.expiresAt,
        }));
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
        accessToken: state.accessToken,
        refreshToken: state.refreshToken,
        expiresAt: state.expiresAt,
        profile: state.profile,
        theme: state.theme,
      }),
    },
  ),
);
