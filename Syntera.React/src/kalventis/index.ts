/**
 * kalventis-ui (in-tree port) — public re-exports.
 *
 * Source: copied from github.com/sebastianbelmero/kalventis-ui (v2.2.3)
 * into this Syntera.React/src/kalventis/ folder as an in-tree module
 * (no external npm dependency). Adapted to consume Syntera's
 * authStore + api client instead of kalventis's decoupled
 * TokenProvider/apiClient.
 *
 * What's NOT here (use Syntera's own equivalents):
 *   - Layout components → Syntera's src/components/layout/*
 *   - themeStore / filterStore → Syntera's src/store/*
 *   - apiClient / TokenProvider → Syntera's src/api/client + authStore
 *
 * What IS here:
 *   - AppGrid + Column + AppDynamicForm (the data grid system)
 *   - grid/* helpers (ColumnChooser, FilterOperator, HeaderFilter,
 *     HighlightText, gridUtils)
 *   - Modal + Drawer (animated + responsive dialogs)
 *   - All Radix-based UI primitives (Button, Input, Select,
 *     DropdownMenu, Tooltip, Avatar, Card, Switch, Checkbox,
 *     Calendar, Popover, Dialog, Sheet, Tabs, Skeleton, Toaster)
 *   - useDevExtremeData hook (wired to Syntera's authStore)
 *   - DevExtremeAdapter + buildDevExtremeQuery
 *   - tokens (legacy JS design tokens — kept for reference)
 */

// ─── Core: DevExtreme adapter ───────────────────────────────────
export { buildDevExtremeQuery } from "./core/DevExtremeAdapter";
export type { DevExtremeLazyState } from "./core/DevExtremeAdapter";

// ─── Hooks ──────────────────────────────────────────────────────
export { useDevExtremeData } from "./hooks/useDevExtremeData";
export type { UseDevExtremeDataOptions } from "./hooks/useDevExtremeData";

// ─── UI Components ───────────────────────────────────────────────
export * from "./ui";
