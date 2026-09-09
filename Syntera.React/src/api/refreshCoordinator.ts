/**
 * Refresh coordinator — THE single place where the silent refresh runs.
 *
 * WHY THIS EXISTS (F5-logout postmortem, 2026-09):
 *   The app previously had THREE independent code paths that could POST
 *   /api/auth/refresh[-site] at the same moment:
 *     1. initAuth() on app boot (main.tsx, via the axios `api` instance),
 *     2. acquireFreshAccessToken() in the 401 interceptor (raw axios),
 *     3. refresh() in api/auth.ts (raw axios).
 *   None of them shared a single-flight promise, and NOTHING serialized
 *   refreshes across BROWSER TABS.
 *
 *   With cookie-only transport this is dangerous: the refresh token rotates
 *   on every use, and the server's reuse detection treats "a rotated token
 *   presented again" as THEFT — it revokes the entire token family. Two
 *   concurrent refreshes presenting the SAME cookie (two tabs F5-ing at the
 *   same time, or a boot refresh racing an interceptor refresh) kill BOTH
 *   sessions: the loser triggers REFRESH_REUSE_DETECTED and the winner's
 *   rotated token dies with the family. Symptom: "logged out on refresh".
 *
 *   This module fixes it with two layers:
 *     - Single-flight (module-level promise): all in-tab callers share one
 *       in-flight refresh; the first caller's promise settles them all.
 *     - Web Locks API (navigator.locks, Chrome/Edge 69+, Firefox 96+,
 *       Safari 16.4+): refreshes are serialized ACROSS TABS. Tab B waits for
 *       tab A's rotation to land in the shared cookie jar before dispatching
 *       its own request — so it presents the ROTATED cookie, not the stale
 *       one, and the family survives. Browsers without Web Locks fall back
 *       to the in-tab single-flight only (the pre-existing behavior).
 *
 * TRANSPORT (H7 cookie-only): the refresh token travels exclusively in the
 * httpOnly `syntera_refresh` cookie — this module never reads, stores, or
 * sends a token value. The browser attaches the cookie automatically
 * (withCredentials: true). The rotated token comes back via Set-Cookie and
 * lands straight in the cookie jar; nothing is persisted client-side.
 */

import axios from "axios";
import type { ApiResponse, RefreshResponse } from "../types";
import { useAuthStore } from "../store/authStore";

const REFRESH_LOCK_NAME = "syntera.auth.refresh";

/** Minimal structural type for the Web Locks API (avoids lib-dom version drift). */
interface LockManagerLike {
  request<R>(name: string, callback: () => Promise<R>): Promise<R>;
}

let inFlight: Promise<RefreshResponse> | null = null;

/**
 * Run (or join) the single silent refresh. All callers — app boot
 * (initAuth), the 401 interceptor, and the auth API's refresh() — funnel
 * through here so at most ONE refresh request per tab (and, with Web Locks,
 * per browser) is ever in flight.
 */
export function silentRefresh(): Promise<RefreshResponse> {
  if (inFlight) return inFlight;
  inFlight = runSilentRefresh().finally(() => {
    inFlight = null;
  });
  return inFlight;
}

async function runSilentRefresh(): Promise<RefreshResponse> {
  // Choose the endpoint by scope. After a page reload the in-memory profile
  // is empty → /api/auth/refresh, whose multi-site scan finds site tokens
  // too. With a profile in memory (401 interceptor) a site user goes to
  // /api/auth/refresh-site with siteId (not secret — the cookie authorizes).
  const { profile } = useAuthStore.getState();
  const siteId = profile?.scope === "site" ? profile.siteId : undefined;
  const url = siteId ? "/api/auth/refresh-site" : "/api/auth/refresh";
  const body = siteId ? { siteId } : {};

  const call = async (): Promise<RefreshResponse> => {
    // Raw axios on purpose: NOT the `api` instance — its 401 interceptor
    // would try to refresh on refresh failures and loop. This call carries
    // no Bearer token ([AllowAnonymous] endpoint); the cookie is the
    // credential and is attached by the browser.
    const res = await axios.post<ApiResponse<RefreshResponse>>(url, body, {
      withCredentials: true,
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      timeout: 30_000,
    });
    const data = res.data?.data;
    if (!res.data?.success || !data?.accessToken) {
      throw new Error("REFRESH_FAILED");
    }
    return data;
  };

  // Cross-tab serialization: hold the lock for the full round-trip so a
  // concurrent tab waits, then dispatches with the freshly rotated cookie.
  const locks = (navigator as Navigator & { locks?: LockManagerLike }).locks;
  const data = locks
    ? await locks.request(REFRESH_LOCK_NAME, call)
    : await call();

  // Commit to the in-memory store. The rotated refresh token is NOT stored
  // anywhere client-side — it lives in the httpOnly cookie only (Set-Cookie
  // already updated the shared cookie jar).
  const store = useAuthStore.getState();
  store.setTokens({ accessToken: data.accessToken, expiresAt: data.expiresAt });
  if (data.profile) store.updateProfile(data.profile);
  if (data.theme) store.updateTheme(data.theme);
  return data;
}
