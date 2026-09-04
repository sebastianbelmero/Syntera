import { Navigate, useLocation } from "react-router-dom";
import type { ReactNode } from "react";
import { useAuthStore } from "../store/authStore";

/**
 * H7-full (Sprint 4): full-screen loading state shown while silent
 * refresh is in flight on app boot. Prevents a "login page flash"
 * for already-authenticated users before the in-memory store is
 * repopulated from the httpOnly cookie.
 */
function AuthInitializing() {
  return (
    <div className="flex h-screen flex-col items-center justify-center gap-4 px-6">
      <div
        className="h-8 w-8 animate-spin rounded-full border-2 border-current border-t-transparent"
        style={{ color: "var(--color-primary)" }}
        aria-label="Loading"
      />
      <p className="text-sm" style={{ color: "var(--color-muted)" }}>
        Loading session…
      </p>
    </div>
  );
}

/**
 * Route guard — redirects to /login if the user is not authenticated.
 * Preserves the original location so we can bounce back after login.
 *
 * H7-full: while silent refresh is in flight (initializing=true), we
 * render AuthInitializing instead of redirecting to /login. This
 * prevents the "login page flash" for already-authenticated users
 * whose in-memory store was wiped by a page reload.
 */
export function RequireAuth({ children }: { children: ReactNode }) {
  const isAuthed = useAuthStore((s) => s.isAuthenticated());
  const initializing = useAuthStore((s) => s.initializing);
  const location = useLocation();

  if (initializing) return <AuthInitializing />;
  if (!isAuthed) {
    return (
      <Navigate
        to="/login"
        replace
        state={{ from: location.pathname + location.search }}
      />
    );
  }
  return <>{children}</>;
}

/**
 * Platform Admin only — admin@syntera.com with platform-admin role.
 */
export function RequirePlatformAdmin({ children }: { children: ReactNode }) {
  const profile = useAuthStore((s) => s.profile);
  const isAuthed = useAuthStore((s) => s.isAuthenticated());
  const initializing = useAuthStore((s) => s.initializing);
  const location = useLocation();

  if (initializing) return <AuthInitializing />;
  if (!isAuthed || !profile) {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  }

  if (!profile.roles.includes("platform-admin")) {
    return <ForbiddenPage />;
  }
  return <>{children}</>;
}

/**
 * Platform Admin OR System Admin — used for /platform/sites.
 * System Admin can access Sites to manage Business Admins for their site.
 */
export function RequirePlatformOrSystemAdmin({ children }: { children: ReactNode }) {
  const profile = useAuthStore((s) => s.profile);
  const isAuthed = useAuthStore((s) => s.isAuthenticated());
  const initializing = useAuthStore((s) => s.initializing);
  const location = useLocation();

  if (initializing) return <AuthInitializing />;
  if (!isAuthed || !profile) {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  }

  const allowed = profile.roles.includes("platform-admin")
    || profile.roles.includes("system-admin");
  if (!allowed) {
    return <ForbiddenPage />;
  }
  return <>{children}</>;
}

/**
 * Site Business Admin only — must have site-business-admin role.
 * Platform Admin also passes (they can do anything site admins can do).
 */
export function RequireSiteAdmin({ children }: { children: ReactNode }) {
  const profile = useAuthStore((s) => s.profile);
  const isAuthed = useAuthStore((s) => s.isAuthenticated());
  const initializing = useAuthStore((s) => s.initializing);
  const location = useLocation();

  if (initializing) return <AuthInitializing />;
  if (!isAuthed || !profile) {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  }

  const allowed = profile.roles.includes("platform-admin")
    || profile.roles.includes("site-business-admin")
    || profile.roles.includes("system-admin")
    || profile.roles.includes("eng-manager")
    || profile.roles.includes("supervisor")
    || profile.roles.includes("qo-manager");
  if (!allowed) {
    return <ForbiddenPage />;
  }
  return <>{children}</>;
}

/**
 * Generic role guard (kept for backward compatibility).
 */
export function RequireRole({
  roles,
  children,
}: {
  roles: string[];
  children: ReactNode;
}) {
  const profile = useAuthStore((s) => s.profile);
  const isAuthed = useAuthStore((s) => s.isAuthenticated());
  const initializing = useAuthStore((s) => s.initializing);
  const location = useLocation();

  if (initializing) return <AuthInitializing />;
  if (!isAuthed || !profile) {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  }

  const allowed = roles.some((r) => profile.roles.includes(r));
  if (!allowed) {
    return <ForbiddenPage />;
  }
  return <>{children}</>;
}

function ForbiddenPage() {
  return (
    <div className="flex h-screen flex-col items-center justify-center gap-4 px-6 text-center">
      <h1 className="text-3xl font-bold" style={{ color: "var(--color-danger)" }}>403</h1>
      <p style={{ color: "var(--color-muted)" }}>
        You do not have permission to access this page. Contact your administrator
        if you believe this is an error.
      </p>
    </div>
  );
}
