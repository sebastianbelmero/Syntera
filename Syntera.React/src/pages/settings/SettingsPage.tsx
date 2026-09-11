import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { toast } from "sonner";
import { KeyRound, Moon, Shield, Sun, User, Loader2, Copy, Check, ExternalLink, Eye, EyeOff, History, CheckCircle2, XCircle } from "lucide-react";
import { useAuthStore } from "../../store/authStore";
import { useThemeStore } from "../../store/themeStore";
import {
  changePassword as changePasswordApi,
  confirmMfa as confirmMfaApi,
  disableMfa as disableMfaApi,
  setupMfa as setupMfaApi,
} from "../../api/auth";
import { auditApi } from "../../api/audit";
import { ApiError } from "../../api/client";
import type { MfaSetupResponse, AuditLogDto } from "../../types";
import {
  PASSWORD_POLICY_HINT,
  validatePasswordPair,
} from "../../lib/password";

/**
 * Settings — Profile, Appearance, Session, plus Sprint 3.2 additions:
 *   • Multi-Factor Authentication (TOTP setup / disable)
 *   • Change Password (Platform Admin only)
 *
 * The MFA section tracks local state because `UserProfile` does not yet
 * expose `mfaEnabled` — once the user enables MFA in this session, we
 * flip a local flag so the "Disable" UI becomes available. A page reload
 * will reset that flag (acceptable per the task spec; future work is to
 * surface `mfaEnabled` from the backend in `UserProfile`).
 *
 * The plaintext TOTP secret returned by `/api/auth/mfa/setup` is held in
 * component state ONLY — never logged, never persisted, and cleared on
 * unmount or when the user cancels setup.
 */
export default function SettingsPage() {
  const profile = useAuthStore((s) => s.profile);
  const theme = useAuthStore((s) => s.theme);
  const { isDark, toggleMode } = useThemeStore();

  if (!profile) return null;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold">Settings</h1>
        <p className="text-sm" style={{ color: "var(--color-muted)" }}>
          User profile, theme preferences, session info, and security settings.
        </p>
      </div>

      {/* Profile */}
      <section
        className="rounded-xl p-6"
        style={{
          backgroundColor: "var(--color-surface)",
          border: "1px solid var(--color-border)",
        }}
      >
        <h3 className="mb-4 flex items-center gap-2 text-lg font-semibold">
          <User size={18} /> Profile
        </h3>
        <dl className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <Field label="Display Name" value={profile.displayName} />
          <Field label="Email" value={profile.email} />
          <Field
            label="Scope"
            value={
              profile.scope === "platform"
                ? "Platform Admin"
                : profile.siteCode ?? "Site User"
            }
          />
          {profile.siteDisplayName && (
            <Field label="Site" value={profile.siteDisplayName} />
          )}
          <Field label="Roles" value={profile.roles.join(", ") || "—"} />
          <Field
            label="Permissions"
            value={`${profile.permissions.length} keys`}
          />
        </dl>
      </section>

      {/* Theme */}
      <section
        className="rounded-xl p-6"
        style={{
          backgroundColor: "var(--color-surface)",
          border: "1px solid var(--color-border)",
        }}
      >
        <h3 className="mb-4 flex items-center gap-2 text-lg font-semibold">
          <Sun size={18} /> Appearance
        </h3>
        <div className="flex items-center justify-between">
          <div>
            <div className="text-sm font-medium flex items-center gap-2">
              {isDark ? <Moon size={14} /> : <Sun size={14} />}
              {isDark ? "Dark Mode" : "Light Mode"}
            </div>
            <div className="text-xs mt-0.5" style={{ color: "var(--color-muted)" }}>
              {theme ? `Brand palette: ${theme.themeKey}` : "Default palette"}
            </div>
          </div>
          <button
            type="button"
            onClick={toggleMode}
            className="px-3 py-2.5 min-h-[44px] rounded-lg text-sm"
            style={{ border: "1px solid var(--color-border)" }}
          >
            Switch to {isDark ? "Light" : "Dark"}
          </button>
        </div>
        <p className="text-xs mt-3" style={{ color: "var(--color-muted)" }}>
          The brand palette is determined by your site (set by the Platform Admin).
          You can override light/dark mode here — preference is saved per-browser.
        </p>
      </section>

      {/* Session */}
      <section
        className="rounded-xl p-6"
        style={{
          backgroundColor: "var(--color-surface)",
          border: "1px solid var(--color-border)",
        }}
      >
        <h3 className="mb-4 flex items-center gap-2 text-lg font-semibold">
          <Shield size={18} /> Session
        </h3>
        <p className="text-sm" style={{ color: "var(--color-muted)" }}>
          Your session is managed by a short-lived JWT (15 minutes) backed by a rotating
          refresh token (24 hours). All authentication events are recorded in the audit log.
        </p>
      </section>

      {/* ─── My Activity — own audit events (audit.read holders) ───
          Keeps Settings useful for SITE users: MFA and Change Password are
          platform-only (site users authenticate via LDAP), so without this
          section their Settings page was nearly empty. Technicians don't
          hold audit.read, so the section hides itself for them. */}
      <MyActivitySection />

      {/* ─── Sprint 3.2: Multi-Factor Authentication ─── */}
      <MfaSection />

      {/* ─── Sprint 3.2: Change Password (Platform Admin only) ─── */}
      {profile.scope === "platform" && <ChangePasswordSection />}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────
// MFA Section
// ─────────────────────────────────────────────────────────────

type MfaStage = "idle" | "setup" | "disable";

function MfaSection() {
  // We don't get `mfaEnabled` from the backend (not in UserProfile yet).
  // Track locally so the "Disable" UI becomes available after the user
  // enables MFA in this session. Page reload resets this to false.
  const [mfaEnabled, setMfaEnabled] = useState(false);
  const [stage, setStage] = useState<MfaStage>("idle");
  const [setupData, setSetupData] = useState<MfaSetupResponse | null>(null);
  const [setupLoading, setSetupLoading] = useState(false);
  const [confirmCode, setConfirmCode] = useState("");
  const [confirmLoading, setConfirmLoading] = useState(false);
  const [disableCode, setDisableCode] = useState("");
  const [disableLoading, setDisableLoading] = useState(false);
  const [copied, setCopied] = useState<"secret" | "url" | null>(null);

  // SECURITY: clear any plaintext secret from state when the user
  // cancels setup or navigates away (component unmount).
  useEffect(() => {
    return () => {
      setSetupData(null);
      setConfirmCode("");
      setDisableCode("");
    };
  }, []);

  const cancelSetup = () => {
    setStage("idle");
    setSetupData(null);
    setConfirmCode("");
  };

  const cancelDisable = () => {
    setStage("idle");
    setDisableCode("");
  };

  const handleEnableClick = async () => {
    setSetupLoading(true);
    try {
      const data = await setupMfaApi();
      setSetupData(data);
      setConfirmCode("");
      setStage("setup");
    } catch (err) {
      if (err instanceof ApiError) {
        toast.error(err.message || "Failed to start MFA setup.");
      } else {
        toast.error("Failed to start MFA setup.");
      }
    } finally {
      setSetupLoading(false);
    }
  };

  const handleConfirmSetup = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!/^\d{6}$/.test(confirmCode)) {
      toast.error("Enter the 6-digit code from your authenticator app.");
      return;
    }
    setConfirmLoading(true);
    try {
      await confirmMfaApi(confirmCode);
      toast.success(
        "MFA enabled. You will need to enter a code from your authenticator app on every login.",
      );
      setMfaEnabled(true);
      setStage("idle");
      setSetupData(null);
      setConfirmCode("");
    } catch (err) {
      if (err instanceof ApiError) {
        toast.error(err.message || "Invalid code. Please try again.");
      } else {
        toast.error("Invalid code. Please try again.");
      }
      setConfirmCode("");
    } finally {
      setConfirmLoading(false);
    }
  };

  const handleDisable = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!/^\d{6}$/.test(disableCode)) {
      toast.error("Enter the 6-digit code from your authenticator app.");
      return;
    }
    setDisableLoading(true);
    try {
      await disableMfaApi(disableCode);
      toast.success("MFA disabled.");
      setMfaEnabled(false);
      setStage("idle");
      setDisableCode("");
    } catch (err) {
      if (err instanceof ApiError) {
        toast.error(err.message || "Invalid code. Please try again.");
      } else {
        toast.error("Invalid code. Please try again.");
      }
      setDisableCode("");
    } finally {
      setDisableLoading(false);
    }
  };

  const copyToClipboard = async (text: string, which: "secret" | "url") => {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(which);
      setTimeout(() => setCopied(null), 1500);
    } catch {
      // Clipboard may be blocked (e.g. insecure origin). Silently fail —
      // the user can still select the text manually.
      toast.message("Copy failed — please select the text manually.");
    }
  };

  const codeInputCls =
    "w-full pl-10 pr-3 py-3 rounded-lg outline-none transition focus-visible:ring-2 focus-visible:ring-[var(--color-primary)] min-h-[44px] tracking-[0.35em] text-center font-mono text-lg";

  const inputStyle: React.CSSProperties = {
    backgroundColor: "var(--color-background)",
    border: "1px solid var(--color-border)",
    color: "var(--color-text)",
  };

  return (
    <section
      className="rounded-xl p-6"
      style={{
        backgroundColor: "var(--color-surface)",
        border: "1px solid var(--color-border)",
      }}
    >
      <h3 className="mb-4 flex items-center gap-2 text-lg font-semibold">
        <KeyRound size={18} /> Multi-Factor Authentication
      </h3>

      {/* ── Idle: status + action button ── */}
      {stage === "idle" && (
        <div className="flex items-center justify-between gap-4 flex-wrap">
          <div>
            <div className="text-sm font-medium flex items-center gap-2">
              <span
                className="w-2 h-2 rounded-full"
                style={{
                  backgroundColor: mfaEnabled
                    ? "var(--color-success)"
                    : "var(--color-muted)",
                }}
              />
              {mfaEnabled ? "Enabled" : "Not enabled"}
            </div>
            <p className="text-xs mt-1" style={{ color: "var(--color-muted)" }}>
              {mfaEnabled
                ? "You will be asked for a 6-digit code from your authenticator app on every login."
                : "Add an extra layer of security to your account using a TOTP authenticator app (Google Authenticator, Microsoft Authenticator, Authy, etc.)."}
            </p>
            {!mfaEnabled && (
              <p className="text-xs mt-1" style={{ color: "var(--color-muted)" }}>
                Status shown here is tracked per-session — reloading the page
                resets it to "Not enabled" until the backend exposes
                <code className="px-1"> mfaEnabled</code> in your profile.
              </p>
            )}
          </div>
          {mfaEnabled ? (
            <button
              type="button"
              onClick={() => {
                setDisableCode("");
                setStage("disable");
              }}
              className="px-3 py-2.5 min-h-[44px] rounded-lg text-sm font-medium transition hover:opacity-90"
              style={{
                border: "1px solid var(--color-border)",
                color: "var(--color-danger)",
              }}
            >
              Disable MFA
            </button>
          ) : (
            <button
              type="button"
              onClick={handleEnableClick}
              disabled={setupLoading}
              className="px-3 py-2.5 min-h-[44px] rounded-lg text-sm font-medium transition disabled:opacity-50 flex items-center gap-2"
              style={{
                backgroundColor: "var(--color-primary)",
                color: "var(--color-primary-foreground)",
              }}
            >
              {setupLoading ? (
                <>
                  <Loader2 size={14} className="animate-spin" />
                  Starting...
                </>
              ) : (
                "Enable MFA"
              )}
            </button>
          )}
        </div>
      )}

      {/* ── Setup stage: show QR URL + secret, then enter code ── */}
      {stage === "setup" && setupData && (
        <div className="space-y-4">
          <p className="text-sm" style={{ color: "var(--color-text)" }}>
            Scan this QR code with your authenticator app (Google
            Authenticator, Microsoft Authenticator, Authy, etc.). Then enter
            the 6-digit code shown in the app to confirm.
          </p>

          {/* TODO (Sprint 3.x): render the otpauth:// URL as an actual QR
              image once a tiny in-tree QR generator is available. For
              now we expose the URL as a clickable link + the plaintext
              secret for manual entry into the authenticator app. */}
          <div className="rounded-lg p-3" style={inputStyle}>
            <div className="text-xs font-medium uppercase tracking-wide mb-1" style={{ color: "var(--color-muted)" }}>
              Secret (manual entry)
            </div>
            <div className="flex items-center gap-2">
              <code
                className="font-mono text-sm break-all flex-1 select-all"
                style={{ color: "var(--color-text)" }}
              >
                {setupData.plaintextSecret}
              </code>
              <button
                type="button"
                onClick={() => copyToClipboard(setupData.plaintextSecret, "secret")}
                className="p-1.5 rounded transition hover:opacity-70"
                style={{ color: "var(--color-muted)" }}
                aria-label="Copy secret"
              >
                {copied === "secret" ? <Check size={14} /> : <Copy size={14} />}
              </button>
            </div>
          </div>

          <div className="rounded-lg p-3" style={inputStyle}>
            <div className="text-xs font-medium uppercase tracking-wide mb-1" style={{ color: "var(--color-muted)" }}>
              QR URL (paste into a QR generator)
            </div>
            <div className="flex items-center gap-2">
              <a
                href={setupData.qrCodeUrl}
                target="_blank"
                rel="noreferrer noopener"
                className="text-xs break-all underline flex-1"
                style={{ color: "var(--color-primary)" }}
              >
                {setupData.qrCodeUrl}
              </a>
              <ExternalLink
                size={14}
                style={{ color: "var(--color-muted)" }}
                className="shrink-0"
              />
              <button
                type="button"
                onClick={() => copyToClipboard(setupData.qrCodeUrl, "url")}
                className="p-1.5 rounded transition hover:opacity-70"
                style={{ color: "var(--color-muted)" }}
                aria-label="Copy QR URL"
              >
                {copied === "url" ? <Check size={14} /> : <Copy size={14} />}
              </button>
            </div>
          </div>

          <form onSubmit={handleConfirmSetup} className="flex flex-col gap-3">
            <div className="flex flex-col gap-1.5">
              <label htmlFor="mfa-confirm-code" className="text-sm font-medium">
                Verification code
              </label>
              <div className="relative">
                <KeyRound
                  size={18}
                  className="absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none"
                  style={{ color: "var(--color-text)" }}
                />
                <input
                  id="mfa-confirm-code"
                  type="text"
                  inputMode="numeric"
                  pattern="[0-9]*"
                  autoComplete="one-time-code"
                  maxLength={6}
                  value={confirmCode}
                  onChange={(e) =>
                    setConfirmCode(e.target.value.replace(/\D/g, "").slice(0, 6))
                  }
                  disabled={confirmLoading}
                  placeholder="123456"
                  className={codeInputCls}
                  style={inputStyle}
                  required
                />
              </div>
            </div>
            <div className="flex items-center gap-2">
              <button
                type="submit"
                disabled={confirmLoading || confirmCode.length !== 6}
                className="px-3 py-2.5 min-h-[44px] rounded-lg text-sm font-medium transition disabled:opacity-50 flex items-center gap-2"
                style={{
                  backgroundColor: "var(--color-primary)",
                  color: "var(--color-primary-foreground)",
                }}
              >
                {confirmLoading ? (
                  <>
                    <Loader2 size={14} className="animate-spin" />
                    Confirming...
                  </>
                ) : (
                  "Confirm and enable"
                )}
              </button>
              <button
                type="button"
                onClick={cancelSetup}
                disabled={confirmLoading}
                className="px-3 py-2.5 min-h-[44px] rounded-lg text-sm transition hover:opacity-80 disabled:opacity-50"
                style={{
                  border: "1px solid var(--color-border)",
                  color: "var(--color-text)",
                }}
              >
                Cancel
              </button>
            </div>
          </form>
        </div>
      )}

      {/* ── Disable stage: 6-digit code + confirm ── */}
      {stage === "disable" && (
        <form onSubmit={handleDisable} className="space-y-4">
          <p className="text-sm" style={{ color: "var(--color-text)" }}>
            Disabling MFA reduces your account's security. Enter a current
            6-digit code from your authenticator app to confirm.
          </p>
          <div className="flex flex-col gap-1.5">
            <label htmlFor="mfa-disable-code" className="text-sm font-medium">
              Authenticator code
            </label>
            <div className="relative">
              <KeyRound
                size={18}
                className="absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none"
                style={{ color: "var(--color-text)" }}
              />
              <input
                id="mfa-disable-code"
                type="text"
                inputMode="numeric"
                pattern="[0-9]*"
                autoComplete="one-time-code"
                maxLength={6}
                value={disableCode}
                onChange={(e) =>
                  setDisableCode(e.target.value.replace(/\D/g, "").slice(0, 6))
                }
                disabled={disableLoading}
                placeholder="123456"
                className={codeInputCls}
                style={inputStyle}
                required
              />
            </div>
          </div>
          <div className="flex items-center gap-2">
            <button
              type="submit"
              disabled={disableLoading || disableCode.length !== 6}
              className="px-3 py-2.5 min-h-[44px] rounded-lg text-sm font-medium transition disabled:opacity-50 flex items-center gap-2"
              style={{
                backgroundColor: "var(--color-danger)",
                color: "var(--color-primary-foreground)",
              }}
            >
              {disableLoading ? (
                <>
                  <Loader2 size={14} className="animate-spin" />
                  Disabling...
                </>
              ) : (
                "Confirm disable"
              )}
            </button>
            <button
              type="button"
              onClick={cancelDisable}
              disabled={disableLoading}
              className="px-3 py-2.5 min-h-[44px] rounded-lg text-sm transition hover:opacity-80 disabled:opacity-50"
              style={{
                border: "1px solid var(--color-border)",
                color: "var(--color-text)",
              }}
            >
              Cancel
            </button>
          </div>
        </form>
      )}
    </section>
  );
}

// ─────────────────────────────────────────────────────────────
// Change Password (Platform Admin only)
// ─────────────────────────────────────────────────────────────

function ChangePasswordSection() {
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [showCurrent, setShowCurrent] = useState(false);
  const [showNew, setShowNew] = useState(false);
  const [loading, setLoading] = useState(false);

  const inputStyle: React.CSSProperties = {
    backgroundColor: "var(--color-background)",
    border: "1px solid var(--color-border)",
    color: "var(--color-text)",
  };
  const baseInputCls =
    "w-full pl-10 pr-10 py-3 rounded-lg outline-none transition focus-visible:ring-2 focus-visible:ring-[var(--color-primary)] min-h-[44px]";

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!currentPassword) {
      toast.error("Enter your current password.");
      return;
    }
    const v = validatePasswordPair(newPassword, confirmPassword);
    if (!v.ok) {
      toast.error(v.message);
      return;
    }
    setLoading(true);
    try {
      await changePasswordApi({ currentPassword, newPassword });
      toast.success("Password changed. Future logins will use the new password.");
      setCurrentPassword("");
      setNewPassword("");
      setConfirmPassword("");
    } catch (err) {
      // Surface backend error messages verbatim — these carry the
      // canonical reason codes (WRONG_CURRENT_PASSWORD,
      // PASSWORD_POLICY_VIOLATION, PASSWORD_REUSE_FORBIDDEN,
      // PASSWORD_SAME_AS_CURRENT) wrapped in a user-readable message.
      if (err instanceof ApiError) {
        toast.error(err.message || "Failed to change password.");
      } else {
        toast.error("Failed to change password. Please try again.");
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <section
      className="rounded-xl p-6"
      style={{
        backgroundColor: "var(--color-surface)",
        border: "1px solid var(--color-border)",
      }}
    >
      <h3 className="mb-4 flex items-center gap-2 text-lg font-semibold">
        <KeyRound size={18} /> Change Password
      </h3>

      <form onSubmit={handleSubmit} className="space-y-4 max-w-md">
        <div className="flex flex-col gap-1.5">
          <label htmlFor="cur-pw" className="text-sm font-medium">
            Current password
          </label>
          <div className="relative">
            <KeyRound
              size={18}
              className="absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none"
              style={{ color: "var(--color-text)" }}
            />
            <input
              id="cur-pw"
              type={showCurrent ? "text" : "password"}
              autoComplete="current-password"
              value={currentPassword}
              onChange={(e) => setCurrentPassword(e.target.value)}
              disabled={loading}
              placeholder="••••••••"
              className={baseInputCls}
              style={inputStyle}
              required
            />
            <button
              type="button"
              onClick={() => setShowCurrent(!showCurrent)}
              className="absolute right-3 top-1/2 -translate-y-1/2 p-1 rounded hover:opacity-70 transition-opacity"
              style={{ color: "var(--color-muted)" }}
              aria-label={showCurrent ? "Hide password" : "Show password"}
            >
              {showCurrent ? <EyeOff size={16} /> : <Eye size={16} />}
            </button>
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <label htmlFor="new-pw" className="text-sm font-medium">
            New password
          </label>
          <div className="relative">
            <KeyRound
              size={18}
              className="absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none"
              style={{ color: "var(--color-text)" }}
            />
            <input
              id="new-pw"
              type={showNew ? "text" : "password"}
              autoComplete="new-password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              disabled={loading}
              placeholder="••••••••••••"
              className={baseInputCls}
              style={inputStyle}
              required
            />
            <button
              type="button"
              onClick={() => setShowNew(!showNew)}
              className="absolute right-3 top-1/2 -translate-y-1/2 p-1 rounded hover:opacity-70 transition-opacity"
              style={{ color: "var(--color-muted)" }}
              aria-label={showNew ? "Hide password" : "Show password"}
            >
              {showNew ? <EyeOff size={16} /> : <Eye size={16} />}
            </button>
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <label htmlFor="confirm-pw" className="text-sm font-medium">
            Confirm new password
          </label>
          <div className="relative">
            <KeyRound
              size={18}
              className="absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none"
              style={{ color: "var(--color-text)" }}
            />
            <input
              id="confirm-pw"
              type={showNew ? "text" : "password"}
              autoComplete="new-password"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              disabled={loading}
              placeholder="••••••••••••"
              className={baseInputCls}
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
          disabled={loading || !currentPassword || !newPassword || !confirmPassword}
          className="px-3 py-2.5 min-h-[44px] rounded-lg text-sm font-medium transition disabled:opacity-50 flex items-center gap-2"
          style={{
            backgroundColor: "var(--color-primary)",
            color: "var(--color-primary-foreground)",
          }}
        >
          {loading ? (
            <>
              <Loader2 size={14} className="animate-spin" />
              Changing...
            </>
          ) : (
            "Change password"
          )}
        </button>
      </form>
    </section>
  );
}

// ─────────────────────────────────────────────────────────────
// My Activity Section (Sprint FE-A5)
// ─────────────────────────────────────────────────────────────

/**
 * Shows the last 10 audit events performed BY the signed-in account.
 *
 * Authorization nuance: /api/audit/logs requires `audit.read`, so this
 * section renders only for users who hold it (Platform Admin bypasses,
 * mirroring the backend's HasPermission filter). The backend scopes the
 * query automatically — a site user only ever sees their own site's
 * events, filtered server-side to actorUserId = self.
 */
function MyActivitySection() {
  const profile = useAuthStore((s) => s.profile);
  const isPlatform = profile?.roles.includes("platform-admin") ?? false;
  const canRead = !!profile && (isPlatform || profile.permissions.includes("audit.read"));

  const { data: logs = [], isLoading } = useQuery<AuditLogDto[]>({
    queryKey: ["my-activity", profile?.userId],
    queryFn: () => auditApi.query({ actorUserId: profile!.userId, take: 10 }),
    enabled: canRead,
    retry: false,
  });

  if (!canRead) return null;

  return (
    <section
      className="rounded-xl p-6"
      style={{
        backgroundColor: "var(--color-surface)",
        border: "1px solid var(--color-border)",
      }}
    >
      <h3 className="mb-1 flex items-center gap-2 text-lg font-semibold">
        <History size={18} /> My Activity
      </h3>
      <p className="text-xs mb-4" style={{ color: "var(--color-muted)" }}>
        Your last 10 recorded actions from the immutable, hash-chained audit trail
        (21 CFR Part 11). Site users see their own site's events only.
      </p>
      {isLoading ? (
        <div className="text-sm" style={{ color: "var(--color-muted)" }}>
          Loading…
        </div>
      ) : logs.length === 0 ? (
        <div className="text-sm" style={{ color: "var(--color-muted)" }}>
          No recorded activity yet.
        </div>
      ) : (
        <ul className="m-0 list-none space-y-1 p-0">
          {logs.map((l) => (
            <li
              key={l.id}
              className="flex items-center gap-3 rounded-md p-2 text-sm"
              style={{ backgroundColor: "var(--color-background)" }}
            >
              {l.outcome === "success" ? (
                <CheckCircle2
                  size={14}
                  className="shrink-0"
                  aria-hidden
                  style={{ color: "var(--color-success)" }}
                />
              ) : (
                <XCircle
                  size={14}
                  className="shrink-0"
                  aria-hidden
                  style={{ color: "var(--color-danger)" }}
                />
              )}
              <span className="font-mono text-xs">{l.action}</span>
              <span
                className="ml-auto whitespace-nowrap text-xs"
                style={{ color: "var(--color-muted)" }}
              >
                {new Date(l.timestamp).toLocaleString()}
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

// ─────────────────────────────────────────────────────────────
// Small field helper (kept from the original SettingsPage)
// ─────────────────────────────────────────────────────────────
function Field({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt
        className="text-xs font-medium uppercase tracking-wide"
        style={{ color: "var(--color-muted)" }}
      >
        {label}
      </dt>
      <dd className="mt-1 text-sm font-medium">{value}</dd>
    </div>
  );
}
