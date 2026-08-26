import * as React from "react";
import { MenuIcon, MoonIcon, SunIcon, LogOutIcon, UserIcon } from "lucide-react";
import { useThemeStore } from "../../store/themeStore";
import {
  Avatar,
  AvatarFallback,
  AvatarImage,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "../ui";
import { cn } from "../../lib/cn";

export interface AppHeaderProps {
  title?: string;
  toggleSidebar: () => void;
  user?: {
    name: string;
    role: string;
    email?: string;
    avatarUrl?: string;
  };
  onLogout?: () => void;
  logo?: React.ReactNode;
}

const getInitials = (name: string): string =>
  name
    .split(" ")
    .map((n) => n[0])
    .join("")
    .toUpperCase()
    .slice(0, 2);

export const AppHeader: React.FC<AppHeaderProps> = ({
  title,
  toggleSidebar,
  user = { name: "Administrator", role: "User" },
  onLogout,
  logo,
}) => {
  const { isDark, toggleTheme } = useThemeStore();

  return (
    <header className="z-20 flex h-[60px] items-center justify-between border-b border-border bg-card px-4 shadow-sm">
      <div className="flex items-center gap-5">
        <button
          type="button"
          onClick={toggleSidebar}
          className="rounded-lg border-none bg-transparent p-1 text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
          aria-label="Toggle sidebar"
        >
          <MenuIcon className="size-5" />
        </button>
        <span className="mr-4 text-[1.2rem] font-medium text-foreground">
          {logo ?? title}
        </span>
      </div>

      <div className="flex items-center gap-3">
        {/* Theme Toggle (button) */}
        <button
          type="button"
          onClick={toggleTheme}
          className={cn(
            "relative flex h-8 w-14 items-center rounded-full border-none px-1 shadow-md transition-all duration-300 hover:shadow-lg",
            isDark
              ? "bg-gradient-to-r from-slate-800 to-slate-900"
              : "bg-gradient-to-r from-blue-400 to-cyan-400"
          )}
          title={isDark ? "Switch to Light Mode" : "Switch to Dark Mode"}
          aria-label="Toggle dark mode"
        >
          <span
            className={cn(
              "absolute flex size-6 items-center justify-center rounded-full bg-white shadow-lg transition-all duration-500",
              isDark
                ? "left-[calc(100%-1.75rem)] shadow-amber-400/50"
                : "left-1 shadow-blue-400/50"
            )}
          >
            {isDark ? (
              <MoonIcon className="size-3.5 text-amber-500" />
            ) : (
              <SunIcon className="size-3.5 text-amber-400" />
            )}
          </span>
        </button>

        {/* User Menu via DropdownMenu primitive */}
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button
              type="button"
              className="flex items-center gap-2.5 rounded-lg border-none bg-transparent px-2 py-1.5 transition-colors hover:bg-muted"
            >
              <Avatar className="size-[38px] rounded-full border-2 border-primary">
                {user.avatarUrl ? (
                  <AvatarImage src={user.avatarUrl} alt={user.name} />
                ) : null}
                <AvatarFallback className="rounded-full bg-gradient-to-br from-primary to-purple-600 text-sm font-bold text-primary-foreground">
                  {getInitials(user.name)}
                </AvatarFallback>
              </Avatar>
              <div className="hidden flex-col items-start md:flex">
                <span className="text-sm font-medium leading-tight text-foreground">
                  {user.name}
                </span>
                <span className="text-xs leading-tight text-muted-foreground">
                  {user.role}
                </span>
              </div>
            </button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-[220px]">
            <DropdownMenuLabel className="p-0 font-normal">
              <div className="border-b border-border px-4 py-3">
                <p className="text-sm font-medium text-foreground">{user.name}</p>
                {user.email && (
                  <p className="text-xs text-muted-foreground">{user.email}</p>
                )}
              </div>
            </DropdownMenuLabel>
            <DropdownMenuItem
              onClick={() => {
                /* no-op profile placeholder */
              }}
            >
              <UserIcon className="size-4 text-muted-foreground" />
              Profile
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem
              variant="destructive"
              onClick={onLogout ?? (() => undefined)}
            >
              <LogOutIcon className="size-4" />
              Logout
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </header>
  );
};
