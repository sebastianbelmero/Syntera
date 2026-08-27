/**
 * Theme store — manages light/dark mode preference only.
 *
 * The brand palette is no longer fixed per-UI-choice — it now comes from
 * the authenticated user's site (loaded by AuthService into authStore.theme).
 * The ThemeProvider component reads authStore.theme.light + .dark and
 * applies them to CSS variables on the <html> element.
 *
 * The user can still toggle between light/dark — this preference is
 * persisted in localStorage so it survives reloads.
 */

import { create } from "zustand";
import { persist, createJSONStorage } from "zustand/middleware";

export interface ThemeState {
  isDark: boolean;
  toggleMode: () => void;
  setMode: (dark: boolean) => void;
}

function resolveInitialDark(): boolean {
  if (typeof window === "undefined") return false;
  const stored = localStorage.getItem("syntera.theme");
  if (stored) {
    try {
      const parsed = JSON.parse(stored) as { state?: { isDark?: boolean } };
      if (typeof parsed.state?.isDark === "boolean") {
        return parsed.state.isDark;
      }
    } catch {
      // Fall through to OS preference below.
    }
  }
  return window.matchMedia("(prefers-color-scheme: dark)").matches;
}

export const useThemeStore = create<ThemeState>()(
  persist(
    (set) => ({
      isDark: resolveInitialDark(),
      toggleMode: () => set((state) => ({ isDark: !state.isDark })),
      setMode: (isDark) => set({ isDark }),
    }),
    {
      name: "syntera.theme",
      storage: createJSONStorage(() => localStorage),
      partialize: (state) => ({ isDark: state.isDark }),
    },
  ),
);
