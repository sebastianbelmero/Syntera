/**
 * Auth store (Zustand + persist) — holds tokens + profile in memory
 * AND mirrors them to localStorage so a page reload doesn't log
 * the user out.
 *
 * Security trade-off:
 *   Storing access tokens in localStorage is more XSS-exposed than
 *   httpOnly cookies, but for an internal B2B pharmaceutical suite
 *   (no third-party scripts, CSP enforced, no user-generated HTML)
 *   the convenience of not bouncing users to /login on every F5
 *   wins. The refresh-token rotation on the backend limits the
 *   blast radius of a stolen access token (short-lived).
 *
 * The store exposes:
 *   - state: accessToken, refreshToken, expiresAt, profile
 *   - actions: login(payload), setTokens(payload), logout(), clear()
 *   - selectors: isAuthenticated(), hasRole(role)
 *
 * `persist` only serialises the four data fields — actions stay
 * on the prototype and never touch storage.
 */

import { create } from "zustand";
import { persist, createJSONStorage } from "zustand/middleware";
import type { LoginResponse, UserProfile } from "../types";

interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  expiresAt: string | null;
  profile: UserProfile | null;
  login: (payload: LoginResponse) => void;
  setTokens: (payload: {
    accessToken: string;
    refreshToken: string;
    expiresAt: string;
  }) => void;
  logout: () => void;
  /** Hard-clear storage (used by 401-refresh failure path). */
  clear: () => void;
  isAuthenticated: () => boolean;
  hasRole: (role: string) => boolean;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set, get) => ({
      accessToken: null,
      refreshToken: null,
      expiresAt: null,
      profile: null,

      login(payload) {
        set({
          accessToken: payload.accessToken,
          refreshToken: payload.refreshToken,
          expiresAt: payload.expiresAt,
          profile: payload.profile,
        });
      },

      setTokens(payload) {
        set((state) => ({
          accessToken: payload.accessToken,
          refreshToken: payload.refreshToken ?? state.refreshToken,
          expiresAt: payload.expiresAt,
        }));
      },

      logout() {
        set({
          accessToken: null,
          refreshToken: null,
          expiresAt: null,
          profile: null,
        });
      },

      // Alias kept for clarity at call sites that mean "storage wipe"
      // (e.g. after a failed token refresh).
      clear() {
        set({
          accessToken: null,
          refreshToken: null,
          expiresAt: null,
          profile: null,
        });
      },

      isAuthenticated() {
        return !!get().accessToken && !!get().profile;
      },

      hasRole(role: string) {
        const p = get().profile;
        return !!p && p.roles.includes(role);
      },
    }),
    {
      name: "syntera.auth",
      storage: createJSONStorage(() => localStorage),
      // Only persist data fields, never the action functions.
      partialize: (state) => ({
        accessToken: state.accessToken,
        refreshToken: state.refreshToken,
        expiresAt: state.expiresAt,
        profile: state.profile,
      }),
    },
  ),
);
