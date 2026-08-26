import { useState, useCallback, useMemo, useRef, useEffect } from "react";
import { createStore } from "devextreme-aspnet-data-nojquery";
import { buildDevExtremeQuery, type DevExtremeLazyState } from "../lib/devextreme";
import { useAuthStore } from "../store/authStore";

export interface UseDevExtremeDataOptions {
  endpoint: string;
  /**
   * HTTP method for load requests. Default: "POST".
   * "POST" sends load options as JSON body (recommended for ASP.NET Core).
   * "GET" sends load options as query string (default DevExtreme behavior,
   *   but requires custom model binder in ASP.NET Core).
   */
  loadMethod?: "GET" | "POST";
  /**
   * Custom function to get access token.
   * Default: reads from Syntera's useAuthStore.
   * Override only if you need a different token source per-grid.
   */
  getAccessToken?: () => string | null;
}

/**
 * Custom hook for fetching data with DevExtreme ASP.NET Data protocol.
 *
 * Token resolution wires directly into Syntera's authStore
 * (Zustand-persisted access token).
 *
 * The store calls the Syntera backend's `/api/{entity}/grid` endpoints,
 * which accept `[DataSourceRequest] DataSourceLoadOptions` and return
 * the raw DevExtreme response shape `{ data, totalCount, ... }`.
 */
export const useDevExtremeData = <T = any>(options: UseDevExtremeDataOptions) => {
  const { endpoint, loadMethod = "POST", getAccessToken } = options;

  const [data, setData] = useState<T[]>([]);
  const [totalRecords, setTotalRecords] = useState(0);
  const [loading, setLoading] = useState(false);
  const extraParamsRef = useRef<Record<string, any>>({});
  const dataLengthRef = useRef(0);

  // Resolve token from Syntera's authStore — kept in a ref so the
  // recreated store (on endpoint change) picks up the latest token.
  const getAccessTokenRef = useRef(getAccessToken);
  useEffect(() => {
    getAccessTokenRef.current = getAccessToken;
  }, [getAccessToken]);

  const resolveToken = useCallback((): string | null => {
    if (getAccessTokenRef.current) {
      const t = getAccessTokenRef.current();
      if (t) return t;
    }
    // Fall through to Syntera's authStore
    return useAuthStore.getState().accessToken;
  }, []);

  const store = useMemo(() => {
    // oxlint-disable-next-line react/refs -- extraParamsRef is only read inside the onBeforeSend callback, which DevExtreme invokes while performing the ajax request (never during render).
    return createStore({
      key: "id",
      loadUrl: endpoint,
      loadMethod,
      onBeforeSend: (method, ajaxOptions) => {
        const token = resolveToken();

        ajaxOptions.headers = {
          ...ajaxOptions.headers,
          "Content-Type": "application/json",
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        };

        // Inject custom query parameters (for GET method)
        if (method === "load" && extraParamsRef.current && ajaxOptions.url) {
          const url = new URL(ajaxOptions.url, window.location.origin);
          Object.keys(extraParamsRef.current).forEach((key) => {
            url.searchParams.append(key, extraParamsRef.current[key]);
          });
          ajaxOptions.url = ajaxOptions.url.startsWith("http")
            ? url.toString()
            : url.pathname + url.search;
        }
      },
    });
  }, [endpoint, loadMethod, resolveToken]);

  const loadData = useCallback(
    async (event: DevExtremeLazyState, appendMode: boolean = false) => {
      setLoading(true);
      try {
        const rawOptions = buildDevExtremeQuery(event);

        if (rawOptions.customQueryParams) {
          extraParamsRef.current = rawOptions.customQueryParams;
        } else {
          extraParamsRef.current = {};
        }

        await new Promise<void>((resolve, reject) => {
          store
            .load(rawOptions)
            .then(
              (resultData: any, extra: any) => {
                setData((prev) => {
                  const newData = appendMode
                    ? [...prev, ...resultData]
                    : resultData;
                  dataLengthRef.current = newData.length;
                  return newData;
                });

                setTotalRecords(
                  extra && extra.totalCount !== undefined
                    ? extra.totalCount
                    : appendMode
                    ? dataLengthRef.current + resultData.length
                    : resultData.length
                );

                resolve();
              },
              (err: any) => {
                reject(err);
              }
            );
        });
      } catch (error) {
        console.error("Failed fetching data:", error);
      } finally {
        setLoading(false);
      }
    },
    [store]
  );

  return { data, totalRecords, loading, loadData };
}
