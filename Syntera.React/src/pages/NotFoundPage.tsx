import { Link } from "react-router-dom";
import { Home, SearchX } from "lucide-react";

/**
 * NotFound — 404 catch-all. Previously the wildcard route silently
 * redirected to /dashboard, which hid typos in URLs and made deep links
 * feel broken ("why am I on the dashboard?"). Render an explicit 404 with
 * a way back instead.
 *
 * Standalone (outside AdminLayout): works for both authenticated and
 * unauthenticated visitors. The "Back to Dashboard" link routes through
 * /dashboard's RequireAuth, which bounces anonymous users to /login.
 */
export default function NotFoundPage() {
  return (
    <div className="flex h-screen flex-col items-center justify-center gap-4 px-6 text-center">
      <SearchX size={48} aria-hidden style={{ color: "var(--color-muted)" }} />
      <h1 className="text-3xl font-bold">404 — Page Not Found</h1>
      <p className="max-w-md text-sm" style={{ color: "var(--color-muted)" }}>
        The page you are looking for does not exist, has been moved, or the
        link is outdated. Check the address or head back to the dashboard.
      </p>
      <Link
        to="/dashboard"
        className="mt-2 inline-flex items-center gap-2 rounded-lg px-4 py-2.5 text-sm font-medium"
        style={{
          backgroundColor: "var(--color-primary)",
          color: "var(--color-primary-foreground)",
        }}
      >
        <Home size={16} /> Back to Dashboard
      </Link>
    </div>
  );
}
