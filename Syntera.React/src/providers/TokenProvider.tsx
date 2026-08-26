import { createContext, useContext, type ReactNode } from "react";

/**
 * TokenProvider — decouples the API client from any specific auth
 * implementation.
 *
 * The consumer (main.tsx) injects a `getToken` callback that reads
 * from whichever store holds the access token (in our case the
 * Zustand auth store). The API client reads it via `useToken()` so
 * it doesn't need to know about Zustand, localStorage, cookies, etc.
 *
 * If no TokenProvider wraps the app, `useToken().getToken()` returns
 * null and requests go out without an Authorization header (anonymous
 * mode — useful for public pages or unit tests).
 */
export interface TokenContextValue {
  /** Returns the current access token, or null if not authenticated. */
  getToken: () => string | null;
}

const defaultContext: TokenContextValue = {
  getToken: () => null,
};

const TokenContext = createContext<TokenContextValue>(defaultContext);

export interface TokenProviderProps extends TokenContextValue {
  children: ReactNode;
}

export function TokenProvider({ getToken, children }: TokenProviderProps) {
  return (
    <TokenContext.Provider value={{ getToken }}>
      {children}
    </TokenContext.Provider>
  );
}

export function useToken(): TokenContextValue {
  return useContext(TokenContext);
}
