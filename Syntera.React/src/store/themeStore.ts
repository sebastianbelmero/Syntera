import { create } from "zustand";
import { persist, createJSONStorage } from "zustand/middleware";

/**
 * Theme store — manages BOTH the brand palette (one of 6) AND the
 * light/dark mode independently.
 *
 * Six brand palettes are derived from the Syntera brand study:
 *   - kalbe      (Kalbe Farma — crimson red)
 *   - dankos     (Dankos Farma — royal blue)
 *   - hexpharm   (Hexpharm Jaya — teal)
 *   - fima       (Fima — violet)
 *   - gof        (GOF — amber)
 *   - kalventis  (Kalventis — emerald, the canonical Syntera palette)
 *
 * Each palette defines its own `:root[data-theme="<name>"]` block in
 * index.css, plus a matching `.dark[data-theme="<name>"]` override.
 * AdminLayout writes the two attributes to <html> on every state
 * change so the CSS variables flip atomically.
 *
 * Persisted to localStorage — UI preference, not sensitive.
 */

export type ThemeBrand =
  | "kalbe"
  | "dankos"
  | "hexpharm"
  | "fima"
  | "gof"
  | "kalventis";

export const THEME_BRANDS: ThemeBrand[] = [
  "kalbe",
  "dankos",
  "hexpharm",
  "fima",
  "gof",
  "kalventis",
];

export const THEME_LABELS: Record<ThemeBrand, string> = {
  kalbe: "Kalbe",
  dankos: "Dankos",
  hexpharm: "Hexpharm",
  fima: "Fima",
  gof: "GOF",
  kalventis: "Kalventis",
};

/** Hex used for the swatch chip in the picker. */
export const THEME_SWATCH: Record<ThemeBrand, string> = {
  kalbe: "#E2231A",
  dankos: "#0054A6",
  hexpharm: "#00796B",
  fima: "#6B46C1",
  gof: "#C2410C",
  kalventis: "#007A4D",
};

export interface ThemeState {
  brand: ThemeBrand;
  isDark: boolean;
  setBrand: (brand: ThemeBrand) => void;
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

function resolveInitialBrand(): ThemeBrand {
  if (typeof window === "undefined") return "kalventis";
  const stored = localStorage.getItem("syntera.theme");
  if (stored) {
    try {
      const parsed = JSON.parse(stored) as { state?: { brand?: ThemeBrand } };
      const b = parsed.state?.brand;
      if (b && THEME_BRANDS.includes(b)) return b;
    } catch {
      // Fall through to default below.
    }
  }
  return "kalventis";
}

export const useThemeStore = create<ThemeState>()(
  persist(
    (set) => ({
      brand: resolveInitialBrand(),
      isDark: resolveInitialDark(),
      setBrand: (brand) => set({ brand }),
      toggleMode: () => set((state) => ({ isDark: !state.isDark })),
      setMode: (isDark) => set({ isDark }),
    }),
    {
      name: "syntera.theme",
      storage: createJSONStorage(() => localStorage),
      partialize: (state) => ({
        brand: state.brand,
        isDark: state.isDark,
      }),
    },
  ),
);
