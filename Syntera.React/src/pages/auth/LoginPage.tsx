import { useState } from "react";
import { Navigate, useLocation, useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { Eye, EyeOff, Loader2, Lock, Mail } from "lucide-react";
import { useAuthStore } from "../../store/authStore";
import { login as loginApi } from "../../api/auth";
import { ApiError } from "../../api/client";
import logoUrl from "../../assets/syntera-logo-tagline.png";

/**
 * Login page — professional enterprise login with Syntera branding.
 *
 * Features:
 *   - Full-screen split layout: brand panel (left) + form (right)
 *   - Logo with tagline
 *   - Show/hide password toggle
 *   - Loading spinner overlay
 *   - Responsive: brand panel hidden on mobile
 */
export default function LoginPage() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
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
    <div className="min-h-screen flex" style={{ backgroundColor: "var(--color-background)", color: "var(--color-text)" }}>
      
      {/* ─── Brand Panel (hidden on mobile) ─── */}
      <div
        className="hidden lg:flex lg:w-1/2 flex-col justify-between p-12 relative overflow-hidden"
        style={{
          background: `linear-gradient(135deg, var(--color-primary) 0%, var(--color-accent) 100%)`,
        }}
      >
        {/* Decorative circles */}
        <div className="absolute top-0 right-0 w-96 h-96 rounded-full opacity-10" style={{ backgroundColor: "white", transform: "translate(30%, -30%)" }} />
        <div className="absolute bottom-0 left-0 w-64 h-64 rounded-full opacity-10" style={{ backgroundColor: "white", transform: "translate(-30%, 30%)" }} />
        <div className="absolute top-1/2 right-12 w-32 h-32 rounded-full opacity-5" style={{ backgroundColor: "white" }} />

        {/* Logo */}
        <div className="relative z-10">
          <div className="bg-white rounded-2xl p-3 inline-block shadow-lg">
            <img src={logoUrl} alt="Syntera" className="h-12 w-auto" />
          </div>
        </div>

        {/* Tagline */}
        <div className="relative z-10 text-white">
          <h2 className="text-4xl font-bold leading-tight mb-3">
            Vital Science,<br />Vital Commerce.
          </h2>
          <p className="text-lg opacity-90 max-w-md">
            Multi-tenant Identity &amp; Access Management platform for
            pharmaceutical excellence across Kalventis, Kalbe, Dankos,
            Hexpharm, Fima &amp; GOF.
          </p>
          <div className="mt-8 flex items-center gap-6 text-sm opacity-80">
            <span className="flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-white" /> 21 CFR Part 11
            </span>
            <span className="flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-white" /> GxP Compliant
            </span>
            <span className="flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-white" /> ISO 27001
            </span>
          </div>
        </div>

        {/* Footer */}
        <div className="relative z-10 text-white text-xs opacity-60">
          © 2026 Syntera. One Platform. One Standard. One Direction.
        </div>
      </div>

      {/* ─── Form Panel ─── */}
      <div className="flex-1 flex items-center justify-center px-6 py-12">
        <div className="w-full max-w-sm">
          
          {/* Mobile logo */}
          <div className="lg:hidden flex flex-col items-center mb-8">
            <div className="bg-white rounded-xl p-2 shadow-md mb-4">
              <img src={logoUrl} alt="Syntera" className="h-10 w-auto" />
            </div>
          </div>

          {/* Header */}
          <div className="mb-8">
            <h1 className="text-2xl font-bold tracking-tight">Sign in to Syntera</h1>
            <p className="text-sm mt-2" style={{ color: "var(--color-muted)" }}>
              Enter your credentials to access the platform.
            </p>
          </div>

          {/* Form */}
          <form onSubmit={handleSubmit} className="flex flex-col gap-5">
            
            {/* Email */}
            <div className="flex flex-col gap-1.5">
              <label htmlFor="email" className="text-sm font-medium">Email</label>
              <div className="relative">
                <Mail size={18} className="absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none"
                  style={{ color: "var(--color-muted)" }} />
                <input
                  id="email"
                  type="email"
                  autoComplete="username"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  disabled={loading}
                  placeholder="you@company.com"
                  className="w-full pl-10 pr-3 py-3 rounded-lg outline-none transition focus-visible:ring-2 focus-visible:ring-[var(--color-primary)] min-h-[44px]"
                  style={{
                    backgroundColor: "var(--color-background)",
                    border: "1px solid var(--color-border)",
                    color: "var(--color-text)",
                  }}
                  required
                />
              </div>
            </div>

            {/* Password */}
            <div className="flex flex-col gap-1.5">
              <label htmlFor="password" className="text-sm font-medium">Password</label>
              <div className="relative">
                <Lock size={18} className="absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none"
                  style={{ color: "var(--color-muted)" }} />
                <input
                  id="password"
                  type={showPassword ? "text" : "password"}
                  autoComplete="current-password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  disabled={loading}
                  placeholder="••••••••"
                  className="w-full pl-10 pr-10 py-3 rounded-lg outline-none transition focus-visible:ring-2 focus-visible:ring-[var(--color-primary)] min-h-[44px]"
                  style={{
                    backgroundColor: "var(--color-background)",
                    border: "1px solid var(--color-border)",
                    color: "var(--color-text)",
                  }}
                  required
                />
                <button
                  type="button"
                  onClick={() => setShowPassword(!showPassword)}
                  className="absolute right-3 top-1/2 -translate-y-1/2 p-1 rounded hover:opacity-70 transition-opacity"
                  style={{ color: "var(--color-muted)" }}
                  aria-label={showPassword ? "Hide password" : "Show password"}
                >
                  {showPassword ? <EyeOff size={18} /> : <Eye size={18} />}
                </button>
              </div>
            </div>

            {/* Submit */}
            <button
              type="submit"
              disabled={loading}
              className="mt-2 py-3 rounded-lg font-medium transition disabled:opacity-50 min-h-[44px] active:scale-[0.98] flex items-center justify-center gap-2"
              style={{
                backgroundColor: "var(--color-primary)",
                color: "var(--color-primary-foreground)",
              }}
            >
              {loading ? (
                <>
                  <Loader2 size={18} className="animate-spin" />
                  Signing in...
                </>
              ) : (
                "Sign in"
              )}
            </button>
          </form>

          {/* Helper text */}
          <div className="mt-8 pt-6" style={{ borderTop: "1px solid var(--color-border)" }}>
            <p className="text-xs text-center" style={{ color: "var(--color-muted)" }}>
              Authentication is routed by your email domain.
              <br />
              Contact your site admin if you cannot log in.
            </p>
          </div>

          {/* Mobile footer */}
          <div className="lg:hidden mt-6 text-center">
            <p className="text-xs" style={{ color: "var(--color-muted)" }}>
              © 2026 Syntera. One Platform. One Standard.
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
