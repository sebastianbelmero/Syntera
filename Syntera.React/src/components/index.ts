/**
 * Syntera.React shared components — public barrel.
 *
 *   - `./grid`  — the AppGrid data grid system (+ Column, AppDynamicForm)
 *   - `./Modal` / `./Drawer` — animated, focus-trapped overlays
 *   - `./ui`    — Radix-based UI primitives (Button, Badge, Select, ...)
 *   - `./layout` — app shell (re-exported for convenience)
 *
 * Pages import from here:
 *   import { AppGrid, Column, Modal, Badge, type ColumnProps } from "../../components";
 */
export * from "./grid";
export { Modal, type ModalProps } from "./Modal";
export { Drawer, type DrawerProps } from "./Drawer";
export * from "./ui";
export * from "./layout";
