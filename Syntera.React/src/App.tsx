import { Routes, Route, Navigate } from "react-router-dom";
import { useEffect } from "react";
import { LayoutDashboard, Settings, Building2, Shield, Users, ScrollText, KeyRound } from "lucide-react";

import { RequireAuth, RequirePlatformAdmin, RequirePlatformOrSystemAdmin, RequireSiteAdmin } from "./routes/guards";
import { useAuthStore } from "./store/authStore";
import { useThemeStore } from "./store/themeStore";
import { AdminLayout, type MenuItem } from "./components/layout";

import LoginPage from "./pages/auth/LoginPage";
import DashboardPage from "./pages/dashboard/DashboardPage";
import SitesPage from "./pages/platform/SitesPage";
import RoleTemplatesPage from "./pages/platform/RoleTemplatesPage";
import UsersPage from "./pages/site/UsersPage";
import AuditLogsPage from "./pages/audit/AuditLogsPage";
import SettingsPage from "./pages/settings/SettingsPage";

/** Calculate readable foreground color (dark/light) based on background luminance. */
function pickFg(hex: string): string {
  const c = hex.replace("#", "");
  const r = parseInt(c.slice(0, 2), 16);
  const g = parseInt(c.slice(2, 4), 16);
  const b = parseInt(c.slice(4, 6), 16);
  const L = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return L > 0.55 ? "#0a1428" : "#ffffff";
}

/**
 * ThemeApplier — applies the brand palette from the authenticated user's
 * site theme (loaded by AuthService into authStore.theme) plus the user's
 * light/dark preference (themeStore.isDark). Runs at the App root so the
 * theme is applied even on /login route (which uses platform-default).
 */
function ThemeApplier() {
  const theme = useAuthStore((s) => s.theme);
  const isDark = useThemeStore((s) => s.isDark);

  useEffect(() => {
    const root = document.documentElement;

    if (theme) {
      const palette = isDark ? theme.dark : theme.light;

      // Set BOTH variable naming conventions so all components work:
      // 1. --color-* (used by inline styles in new IAM pages)
      // 2. --background/--foreground/--card/--border/etc (Tailwind-style,
      //    used by existing components like AdminLayout, AppSidebar, etc)
      root.style.setProperty("--color-primary", palette.primary);
      root.style.setProperty("--color-accent", palette.accent);
      root.style.setProperty("--color-background", palette.background);
      root.style.setProperty("--color-surface", palette.surface);
      root.style.setProperty("--color-text", palette.text);
      root.style.setProperty("--color-muted", palette.muted);
      root.style.setProperty("--color-border", palette.border);
      root.style.setProperty("--color-success", palette.success);
      root.style.setProperty("--color-warning", palette.warning);
      root.style.setProperty("--color-danger", palette.danger);

      // Tailwind-style variables (match what index.css defines per brand)
      root.style.setProperty("--background", palette.background);
      root.style.setProperty("--foreground", palette.text);
      root.style.setProperty("--card", palette.surface);
      root.style.setProperty("--card-foreground", palette.text);
      root.style.setProperty("--primary", palette.primary);
      root.style.setProperty("--primary-foreground", pickFg(palette.primary));
      root.style.setProperty("--primary-hover", palette.primary);
      root.style.setProperty("--accent", palette.accent);
      root.style.setProperty("--accent-foreground", pickFg(palette.accent));
      root.style.setProperty("--muted", palette.muted);
      root.style.setProperty("--muted-foreground", palette.muted);
      root.style.setProperty("--border", palette.border);
      root.style.setProperty("--input", palette.border);
      root.style.setProperty("--ring", palette.primary);
      root.style.setProperty("--popover", palette.surface);
      root.style.setProperty("--popover-foreground", palette.text);
      root.style.setProperty("--secondary", palette.muted);
      root.style.setProperty("--secondary-foreground", palette.text);

      root.setAttribute("data-theme", theme.themeKey);
    }

    root.classList.toggle("dark", isDark);
  }, [theme, isDark]);

  return null;
}

function buildMenu(isPlatformAdmin: boolean, isSiteAdmin: boolean, isSystemAdmin: boolean): MenuItem[] {
  const items: MenuItem[] = [{ label: "Dashboard", path: "/dashboard", icon: <LayoutDashboard size={18} /> }];

  if (isPlatformAdmin) {
    items.push({ label: "Sites", path: "/platform/sites", icon: <Building2 size={18} /> });
    items.push({ label: "Role Templates", path: "/platform/role-templates", icon: <KeyRound size={18} /> });
    items.push({ label: "Audit Logs", path: "/audit/logs", icon: <ScrollText size={18} /> });
  }

  // System Admin sees Sites (to manage Business Admins for their site)
  if (isSystemAdmin && !isPlatformAdmin) {
    items.push({ label: "Sites", path: "/platform/sites", icon: <Building2 size={18} /> });
  }

  if (isSiteAdmin || isSystemAdmin) {
    items.push({ label: "Users", path: "/site/users", icon: <Users size={18} /> });
    items.push({ label: "Site Audit", path: "/site/audit", icon: <Shield size={18} /> });
  }

  items.push({ label: "Settings", path: "/settings", icon: <Settings size={18} /> });
  return items;
}

export default function App() {
  const profile = useAuthStore((s) => s.profile);
  const logout = useAuthStore((s) => s.logout);

  const isPlatform = profile?.roles.includes("platform-admin") ?? false;
  const isSiteAdmin = profile?.roles.includes("site-business-admin") ?? false;
  const isSystemAdmin = profile?.roles.includes("system-admin") ?? false;
  const menu = buildMenu(isPlatform, isSiteAdmin, isSystemAdmin);

  return (
    <>
      <ThemeApplier />
      <Routes>
        <Route path="/" element={<Navigate to="/dashboard" replace />} />
        <Route path="/login" element={<LoginPage />} />

        <Route
          element={
            <RequireAuth>
              <AdminLayout
                title="Syntera IAM"
                menuItems={menu}
                user={
                  profile
                    ? {
                        name: profile.displayName,
                        email: profile.email,
                        role: profile.roles.join(", "),
                      }
                    : undefined
                }
                onLogout={() => {
                  logout();
                  window.location.href = "/login";
                }}
              />
            </RequireAuth>
          }
        >
          <Route path="/dashboard" element={<DashboardPage />} />

          {/* Platform Admin + System Admin routes */}
          <Route
            path="/platform/sites"
            element={
              <RequirePlatformOrSystemAdmin>
                <SitesPage />
              </RequirePlatformOrSystemAdmin>
            }
          />
          <Route
            path="/platform/role-templates"
            element={
              <RequirePlatformAdmin>
                <RoleTemplatesPage />
              </RequirePlatformAdmin>
            }
          />

          {/* Site Admin routes */}
          <Route
            path="/site/users"
            element={
              <RequireSiteAdmin>
                <UsersPage />
              </RequireSiteAdmin>
            }
          />

          {/* Audit Logs (both platform and site admins) */}
          <Route path="/audit/logs" element={<AuditLogsPage />} />
          <Route path="/site/audit" element={<AuditLogsPage />} />

          <Route path="/settings" element={<SettingsPage />} />
        </Route>

        <Route path="*" element={<Navigate to="/dashboard" replace />} />
      </Routes>
    </>
  );
}
