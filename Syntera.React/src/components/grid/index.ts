/**
 * AppGrid — Syntera's declarative data grid system.
 *
 * A thin, in-house layer over `@tanstack/react-table` v8 with a
 * DevExtreme-compatible server protocol:
 *
 *   - `<AppGrid>` — the grid itself (toolbar, desktop table, mobile card
 *     view, pagination, master-detail, built-in CRUD with auto-rendered
 *     forms, state persistence via localStorage).
 *   - `<Column>` — declarative column schema (returns null, like
 *     DevExtreme's Column). The SAME schema drives the table cells AND
 *     the auto-generated `AppDynamicForm` editors.
 *   - `HeaderFilterDropdown` / `FilterOperatorDropdown` /
 *     `ColumnChooserDropdown` / `HighlightText` — grid chrome.
 *   - `buildTanstackToLazyState` — TanStack state → DevExtreme payload.
 *
 * Server communication runs through `useDevExtremeData` (src/hooks) and
 * the query builder in `src/lib/devextreme.ts`.
 *
 * Provenance: originally adapted from the kalventis-ui v2.2.3 design
 * (github.com/sebastianbelmero/kalventis-ui); now owned, adapted and
 * maintained in-tree by Syntera.React. It is brand-neutral — all visual
 * tokens come from the active theme palette in `src/index.css`, so all
 * 7 Syntera brand themes re-skin it automatically.
 */

export { AppGrid, type AppGridProps } from "./AppGrid";
export { Column, type ColumnProps } from "./Column";
export { AppDynamicForm, type AppDynamicFormProps } from "./AppDynamicForm";
export { HighlightText, type HighlightTextProps } from "./HighlightText";
export {
  FilterOperatorDropdown,
  type FilterOperatorDropdownProps,
} from "./FilterOperatorDropdown";
export {
  ColumnChooserDropdown,
  type ColumnChooserDropdownProps,
} from "./ColumnChooserDropdown";
export {
  HeaderFilterDropdown,
  type HeaderFilterDropdownProps,
} from "./HeaderFilterDropdown";
export { buildTanstackToLazyState } from "./gridUtils";
