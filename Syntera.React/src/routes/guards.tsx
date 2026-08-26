import { Navigate, useLocation } from "react-router-dom";
import type { ReactNode } from "react";
import { useAuthStore } from "../store/authStore";

/**
 * Route guard — redirects to /login if the user is not authenticated.
 * Preserves the original location so we can bounce back after login.
 *
 * Usage:
 *   <Route element={<RequireAuth><DashboardPage /></RequireAuth>} />
 */
export function RequireAuth({ children }: { children: ReactNode }) {
  const isAuthed = useAuthStore((s) => s.isAuthenticated());
  const location = useLocation();
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
 * Role guard — additionally requires the user to hold one of the
 * given roles. Composes RequireAuth internally so the component is
 * self-sufficient: if reused outside a RequireAuth-wrapped layout
 * (e.g. a future public-facing page that needs role checks), it
 * still redirects unauthenticated users to /login rather than
 * rendering the Forbidden page. Authenticated-but-unauthorized
 * users see the 403 page (existing behavior preserved).
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
  const location = useLocation();

  if (!isAuthed || !profile) {
    return (
      <Navigate
        to="/login"
        replace
        state={{ from: location.pathname + location.search }}
      />
    );
  }

  const userRoles = profile.roles;
  const allowed = roles.some((r) => userRoles.includes(r));
  if (!allowed) {
    return <ForbiddenPage />;
  }
  return <>{children}</>;
}

function ForbiddenPage() {
  return (
    <div className="flex h-screen flex-col items-center justify-center gap-4 px-6 text-center">
      <h1 className="text-3xl font-bold text-[var(--primary)]">403</h1>
      <p className="text-[var(--muted-foreground)]">
        Anda tidak memiliki izin untuk mengakses halaman ini. Hubungi
        administrator jika ini sebuah kekeliruan.
      </p>
    </div>
  );
}
