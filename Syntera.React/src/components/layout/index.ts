/**
 * Layout barrel — Syntera.React admin shell components.
 *
 * Components:
 *   - AdminLayout  — top-level shell (header + sidebar + <Outlet/>)
 *   - AppHeader    — sticky top bar with theme toggle + user menu
 *   - AppSidebar   — collapsible navigation rail
 */
export { AdminLayout } from "./AdminLayout";
export type { AdminLayoutProps } from "./AdminLayout";
export { AppHeader } from "./AppHeader";
export type { AppHeaderProps } from "./AppHeader";
export { AppSidebar } from "./AppSidebar";
export type { MenuItem, AppSidebarProps } from "./AppSidebar";
