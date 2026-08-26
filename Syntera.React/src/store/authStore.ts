/**
 * Auth store (Zustand) — holds tokens + profile in memory only.
 * Persisting to localStorage is intentionally omitted to avoid
 * XSS-based token theft from any compromised third-party script.
 * The trade-off: users re-login on every page reload, but we
 * mitigate that by issuing long-lived refresh tokens (30 days dev,
 * 7 days prod) on the backend.
 *
 * The store exposes:
 *   - state: accessToken, refreshToken, expiresAt, profile
 *   - actions: login(payload), setTokens(payload), logout()
 *   - selectors: isAuthenticated, hasRole(role)
 */

import { create } from "zustand";
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
  isAuthenticated: () => boolean;
  hasRole: (role: string) => boolean;
}

export const useAuthStore = create<AuthState>((set, get) => ({
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

  isAuthenticated() {
    return !!get().accessToken && !!get().profile;
  },

  hasRole(role: string) {
    const p = get().profile;
    return !!p && p.roles.includes(role);
  },
}));
