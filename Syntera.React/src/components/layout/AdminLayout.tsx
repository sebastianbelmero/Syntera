import * as React from "react";
import { Outlet } from "react-router-dom";
import { AppSidebar, type MenuItem } from "./AppSidebar";
import { AppHeader } from "./AppHeader";
import { useThemeStore } from "../../store/themeStore";

export interface AdminLayoutProps {
  title?: string;
  menuItems?: MenuItem[];
  user?: {
    name: string;
    role: string;
    email?: string;
    avatarUrl?: string;
  };
  onLogout?: () => void;
  logo?: React.ReactNode;
  /** When true, route content is rendered via <Outlet />. Default true. */
  withOutlet?: boolean;
  /** Alternative to Outlet — render children directly. */
  children?: React.ReactNode;
}

export const AdminLayout: React.FC<AdminLayoutProps> = ({
  title,
  menuItems,
  user,
  onLogout,
  logo,
  withOutlet = true,
  children,
}) => {
  const [sidebarOpen, setSidebarOpen] = React.useState(true);
  const { isDark } = useThemeStore();

  React.useEffect(() => {
    const handleResize = () => {
      setSidebarOpen(window.innerWidth >= 1024);
    };
    handleResize();
    window.addEventListener("resize", handleResize);
    return () => window.removeEventListener("resize", handleResize);
  }, []);

  React.useEffect(() => {
    document.documentElement.classList.toggle("dark", isDark);
  }, [isDark]);

  return (
    <div className="flex h-screen w-screen flex-col overflow-hidden bg-background">
      <AppHeader
        title={title}
        toggleSidebar={() => setSidebarOpen((v) => !v)}
        user={user}
        onLogout={onLogout}
        logo={logo}
      />

      <div className="relative flex flex-1 overflow-hidden">
        <AppSidebar isOpen={sidebarOpen} menuItems={menuItems} />

        {sidebarOpen && (
          <div
            className="absolute inset-0 z-20 bg-black/40 transition-opacity lg:hidden"
            onClick={() => setSidebarOpen(false)}
          />
        )}

        <main className="relative z-0 flex w-full min-w-0 flex-1 flex-col overflow-y-auto bg-background p-4 md:p-6">
          <div className="flex min-w-0 flex-1 flex-col">
            {withOutlet ? <Outlet /> : children}
          </div>
        </main>
      </div>
    </div>
  );
};
