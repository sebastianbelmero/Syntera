import { create } from "zustand";

/**
 * Theme store — manages light/dark mode.
 *
 * Persists the user's preference to localStorage (which is fine for a
 * non-sensitive UI flag — unlike auth tokens, this does not need to be
 * XSS-safe) and respects the OS color-scheme preference on first visit.
 */
export interface ThemeState {
  isDark: boolean;
  toggleTheme: () => void;
}

export const useThemeStore = create<ThemeState>((set) => ({
  isDark: (() => {
    if (typeof window === "undefined") return false;
    const stored = localStorage.getItem("theme");
    if (stored) return stored === "dark";
    return window.matchMedia("(prefers-color-scheme: dark)").matches;
  })(),
  toggleTheme: () =>
    set((state) => {
      const newIsDark = !state.isDark;
      localStorage.setItem("theme", newIsDark ? "dark" : "light");
      return { isDark: newIsDark };
    }),
}));
