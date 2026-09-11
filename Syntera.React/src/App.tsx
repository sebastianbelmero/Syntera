import { Routes, Route, Navigate } from "react-router-dom";
import { useEffect } from "react";
import { LayoutDashboard, Settings, Building2, Users, ScrollText, KeyRound, ClipboardCheck } from "lucide-react";

import { RequireAuth, RequirePlatformAdmin, RequirePlatformOrSystemAdmin, RequirePermission } from "./routes/guards";
import { useAuthStore } from "./store/authStore";
import { logout as apiLogout } from "./api/auth";
import { useThemeStore } from "./store/themeStore";
import { AdminLayout, type MenuItem } from "./components/layout";
import type { UserProfile } from "./types";

import LoginPage from "./pages/auth/LoginPage";
import DashboardPage from "./pages/dashboard/DashboardPage";
import SitesPage from "./pages/platform/SitesPage";
import RoleTemplatesPage from "./pages/platform/RoleTemplatesPage";
import ApprovalQueuePage from "./pages/platform/ApprovalQueuePage";
import UsersPage from "./pages/site/UsersPage";
import UserDetailPage from "./pages/site/UserDetailPage";
import AuditLogsPage from "./pages/audit/AuditLogsPage";
import SettingsPage from "./pages/settings/SettingsPage";
import NotFoundPage from "./pages/NotFoundPage";

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

/**
 * Sidebar menu — PERMISSION-driven, mirroring backend authorization
 * (guards.tsx → RequirePermission):
 *
 *   • Platform Admin bypasses permission checks (backend HasPermission
 *     semantics), so they see every admin page.
 *   • System Admin sees Sites only — their user-management job (assigning
 *     Business Admins) lives there. NOTE: they deliberately do NOT get the
 *     Users menu: SystemAdminPermissions has no user.read, so the site
 *     users endpoint would 403 for them.
 *   • Everyone else sees pages gated by their EFFECTIVE permission keys
 *     (role ∪ direct grants − deny). The menu can therefore never
 *     advertise a page the backend would reject — fixing the old gap where
 *     eng-manager could open /site/users via URL but had no menu entry,
 *     and viewers had audit.read yet no Audit menu item.
 */
function buildMenu(profile: UserProfile | null): MenuItem[] {
  const items: MenuItem[] = [{ label: "Dashboard", path: "/dashboard", icon: <LayoutDashboard size={18} /> }];
  if (!profile) return items;

  const isPlatform = profile.roles.includes("platform-admin");
  const isSystemAdmin = profile.roles.includes("system-admin");
  const has = (perm: string) => isPlatform || profile.permissions.includes(perm);

  if (isPlatform) {
    items.push({ label: "Sites", path: "/platform/sites", icon: <Building2 size={18} /> });
    items.push({ label: "Role Templates", path: "/platform/role-templates", icon: <KeyRound size={18} /> });
    items.push({ label: "Approval Queue", path: "/platform/approvals", icon: <ClipboardCheck size={18} /> });
  } else if (isSystemAdmin) {
    // System Admin manages Business Admins through the Sites page.
    items.push({ label: "Sites", path: "/platform/sites", icon: <Building2 size={18} /> });
  }

  if (has("user.read")) {
    items.push({ label: "Users", path: "/site/users", icon: <Users size={18} /> });
  }
  if (has("audit.read")) {
    items.push({ label: "Audit Logs", path: "/audit/logs", icon: <ScrollText size={18} /> });
  }

  items.push({ label: "Settings", path: "/settings", icon: <Settings size={18} /> });
  return items;
}

export default function App() {
  const profile = useAuthStore((s) => s.profile);
  const menu = buildMenu(profile);

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
                onLogout={async () => {
                  // LOGOUT-RELOGIN FIX (2026-09-09): revoke the SERVER-side
                  // session FIRST — POST /api/auth/logout revokes the
                  // refresh-token family and deletes the httpOnly cookie —
                  // THEN hard-navigate. The previous handler only cleared the
                  // in-memory store: the cookie stayed valid, main.tsx's
                  // initAuth() silently re-authenticated from it on the next
                  // boot, and the user was logged right back in
                  // ("logout → instant re-login"). apiLogout() clears the
                  // store in its finally; the .catch guarantees navigation
                  // still happens if the POST fails (e.g. network error).
                  await apiLogout().catch(() => {});
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
          <Route
            path="/platform/approvals"
            element={
              <RequirePlatformAdmin>
                <ApprovalQueuePage />
              </RequirePlatformAdmin>
            }
          />

          {/* Site user management — gated by user.read (Platform Admin
              bypasses; Biz Admin / Eng Manager hold it). */}
          <Route
            path="/site/users"
            element={
              <RequirePermission permission="user.read">
                <UsersPage />
              </RequirePermission>
            }
          />
          <Route
            path="/site/users/:id"
            element={
              <RequirePermission permission="user.read">
                <UserDetailPage />
              </RequirePermission>
            }
          />

          {/* Audit trail — gated by audit.read (viewers/supervisors/QO
              managers hold it; technicians do not). Platform Admin
              bypasses. Previously this route had NO guard at all — any
              authenticated user could open it and eat a raw 403. */}
          <Route
            path="/audit/logs"
            element={
              <RequirePermission permission="audit.read">
                <AuditLogsPage />
              </RequirePermission>
            }
          />
          {/* Legacy alias — site admins' old bookmark still works. */}
          <Route path="/site/audit" element={<Navigate to="/audit/logs" replace />} />

          <Route path="/settings" element={<SettingsPage />} />
        </Route>

        {/* Explicit 404 instead of a silent redirect to /dashboard. */}
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </>
  );
}
