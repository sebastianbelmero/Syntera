import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router-dom";
import { Toaster } from "sonner";

import "./index.css";
import App from "./App.tsx";

// ─── TanStack Query — single client, 1-min stale time to avoid
// hammering the API on focus changes; mutations invalidated manually
// via `queryClient.invalidateQueries(...)` after writes. ─────────
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: (failureCount, error) => {
        // Don't retry on 4xx — they won't magically succeed.
        if (error && typeof error === "object" && "status" in error) {
          const status = (error as { status: number }).status;
          if (status >= 400 && status < 500) return false;
        }
        return failureCount < 3;
      },
      staleTime: 60_000,
      refetchOnWindowFocus: false,
    },
  },
});

// Top-level mount. The Toaster lives here so any route can fire a
// toast (e.g. 401-refresh-failure → /login redirect). BrowserRouter
// gives us SPA navigation; the api client reads the access token
// directly from the Zustand auth store (see api/client.ts).
createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <App />
        <Toaster position="top-right" richColors closeButton />
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
