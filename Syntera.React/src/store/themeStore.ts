import { create } from "zustand";
import { persist, createJSONStorage } from "zustand/middleware";

/**
 * Theme store — manages BOTH the brand palette (one of 7) AND the
 * light/dark mode independently.
 *
 * Seven brand palettes:
 *   - syntera    (Syntera canonical — navy + teal + green accent,
 *                 matching the official logo and brand guidelines)
 *   - kalbe      (Kalbe Farma — crimson red)
 *   - dankos     (Dankos Farma — royal blue)
 *   - hexpharm   (Hexpharm Jaya — teal)
 *   - fima       (Fima — violet)
 *   - gof        (GOF — amber)
 *   - kalventis  (Kalventis — emerald green, the original placeholder
 *                 palette before the Syntera brand identity was
 *                 finalized; kept as a selectable option)
 *
 * The `syntera` palette is the default and reflects the official
 * Syntera brand identity per the logo description document.
 *
 * Each palette defines its own `:root[data-theme="<name>"]` block in
 * index.css, plus a matching `.dark[data-theme="<name>"]` override.
 * <ThemeApplier /> writes the two attributes to <html> on every
 * state change so the CSS variables flip atomically.
 *
 * Persisted to localStorage — UI preference, not sensitive.
 */

export type ThemeBrand =
  | "syntera"
  | "kalbe"
  | "dankos"
  | "hexpharm"
  | "fima"
  | "gof"
  | "kalventis";

export const THEME_BRANDS: ThemeBrand[] = [
  "syntera",
  "kalbe",
  "dankos",
  "hexpharm",
  "fima",
  "gof",
  "kalventis",
];

export const THEME_LABELS: Record<ThemeBrand, string> = {
  syntera: "Syntera",
  kalbe: "Kalbe",
  dankos: "Dankos",
  hexpharm: "Hexpharm",
  fima: "Fima",
  gof: "GOF",
  kalventis: "Kalventis",
};

/**
 * Hex used for the swatch chip in the picker.
 * The `syntera` swatch matches the navy blue of the upper half of
 * the Syntera "S" icon per the official logo description.
 * The `kalventis` swatch is the original emerald placeholder.
 */
export const THEME_SWATCH: Record<ThemeBrand, string> = {
  syntera: "#0B3D6F",
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
  if (typeof window === "undefined") return "syntera";
  const stored = localStorage.getItem("syntera.theme");
  if (stored) {
    try {
      const parsed = JSON.parse(stored) as { state?: { brand?: ThemeBrand } };
      const b = parsed.state?.brand;
      // Old persisted values may say "kalventis" — silently fall back
      // to the new default "syntera" rather than erroring out.
      if (b && THEME_BRANDS.includes(b)) return b;
    } catch {
      // Fall through to default below.
    }
  }
  return "syntera";
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
