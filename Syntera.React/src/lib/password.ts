/**
 * Password-policy helpers shared by the LoginPage (forced-change flow)
 * and SettingsPage (Change Password section).
 *
 * Mirrors the backend's `PasswordPolicy` (Syntera.Backend):
 *   - length 12–256
 *   - ≥1 uppercase letter (A–Z)
 *   - ≥1 lowercase letter (a–z)
 *   - ≥1 digit (0–9)
 *   - ≥1 symbol (anything not in the above categories)
 *
 * The backend is the source of truth — these checks are a UX fast-fail
 * so the user gets immediate feedback without a roundtrip. Any password
 * that passes here will still be re-validated server-side, and the
 * server may additionally enforce reuse/history rules that we cannot
 * check client-side (no access to historical hashes).
 */

export interface PasswordValidationResult {
  ok: boolean;
  /** Human-readable message safe to surface in a toast. Empty string when `ok === true`. */
  message: string;
}

const MIN_LEN = 12;
const MAX_LEN = 256;

/**
 * Validate a single password against the policy.
 *
 * @returns `{ ok: true, message: "" }` on success, otherwise a
 *          `{ ok: false, message }` describing the first failed rule.
 */
export function validatePassword(password: string): PasswordValidationResult {
  if (!password) {
    return { ok: false, message: "Password is required." };
  }
  if (password.length < MIN_LEN) {
    return {
      ok: false,
      message: `Password must be at least ${MIN_LEN} characters long.`,
    };
  }
  if (password.length > MAX_LEN) {
    return {
      ok: false,
      message: `Password must be at most ${MAX_LEN} characters long.`,
    };
  }
  if (!/[A-Z]/.test(password)) {
    return { ok: false, message: "Password must include an uppercase letter." };
  }
  if (!/[a-z]/.test(password)) {
    return { ok: false, message: "Password must include a lowercase letter." };
  }
  if (!/[0-9]/.test(password)) {
    return { ok: false, message: "Password must include a digit." };
  }
  // Symbol = anything that's not a letter or digit. Whitespace counts
  // (and the backend rejects it), but we'd rather surface the symbol
  // requirement than the whitespace rule.
  if (!/[^A-Za-z0-9]/.test(password)) {
    return { ok: false, message: "Password must include a symbol." };
  }
  return { ok: true, message: "" };
}

/**
 * Convenience wrapper: validate two password fields (new + confirm)
 * and return the first error, or `null` if both pass.
 */
export function validatePasswordPair(
  newPassword: string,
  confirmPassword: string,
): PasswordValidationResult {
  const policy = validatePassword(newPassword);
  if (!policy.ok) return policy;
  if (newPassword !== confirmPassword) {
    return { ok: false, message: "Passwords do not match." };
  }
  return { ok: true, message: "" };
}

/** Tiny rule list for inline UI hints ("Must be 12+ chars…"). */
export const PASSWORD_POLICY_HINT =
  `12–256 chars, with at least one uppercase letter, one lowercase letter, ` +
  `one digit, and one symbol.`;
