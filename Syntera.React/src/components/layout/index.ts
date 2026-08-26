/**
 * Layout barrel — Syntera.React owns its own admin shell.
 *
 * Components:
 *   - AdminLayout   — top-level shell (header + sidebar + <Outlet/>)
 *   - AppHeader     — sticky top bar with theme toggle + user menu
 *   - AppSidebar    — collapsible navigation rail with optional groups
 *   - AppBreadcrumb — simple path-aware breadcrumb (optional use)
 */
export { AdminLayout } from "./AdminLayout";
export type { AdminLayoutProps } from "./AdminLayout";
export { AppHeader } from "./AppHeader";
export type { AppHeaderProps } from "./AppHeader";
export { AppSidebar } from "./AppSidebar";
export type { MenuItem, AppSidebarProps } from "./AppSidebar";
export { AppBreadcrumb } from "./AppBreadcrumb";
export type { AppBreadcrumbProps } from "./AppBreadcrumb";
