/**
 * Syntera API client — single axios instance for the entire SPA.
 *
 * Responsibilities:
 *   1. Inject Bearer token from the auth store on every request.
 *   2. Catch 401 responses and attempt a refresh-token exchange
 *      once; on success, replay the original request; on failure,
 *      clear the session and redirect to /login.
 *   3. Unwrap ApiResponse envelopes (success → data, failure → throw
 *      ApiError with field errors attached) so call sites can use
 *      try/catch without inspecting the wrapper every time.
 *
 * Design goals:
 *   - DRY: every endpoint goes through this single instance.
 *   - Safe (H7): refresh token lives in httpOnly cookie set by backend —
 *     JS cannot read it, XSS cannot exfiltrate it. `withCredentials: true`
 *     ensures the cookie is sent on cross-origin (Vite dev :5173 → API
 *     :5296) requests. Access token is short-lived (15 min) and stored in
 *     localStorage — blast radius of an XSS leak is bounded.
 *   - Transparent: a single request queue prevents refresh-token
 *     thundering herds when multiple requests 401 simultaneously.
 */

import axios, { AxiosError } from "axios";
import type {
  AxiosRequestConfig,
  AxiosResponse,
  InternalAxiosRequestConfig,
} from "axios";
import type {
  ApiResponse,
  FieldError,
} from "../types";
import { useAuthStore } from "../store/authStore";

export const api = axios.create({
  baseURL: "/api",
  // H7: must be true so the httpOnly refresh-token cookie set on /api/auth
  // is sent on every /api/* request (refresh + logout rely on it).
  withCredentials: true,
  headers: {
    "Content-Type": "application/json",
    Accept: "application/json",
  },
  timeout: 30_000,
});

// ── Request: attach access token ────────────────────────────
api.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = useAuthStore.getState().accessToken;
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// ── Response: unwrap envelope + handle 401 ──────────────────
let refreshPromise: Promise<string> | null = null;

api.interceptors.response.use(
  (response: AxiosResponse<ApiResponse<unknown>>) => {
    // 204 No Content — return empty payload.
    if (response.status === 204) {
      return { ...response, data: undefined } as AxiosResponse;
    }

    const payload = response.data;
    // Some endpoints (e.g. /health) don't use the ApiResponse envelope;
    // if we don't see `success`, return raw payload.
    if (!payload || typeof payload !== "object" || !("success" in payload)) {
      return response;
    }

    if (!payload.success) {
      throw new ApiError(
        payload.errorCode ?? "REQUEST_FAILED",
        payload.message ?? "Request failed.",
        payload.fieldErrors ?? [],
        response.status,
      );
    }

    // Mutate response.data to unwrap the inner `data` so call sites
    // can do `const products = await api.get<ProductDto[]>('/products')`.
    response.data = payload.data as never;
    return response;
  },
  async (error: AxiosError<ApiResponse<unknown>>) => {
    const originalRequest = error.config as
      | (InternalAxiosRequestConfig & { _retry?: boolean })
      | undefined;

    // Network / timeout errors — no envelope available.
    if (!error.response) {
      throw new ApiError("NETWORK_ERROR", error.message, [], 0);
    }

    // 401 → attempt refresh ONCE per request cycle.
    if (
      error.response.status === 401 &&
      originalRequest &&
      !originalRequest._retry &&
      !originalRequest.url?.includes("/auth/")
    ) {
      originalRequest._retry = true;
      try {
        const newToken = await acquireFreshAccessToken();
        originalRequest.headers = originalRequest.headers ?? {};
        originalRequest.headers.Authorization = `Bearer ${newToken}`;
        return api(originalRequest);
      } catch (refreshErr) {
        useAuthStore.getState().logout();
        if (typeof window !== "undefined") {
          window.location.href = "/login";
        }
        throw refreshErr;
      }
    }

    const payload = error.response.data;
    if (payload && typeof payload === "object" && "success" in payload) {
      throw new ApiError(
        payload.errorCode ?? "REQUEST_FAILED",
        payload.message ?? "Request failed.",
        payload.fieldErrors ?? [],
        error.response.status,
      );
    }

    throw new ApiError(
      "HTTP_ERROR",
      `HTTP ${error.response.status}`,
      [],
      error.response.status,
    );
  },
);

async function acquireFreshAccessToken(): Promise<string> {
  if (refreshPromise) return refreshPromise;

  const { profile } = useAuthStore.getState();

  // H7: refresh token is sent automatically by the browser as an httpOnly
  // cookie on /api/auth/* requests (withCredentials=true). We don't read
  // it from the auth store anymore — it's never stored client-side.
  //
  // Choose endpoint based on scope — site users need /auth/refresh-site
  // with siteId in body. Platform admin uses /auth/refresh (no body needed
  // beyond what the cookie carries).
  const isSiteUser = profile?.scope === "site" && profile.siteId;
  const url = isSiteUser ? "/api/auth/refresh-site" : "/api/auth/refresh";
  const body = isSiteUser ? { siteId: profile!.siteId } : {};

  refreshPromise = (async () => {
    try {
      const res = await axios.post<
        ApiResponse<{
          accessToken: string;
          refreshToken: string;
          expiresAt: string;
          profile: unknown;
          theme: unknown;
        }>
      >(url, body, { withCredentials: true });
      const data = res.data.data;
      if (!data) throw new Error("REFRESH_FAILED");
      useAuthStore.getState().setTokens({
        accessToken: data.accessToken,
        // H7: backend also returns refreshToken in body for backward compat,
        // but we deliberately don't store it — the cookie is rotated by the
        // backend's Set-Cookie header automatically.
        expiresAt: data.expiresAt,
      });
      if (data.theme) {
        useAuthStore.getState().updateTheme(data.theme as import("../types").ThemeBundle);
      }
      return data.accessToken;
    } finally {
      refreshPromise = null;
    }
  })();

  return refreshPromise;
}

export class ApiError extends Error {
  readonly code: string;
  readonly status: number;
  readonly fieldErrors: FieldError[];

  constructor(code: string, message: string, fieldErrors: FieldError[], status: number) {
    super(message);
    this.name = "ApiError";
    this.code = code;
    this.status = status;
    this.fieldErrors = fieldErrors;
  }

  isClientError() {
    return this.status >= 400 && this.status < 500;
  }

  isUnauthorized() {
    return this.status === 401;
  }

  isNotFound() {
    return this.status === 404;
  }

  isConflict() {
    return this.status === 409;
  }

  isRateLimited() {
    return this.status === 429;
  }
}

// ── Helper: typed GET/POST/PUT/PATCH/DELETE wrappers ─────────
// Keeps call sites ergonomic without losing type-safety.
export async function get<T>(url: string, config?: AxiosRequestConfig): Promise<T> {
  const res = await api.get<T>(url, config);
  return res.data;
}

export async function post<T>(url: string, body?: unknown, config?: AxiosRequestConfig): Promise<T> {
  const res = await api.post<T>(url, body, config);
  return res.data;
}

export async function put<T>(url: string, body?: unknown, config?: AxiosRequestConfig): Promise<T> {
  const res = await api.put<T>(url, body, config);
  return res.data;
}

export async function patch<T>(url: string, body?: unknown, config?: AxiosRequestConfig): Promise<T> {
  const res = await api.patch<T>(url, body, config);
  return res.data;
}

export async function del<T = void>(url: string, config?: AxiosRequestConfig): Promise<T> {
  const res = await api.delete<T>(url, config);
  return res.data;
}
