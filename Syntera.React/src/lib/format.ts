/**
 * Currency + date formatters used across pages. Centralised so a
 * future i18n switch (e.g. EN → ID for any locale) changes a single
 * file rather than every component.
 */

export const formatIDR = (amount: number): string =>
  new Intl.NumberFormat("id-ID", {
    style: "currency",
    currency: "IDR",
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
  }).format(amount ?? 0);

export const formatNumber = (value: number, decimals = 0): string =>
  new Intl.NumberFormat("id-ID", {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  }).format(value ?? 0);

export const formatDate = (iso?: string | null): string =>
  !iso
    ? "—"
    : new Date(iso).toLocaleDateString("id-ID", {
        day: "2-digit",
        month: "short",
        year: "numeric",
      });

export const formatDateTime = (iso?: string | null): string =>
  !iso
    ? "—"
    : new Date(iso).toLocaleString("id-ID", {
        day: "2-digit",
        month: "short",
        year: "numeric",
        hour: "2-digit",
        minute: "2-digit",
      });

/** Returns the number of days until expiry (negative = expired). */
export const daysUntil = (iso?: string | null): number | null => {
  if (!iso) return null;
  const ms = new Date(iso).getTime() - Date.now();
  return Math.round(ms / 86_400_000);
};
