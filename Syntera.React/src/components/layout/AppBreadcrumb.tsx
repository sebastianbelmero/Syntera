import * as React from "react";
import { useLocation, Link } from "react-router-dom";
import { HomeIcon } from "lucide-react";

export interface AppBreadcrumbProps {
  routeDictionary?: Record<string, string>;
}

export const AppBreadcrumb: React.FC<AppBreadcrumbProps> = ({
  routeDictionary = {},
}) => {
  const location = useLocation();
  const pathnames = location.pathname.split("/").filter(Boolean);

  return (
    <nav aria-label="breadcrumb" className="mb-4">
      <ol className="flex items-center gap-2 text-sm text-muted-foreground">
        <li className="flex items-center">
          <Link
            to="/dashboard"
            className="text-primary hover:text-primary-hover"
          >
            <HomeIcon className="size-4" />
          </Link>
        </li>
        {pathnames.map((value, index) => {
          const isLast = index === pathnames.length - 1;
          const routeTo = `/${pathnames.slice(0, index + 1).join("/")}`;
          const title = routeDictionary[value] || value;
          return (
            <li key={value} className="flex items-center gap-2">
              <span className="text-muted-foreground">/</span>
              {isLast ? (
                <span className="font-semibold text-foreground">{title}</span>
              ) : (
                <Link
                  to={routeTo}
                  className="text-muted-foreground hover:text-foreground"
                >
                  {title}
                </Link>
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
};
