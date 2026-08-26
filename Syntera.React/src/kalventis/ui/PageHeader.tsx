import * as React from "react";
import { useLocation, Link } from "react-router-dom";
import { HomeIcon, ChevronRightIcon } from "lucide-react";
import { cn } from "../lib/utils";

export interface PageHeaderProps {
  title?: string;
  description?: string;
  routeDictionary?: Record<string, string>;
  primaryAction?: {
    label: string;
    icon?: React.ReactNode;
    onClick: () => void;
    variant?: "primary" | "secondary" | "danger";
  };
  secondaryActions?: React.ReactNode;
}

/**
 * v2.0 breaking change: `primaryAction.icon` now accepts a ReactNode (e.g. a lucide-react icon)
 * instead of a PrimeIcons class string like "pi pi-plus".
 */
export const PageHeader: React.FC<PageHeaderProps> = ({
  title,
  description,
  routeDictionary = {},
  primaryAction,
  secondaryActions,
}) => {
  const location = useLocation();
  const pathnames = location.pathname.split("/").filter(Boolean);
  const generatedTitle =
    title ||
    routeDictionary[pathnames[pathnames.length - 1]] ||
    "Halaman";

  const primaryActionClass = {
    primary: "bg-primary text-primary-foreground hover:bg-primary-hover",
    secondary:
      "bg-card text-surface-foreground border border-input hover:bg-muted",
    danger: "bg-danger text-danger-foreground hover:bg-danger-hover",
  };

  return (
    <div className="mb-4 flex flex-col justify-between gap-3 md:flex-row md:items-end">
      <div>
        <nav aria-label="breadcrumb" className="mb-1.5">
          <ol className="m-0 flex items-center space-x-1.5 text-[0.8rem] text-muted-foreground p-0">
            <li>
              <Link
                to="/dashboard"
                className="flex items-center text-primary hover:text-primary-hover transition-colors"
              >
                <HomeIcon className="size-3.5" />
              </Link>
            </li>
            {pathnames.map((value, index) => {
              const isLast = index === pathnames.length - 1;
              const routeTo = `/${pathnames.slice(0, index + 1).join("/")}`;
              const titleName = routeDictionary[value] || value;
              return (
                <li key={value} className="flex items-center">
                  <ChevronRightIcon className="mx-1 size-3 text-muted-foreground" />
                  {isLast ? (
                    <span className="font-semibold capitalize text-foreground">
                      {titleName}
                    </span>
                  ) : (
                    <Link
                      to={routeTo}
                      className="capitalize transition-colors hover:text-primary"
                    >
                      {titleName}
                    </Link>
                  )}
                </li>
              );
            })}
          </ol>
        </nav>

        <div className="flex items-baseline gap-3">
          <div className="m-0 text-xl font-bold leading-none tracking-tight text-foreground">
            {generatedTitle}
          </div>
          {description && (
            <span className="hidden border-l-2 border-border pl-3 text-xs leading-none text-muted-foreground md:inline-block">
              {description}
            </span>
          )}
        </div>
      </div>

      {(primaryAction || secondaryActions) && (
        <div className="mt-2 flex items-center gap-2 md:mt-0">
          {secondaryActions}
          {primaryAction && (
            <button
              type="button"
              onClick={primaryAction.onClick}
              className={cn(
                "flex items-center gap-2 rounded px-3 py-1.5 text-[13px] font-semibold shadow-sm transition-all",
                primaryActionClass[primaryAction.variant ?? "primary"]
              )}
            >
              {primaryAction.icon}
              {primaryAction.label}
            </button>
          )}
        </div>
      )}
    </div>
  );
};
