/**
 * Auth API — login, refresh, current profile. Endpoints match the
 * backend AuthController (/api/auth/*).
 */

import { post } from "./client";
import type { LoginRequest, LoginResponse } from "../types";

export const authApi = {
  login: (req: LoginRequest) =>
    post<LoginResponse>("/auth/login", req),

  // Refresh is called inside the API client interceptor directly via
  // axios (not through `api`) — no need to expose it here.
};
