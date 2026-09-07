import { useEffect, useRef, useState } from "react";
import { Navigate, useLocation, useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { ArrowLeft, Eye, EyeOff, Loader2, Lock, Mail, ShieldCheck, KeyRound } from "lucide-react";
import { useAuthStore } from "../../store/authStore";
import {
  changePassword as changePasswordApi,
  login as loginApi,
  loginMfa as loginMfaApi,
} from "../../api/auth";
import { ApiError } from "../../api/client";
import { validatePasswordPair, PASSWORD_POLICY_HINT } from "../../lib/password";
import logoUrl from "../../assets/syntera-logo-tagline.png";

/**
 * Login page — professional enterprise login with Syntera branding.
 *
 * Sprint 3.2 — multi-step flow:
 *   1. credentials    — email + password (default).
 *   2. mfa            — 6-digit TOTP code (only when `requiresMfa`).
 *   3. passwordChange — new password + confirm (only when `requiresPasswordChange`).
 *
 * All three steps render in the SAME form card (no separate route) so
 * the URL stays at `/login` and back-button behaviour stays clean.
 *
 * Backward compat: when neither `requiresMfa` nor `requiresPasswordChange`
 * is set, the flow is identical to before — submit → store tokens →
 * navigate to dashboard (or `from`).
 */
type LoginStep = "credentials" | "mfa" | "passwordChange";

export default function LoginPage() {
  // ── credentials step ────────────────────────────────────────
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);

  // ── mfa step ────────────────────────────────────────────────
  const [mfaCode, setMfaCode] = useState("");
  const [mfaChallengeToken, setMfaChallengeToken] = useState<string | null>(null);

  // ── passwordChange step ─────────────────────────────────────
  const [newPassword, setNewPassword] = useState("");
  const [confirmNewPassword, setConfirmNewPassword] = useState("");
  const [showNewPassword, setShowNewPassword] = useState(false);
  const [passwordChangeChallengeToken, setPasswordChangeChallengeToken] =
    useState<string | null>(null);

  // ── shared ──────────────────────────────────────────────────
  const [step, setStep] = useState<LoginStep>("credentials");
  const [loading, setLoading] = useState(false);

  const navigate = useNavigate();
  const location = useLocation();
  const isAuthed = useAuthStore((s) => s.isAuthenticated());
  const mfaInputRef = useRef<HTMLInputElement | null>(null);

  // Focus the MFA input when that step is shown.
  useEffect(() => {
    if (step === "mfa") {
      // requestAnimationFrame — wait for the input to mount.
      const id = requestAnimationFrame(() => mfaInputRef.current?.focus());
      return () => cancelAnimationFrame(id);
    }
  }, [step]);

  if (isAuthed) {
    const from = (location.state as { from?: string } | null)?.from ?? "/dashboard";
    return <Navigate to={from} replace />;
  }

  // ── credentials submit ──────────────────────────────────────
  const handleSubmitCredentials = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email || !password) {
      toast.error("Email and password are required.");
      return;
    }
    setLoading(true);
    try {
      const res = await loginApi({ email, password });

      // MFA challenge — switch step, keep tokens out of the store.
      if (res.requiresMfa && res.mfaChallengeToken) {
        setMfaChallengeToken(res.mfaChallengeToken);
        setMfaCode("");
        setStep("mfa");
        return;
      }

      // Forced password change — switch step, keep tokens out of the store.
      if (res.requiresPasswordChange && res.passwordChangeChallengeToken) {
        setPasswordChangeChallengeToken(res.passwordChangeChallengeToken);
        setNewPassword("");
        setConfirmNewPassword("");
        setStep("passwordChange");
        return;
      }

      // Normal login — the api wrapper already pushed tokens to the store.
      toast.success(`Welcome, ${res.profile.displayName}!`);
      const from =
        (location.state as { from?: string } | null)?.from ?? "/dashboard";
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

  // ── mfa submit ─────────────────────────────────────────────
  const handleSubmitMfa = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!mfaChallengeToken) {
      // Should never happen — challenge token was set when we entered the
      // step. Reset to credentials to recover.
      setStep("credentials");
      return;
    }
    if (mfaCode.length !== 6 || !/^\d{6}$/.test(mfaCode)) {
      toast.error("Enter the 6-digit code from your authenticator app.");
      return;
    }
    setLoading(true);
    try {
      const res = await loginMfaApi({ mfaChallengeToken, code: mfaCode });
      toast.success(`Welcome, ${res.profile.displayName}!`);
      const from =
        (location.state as { from?: string } | null)?.from ?? "/dashboard";
      navigate(from, { replace: true });
    } catch (err) {
      if (err instanceof ApiError) {
        toast.error(err.message || "Invalid MFA code.");
      } else {
        toast.error("Invalid MFA code. Please try again.");
      }
      setMfaCode("");
      // Keep the user on the MFA step so they can retry; the challenge
      // token is single-use per attempt server-side but the backend
      // re-issues a new one as long as the original login attempt hasn't
      // expired. If they truly can't get in, "Back to login" lets them
      // restart from credentials.
    } finally {
      setLoading(false);
    }
  };

  // ── passwordChange submit ──────────────────────────────────
  const handleSubmitPasswordChange = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!passwordChangeChallengeToken) {
      setStep("credentials");
      return;
    }
    const v = validatePasswordPair(newPassword, confirmNewPassword);
    if (!v.ok) {
      toast.error(v.message);
      return;
    }
    setLoading(true);
    try {
      await changePasswordApi({
        newPassword,
        passwordChangeChallengeToken,
      });
      toast.success("Password changed. Please log in with your new password.");
      // Reset back to credentials step so the user can log in with the
      // new password. Wipe the old password field — the user typed the
      // expired one, but we don't want to leave stale credentials in
      // memory; clear the challenge token too (it's single-use anyway).
      setPassword("");
      setNewPassword("");
      setConfirmNewPassword("");
      setPasswordChangeChallengeToken(null);
      setStep("credentials");
    } catch (err) {
      if (err instanceof ApiError) {
        toast.error(err.message || "Failed to change password.");
      } else {
        toast.error("Failed to change password. Please try again.");
      }
    } finally {
      setLoading(false);
    }
  };

  // ── step resets ─────────────────────────────────────────────
  const backToCredentials = () => {
    setStep("credentials");
    setMfaCode("");
    setMfaChallengeToken(null);
    setNewPassword("");
    setConfirmNewPassword("");
    setPasswordChangeChallengeToken(null);
  };

  // ── shared input style ──────────────────────────────────────
  const inputStyle: React.CSSProperties = {
    backgroundColor: "var(--color-background)",
    border: "1px solid var(--color-border)",
    color: "var(--color-text)",
  };
  const inputCls =
    "w-full pl-10 pr-3 py-3 rounded-lg outline-none transition focus-visible:ring-2 focus-visible:ring-[var(--color-primary)] min-h-[44px]";
  const labelCls = "text-sm font-medium";

  return (
    <div
      className="min-h-screen flex"
      style={{
        backgroundColor: "var(--color-background)",
        color: "var(--color-text)",
      }}
    >
      {/* ─── Brand Panel (hidden on mobile) ─── */}
      <div
        className="hidden lg:flex lg:w-1/2 flex-col justify-between p-12 relative overflow-hidden"
        style={{
          background: `linear-gradient(135deg, var(--color-primary) 0%, var(--color-accent) 100%)`,
        }}
      >
        {/* Decorative circles */}
        <div
          className="absolute top-0 right-0 w-96 h-96 rounded-full opacity-10"
          style={{ backgroundColor: "white", transform: "translate(30%, -30%)" }}
        />
        <div
          className="absolute bottom-0 left-0 w-64 h-64 rounded-full opacity-10"
          style={{ backgroundColor: "white", transform: "translate(-30%, 30%)" }}
        />
        <div
          className="absolute top-1/2 right-12 w-32 h-32 rounded-full opacity-5"
          style={{ backgroundColor: "white" }}
        />

        {/* Logo */}
        <div className="relative z-10">
          <div className="bg-white rounded-2xl p-3 inline-block shadow-lg">
            <img src={logoUrl} alt="Syntera" className="h-12 w-auto" />
          </div>
        </div>

        {/* Tagline */}
        <div className="relative z-10 text-white">
          <h2 className="text-4xl font-bold leading-tight mb-3">
            Vital Science,
            <br />
            Vital Commerce.
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

          {/* Header — varies by step */}
          <div className="mb-8">
            {step === "credentials" && (
              <>
                <h1 className="text-2xl font-bold tracking-tight">
                  Sign in to Syntera
                </h1>
                <p className="text-sm mt-2" style={{ color: "var(--color-text)" }}>
                  Enter your credentials to access the platform.
                </p>
              </>
            )}
            {step === "mfa" && (
              <>
                <h1 className="text-2xl font-bold tracking-tight flex items-center gap-2">
                  <ShieldCheck size={22} /> Verification required
                </h1>
                <p className="text-sm mt-2" style={{ color: "var(--color-text)" }}>
                  Enter the 6-digit code from your authenticator app to finish
                  signing in.
                </p>
              </>
            )}
            {step === "passwordChange" && (
              <>
                <h1 className="text-2xl font-bold tracking-tight flex items-center gap-2">
                  <KeyRound size={22} /> Update your password
                </h1>
                <p className="text-sm mt-2" style={{ color: "var(--color-text)" }}>
                  Your password has expired. Please set a new password to
                  continue.
                </p>
              </>
            )}
          </div>

          {/* ─── Step: credentials ─── */}
          {step === "credentials" && (
            <form onSubmit={handleSubmitCredentials} className="flex flex-col gap-5">
              {/* Email */}
              <div className="flex flex-col gap-1.5">
                <label htmlFor="email" className={labelCls}>
                  Email
                </label>
                <div className="relative">
                  <Mail
                    size={18}
                    className="absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none"
                    style={{ color: "var(--color-text)" }}
                  />
                  <input
                    id="email"
                    type="email"
                    autoComplete="username"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    disabled={loading}
                    placeholder="you@company.com"
                    className={inputCls}
                    style={inputStyle}
                    required
                  />
                </div>
              </div>

              {/* Password */}
              <div className="flex flex-col gap-1.5">
                <label htmlFor="password" className={labelCls}>
                  Password
                </label>
                <div className="relative">
                  <Lock
                    size={18}
                    className="absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none"
                    style={{ color: "var(--color-text)" }}
                  />
                  <input
                    id="password"
                    type={showPassword ? "text" : "password"}
                    autoComplete="current-password"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    disabled={loading}
                    placeholder="••••••••"
                    className="w-full pl-10 pr-10 py-3 rounded-lg outline-none transition focus-visible:ring-2 focus-visible:ring-[var(--color-primary)] min-h-[44px]"
                    style={inputStyle}
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
          )}

          {/* ─── Step: mfa ─── */}
          {step === "mfa" && (
            <form onSubmit={handleSubmitMfa} className="flex flex-col gap-5">
              <div className="flex flex-col gap-1.5">
                <label htmlFor="mfa-code" className={labelCls}>
                  Authenticator code
                </label>
                <div className="relative">
                  <ShieldCheck
                    size={18}
                    className="absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none"
                    style={{ color: "var(--color-text)" }}
                  />
                  <input
                    ref={mfaInputRef}
                    id="mfa-code"
                    type="text"
                    inputMode="numeric"
                    pattern="[0-9]*"
                    autoComplete="one-time-code"
                    maxLength={6}
                    value={mfaCode}
                    onChange={(e) =>
                      setMfaCode(e.target.value.replace(/\D/g, "").slice(0, 6))
                    }
                    disabled={loading}
                    placeholder="123456"
                    className="w-full pl-10 pr-3 py-3 rounded-lg outline-none transition focus-visible:ring-2 focus-visible:ring-[var(--color-primary)] min-h-[44px] tracking-[0.35em] text-center font-mono text-lg"
                    style={inputStyle}
                    required
                  />
                </div>
                <p className="text-xs mt-1" style={{ color: "var(--color-muted)" }}>
                  Open your authenticator app (Google Authenticator, Microsoft
                  Authenticator, Authy, etc.) and enter the current 6-digit code.
                </p>
              </div>

              <button
                type="submit"
                disabled={loading || mfaCode.length !== 6}
                className="mt-2 py-3 rounded-lg font-medium transition disabled:opacity-50 min-h-[44px] active:scale-[0.98] flex items-center justify-center gap-2"
                style={{
                  backgroundColor: "var(--color-primary)",
                  color: "var(--color-primary-foreground)",
                }}
              >
                {loading ? (
                  <>
                    <Loader2 size={18} className="animate-spin" />
                    Verifying...
                  </>
                ) : (
                  "Verify and sign in"
                )}
              </button>

              <button
                type="button"
                onClick={backToCredentials}
                disabled={loading}
                className="text-sm flex items-center justify-center gap-1.5 min-h-[44px] transition hover:opacity-80 disabled:opacity-50"
                style={{ color: "var(--color-muted)" }}
              >
                <ArrowLeft size={14} /> Back to login
              </button>
            </form>
          )}

          {/* ─── Step: passwordChange ─── */}
          {step === "passwordChange" && (
            <form
              onSubmit={handleSubmitPasswordChange}
              className="flex flex-col gap-5"
            >
              <div className="flex flex-col gap-1.5">
                <label htmlFor="new-password" className={labelCls}>
                  New password
                </label>
                <div className="relative">
                  <Lock
                    size={18}
                    className="absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none"
                    style={{ color: "var(--color-text)" }}
                  />
                  <input
                    id="new-password"
                    type={showNewPassword ? "text" : "password"}
                    autoComplete="new-password"
                    value={newPassword}
                    onChange={(e) => setNewPassword(e.target.value)}
                    disabled={loading}
                    placeholder="••••••••••••"
                    className="w-full pl-10 pr-10 py-3 rounded-lg outline-none transition focus-visible:ring-2 focus-visible:ring-[var(--color-primary)] min-h-[44px]"
                    style={inputStyle}
                    required
                  />
                  <button
                    type="button"
                    onClick={() => setShowNewPassword(!showNewPassword)}
                    className="absolute right-3 top-1/2 -translate-y-1/2 p-1 rounded hover:opacity-70 transition-opacity"
                    style={{ color: "var(--color-muted)" }}
                    aria-label={
                      showNewPassword ? "Hide password" : "Show password"
                    }
                  >
                    {showNewPassword ? <EyeOff size={18} /> : <Eye size={18} />}
                  </button>
                </div>
              </div>

              <div className="flex flex-col gap-1.5">
                <label htmlFor="confirm-new-password" className={labelCls}>
                  Confirm new password
                </label>
                <div className="relative">
                  <Lock
                    size={18}
                    className="absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none"
                    style={{ color: "var(--color-text)" }}
                  />
                  <input
                    id="confirm-new-password"
                    type={showNewPassword ? "text" : "password"}
                    autoComplete="new-password"
                    value={confirmNewPassword}
                    onChange={(e) => setConfirmNewPassword(e.target.value)}
                    disabled={loading}
                    placeholder="••••••••••••"
                    className={inputCls}
                    style={inputStyle}
                    required
                  />
                </div>
                <p className="text-xs mt-1" style={{ color: "var(--color-muted)" }}>
                  {PASSWORD_POLICY_HINT}
                </p>
              </div>

              <button
                type="submit"
                disabled={
                  loading || !newPassword || !confirmNewPassword
                }
                className="mt-2 py-3 rounded-lg font-medium transition disabled:opacity-50 min-h-[44px] active:scale-[0.98] flex items-center justify-center gap-2"
                style={{
                  backgroundColor: "var(--color-primary)",
                  color: "var(--color-primary-foreground)",
                }}
              >
                {loading ? (
                  <>
                    <Loader2 size={18} className="animate-spin" />
                    Updating...
                  </>
                ) : (
                  "Set new password"
                )}
              </button>

              <button
                type="button"
                onClick={backToCredentials}
                disabled={loading}
                className="text-sm flex items-center justify-center gap-1.5 min-h-[44px] transition hover:opacity-80 disabled:opacity-50"
                style={{ color: "var(--color-muted)" }}
              >
                <ArrowLeft size={14} /> Back to login
              </button>
            </form>
          )}

          {/* Helper text (only on credentials step) */}
          {step === "credentials" && (
            <div
              className="mt-8 pt-6"
              style={{ borderTop: "1px solid var(--color-border)" }}
            >
              <p
                className="text-xs text-center leading-relaxed"
                style={{ color: "var(--color-text)", opacity: 0.6 }}
              >
                Authentication is routed by your email domain.
                <br />
                Contact your site admin if you cannot log in.
              </p>
            </div>
          )}

          {/* Mobile footer */}
          <div className="lg:hidden mt-6 text-center">
            <p className="text-xs" style={{ color: "var(--color-text)", opacity: 0.5 }}>
              © 2026 Syntera. One Platform. One Standard.
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
