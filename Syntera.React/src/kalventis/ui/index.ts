// ─── Existing components (refactored for v2.0) ──────────────
export { Badge, badgeVariants } from "./Badge";
export type { BadgeProps, BadgeVariant } from "./Badge";

export { Column } from "./Column";
export type { ColumnProps } from "./Column";

export { Modal } from "./Modal";
export type { ModalProps } from "./Modal";

export { Drawer } from "./Drawer";
export type { DrawerProps } from "./Drawer";

export { PageHeader } from "./PageHeader";
export type { PageHeaderProps } from "./PageHeader";

export { AppGrid } from "./AppGrid";
export type { AppGridProps } from "./AppGrid";

export { AppDynamicForm } from "./AppDynamicForm";
export type { AppDynamicFormProps } from "./AppDynamicForm";

export {
  HighlightText,
  FilterOperatorDropdown,
  ColumnChooserDropdown,
  HeaderFilterDropdown,
  buildTanstackToLazyState,
} from "./grid";

// ─── v2.0 Primitives (Radix UI + lucide-react) ──────────────
export * from "./primitives";
