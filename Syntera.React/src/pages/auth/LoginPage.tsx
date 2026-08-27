import { useState } from "react";
import { Navigate, useLocation, useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { ShieldCheck } from "lucide-react";
import { useAuthStore } from "../../store/authStore";
import { login as loginApi } from "../../api/auth";
import { ApiError } from "../../api/client";

/**
 * Login page — single form, single submit.
 * Email domain determines authentication method (handled by backend):
 *   @syntera.com      → Platform Admin (local credential)
 *   @kalventis.com    → LDAP Kalventis
 *   @kalbe.co.id      → LDAP Kalbe
 *   ... (other registered site domains)
 *
 * No demo credentials are auto-filled — production-safe.
 */
export default function LoginPage() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const navigate = useNavigate();
  const location = useLocation();
  const isAuthed = useAuthStore((s) => s.isAuthenticated());

  if (isAuthed) {
    const from = (location.state as { from?: string } | null)?.from ?? "/dashboard";
    return <Navigate to={from} replace />;
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email || !password) {
      toast.error("Email and password are required.");
      return;
    }
    setLoading(true);
    try {
      const res = await loginApi({ email, password });
      toast.success(`Welcome, ${res.profile.displayName}!`);
      // Honor the "from" path preserved by RequireAuth, fall back to /dashboard.
      const from = (location.state as { from?: string } | null)?.from ?? "/dashboard";
      navigate(from, { replace: true });
    } catch (err) {
      if (err instanceof ApiError) {
        toast.error(err.message);
      } else {
        toast.error("Login failed. Please try again.");
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <div
      className="min-h-screen flex items-center justify-center px-4"
      style={{ backgroundColor: "var(--color-background)", color: "var(--color-text)" }}
    >
      <div
        className="w-full max-w-md rounded-2xl p-8 shadow-xl"
        style={{ backgroundColor: "var(--color-surface)", border: "1px solid var(--color-border)" }}
      >
        <div className="flex flex-col items-center mb-8">
          <div
            className="w-14 h-14 rounded-2xl flex items-center justify-center mb-4"
            style={{ backgroundColor: "var(--color-primary)" }}
          >
            <ShieldCheck size={28} color="white" />
          </div>
          <h1 className="text-2xl font-bold">Syntera IAM</h1>
          <p className="text-sm mt-1" style={{ color: "var(--color-muted)" }}>
            Multi-tenant Identity &amp; Access Management
          </p>
        </div>

        <form onSubmit={handleSubmit} className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <label htmlFor="email" className="text-sm font-medium">Email</label>
            <input
              id="email"
              type="email"
              autoComplete="username"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              disabled={loading}
              placeholder="you@company.com"
              className="px-3 py-2 rounded-lg outline-none transition"
              style={{
                backgroundColor: "var(--color-background)",
                border: "1px solid var(--color-border)",
                color: "var(--color-text)",
              }}
              required
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <label htmlFor="password" className="text-sm font-medium">Password</label>
            <input
              id="password"
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              disabled={loading}
              placeholder="••••••••"
              className="px-3 py-2 rounded-lg outline-none transition"
              style={{
                backgroundColor: "var(--color-background)",
                border: "1px solid var(--color-border)",
                color: "var(--color-text)",
              }}
              required
            />
          </div>
          <button
            type="submit"
            disabled={loading}
            className="mt-2 py-2.5 rounded-lg font-medium transition disabled:opacity-50"
            style={{
              backgroundColor: "var(--color-primary)",
              color: "white",
            }}
          >
            {loading ? "Signing in..." : "Sign in"}
          </button>
        </form>

        <p className="mt-6 text-xs text-center" style={{ color: "var(--color-muted)" }}>
          Authentication is routed by your email domain.
          <br />
          Contact your site admin if you cannot log in.
        </p>
      </div>
    </div>
  );
}
