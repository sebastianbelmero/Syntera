import * as React from "react";
import { NavLink } from "react-router-dom";
import { ChevronDownIcon } from "lucide-react";
import { cn } from "../../lib/cn";
import logoUrl from "../../assets/syntera-logo.jpg";

/**
 * MenuItem — sidebar navigation entry.
 *
 * `icon` is a ReactNode (typically a lucide-react element like
 * `<HomeIcon className="size-4" />`) so the app owns its icon set and
 * isn't locked to any specific icon library.
 */
export interface MenuItem {
  path: string;
  label: string;
  icon?: React.ReactNode;
  children?: MenuItem[];
}

export interface AppSidebarProps {
  isOpen: boolean;
  menuItems?: MenuItem[];
}

export const AppSidebar: React.FC<AppSidebarProps> = ({
  isOpen,
  menuItems = [],
}) => {
  const [openGroups, setOpenGroups] = React.useState<Record<string, boolean>>(
    {}
  );

  const toggleGroup = (path: string) =>
    setOpenGroups((prev) => ({ ...prev, [path]: !prev[path] }));

  const renderMenuItem = (item: MenuItem) => {
    if (item.children && item.children.length > 0) {
      const isOpenGroup = openGroups[item.path] || false;
      return (
        <li key={item.path} className="mt-1">
          <div
            onClick={() => isOpen && toggleGroup(item.path)}
            className={cn(
              "group flex cursor-pointer select-none items-center transition-colors duration-200",
              isOpen
                ? "justify-between px-[1.2rem] py-2.5 hover:bg-muted"
                : "justify-center py-3"
            )}
            title={item.label}
          >
            <div className="flex items-center">
              <span
                className={cn(
                  "text-muted-foreground group-hover:text-foreground",
                  isOpen ? "w-8" : ""
                )}
              >
                {item.icon}
              </span>
              {isOpen && (
                <span className="text-[13.5px] font-semibold tracking-tight text-foreground group-hover:text-foreground">
                  {item.label}
                </span>
              )}
            </div>
            {isOpen && (
              <ChevronDownIcon
                className={cn(
                  "size-3 text-muted-foreground transition-transform duration-200 group-hover:text-foreground",
                  isOpenGroup ? "" : "-rotate-90"
                )}
              />
            )}
          </div>
          <div
            className={cn(
              "overflow-hidden transition-all duration-300 ease-in-out",
              isOpen && isOpenGroup
                ? "max-h-96 opacity-100"
                : "max-h-0 opacity-0"
            )}
          >
            <ul className="m-0 list-none p-0">
              {item.children.map((child) => renderSubMenu(child))}
            </ul>
          </div>
        </li>
      );
    }

    return (
      <li key={item.path}>
        <NavLink
          to={item.path}
          className={({ isActive }) =>
            cn(
              "flex cursor-pointer items-center no-underline transition-colors duration-200",
              isOpen ? "justify-start py-3 px-[1.2rem]" : "justify-center py-3",
              isActive
                ? "bg-primary/10 font-semibold text-primary border-r-4 border-primary"
                : "border-r-4 border-transparent font-normal text-muted-foreground hover:bg-muted hover:text-foreground"
            )
          }
          title={item.label}
        >
          <span className={cn("text-muted-foreground", isOpen ? "w-8" : "")}>
            {item.icon}
          </span>
          {isOpen && <span className="text-[14px]">{item.label}</span>}
        </NavLink>
      </li>
    );
  };

  const renderSubMenu = (item: MenuItem) => (
    <li key={item.path}>
      <NavLink
        to={item.path}
        className={({ isActive }) =>
          cn(
            "flex cursor-pointer items-center no-underline transition-colors duration-200",
            isOpen
              ? "justify-start py-2.5 pl-[3.2rem] pr-6"
              : "justify-center py-3",
            isActive
              ? "bg-primary/10 font-medium text-primary border-r-4 border-primary"
              : "border-r-4 border-transparent text-muted-foreground hover:bg-muted hover:text-foreground"
          )
        }
        title={!isOpen ? item.label : undefined}
      >
        <span className={cn("text-muted-foreground", isOpen ? "w-8" : "")}>
          {item.icon}
        </span>
        {isOpen && <span className="text-[13px]">{item.label}</span>}
      </NavLink>
    </li>
  );

  return (
    <aside
      className={cn(
        "z-30 flex h-full shrink-0 flex-col overflow-y-auto overflow-x-hidden border-r border-border bg-card transition-all duration-300 ease-[cubic-bezier(0.4,0,0.2,1)]",
        isOpen
          ? "w-[260px] max-lg:absolute max-lg:left-0 max-lg:shadow-2xl"
          : "w-[70px] max-lg:absolute max-lg:-left-[80px]",
        "lg:relative"
      )}
    >
      {/* Logo header — full wordmark when sidebar is open, scaled
          icon-only square when collapsed. The logo JPG has a white
          background so we wrap it in a rounded white chip that
          looks intentional in both light and dark modes. */}
      <a
        href="/dashboard"
        className={cn(
          "flex shrink-0 items-center border-b border-border py-3 no-underline",
          isOpen ? "justify-start gap-2 px-4" : "justify-center px-1.5"
        )}
        aria-label="Syntera — ke Dashboard"
      >
        <span
          className={cn(
            "shrink-0 overflow-hidden rounded-lg bg-white shadow-sm ring-1 ring-black/5",
            isOpen ? "size-9" : "size-9"
          )}
        >
          <img
            src={logoUrl}
            alt=""
            aria-hidden
            className="size-full object-cover"
            draggable={false}
          />
        </span>
        {isOpen && (
          <span className="flex flex-col leading-tight">
            <span className="text-[15px] font-bold tracking-tight text-foreground">
              SYNTERA
            </span>
            <span className="text-[10px] font-medium uppercase tracking-wider text-[var(--accent)]">
              One Platform
            </span>
          </span>
        )}
      </a>

      <nav className="py-4">
        <ul className="m-0 list-none p-0">{menuItems.map(renderMenuItem)}</ul>
      </nav>
    </aside>
  );
};
