import * as React from "react";
import { useNavigate } from "react-router-dom";
import { MenuIcon, MoonIcon, SunIcon, LogOutIcon, UserIcon } from "lucide-react";
import { useThemeStore } from "../../store/themeStore";
import { useAuthStore } from "../../store/authStore";
import logoUrl from "../../assets/syntera-logo.jpg";
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
  toggleSidebar,
  user = { name: "User", role: "User" },
  onLogout,
}) => {
  const { isDark, toggleMode } = useThemeStore();
  const theme = useAuthStore((s) => s.theme);
  const navigate = useNavigate();

  return (
    <header
      className="z-20 flex h-[60px] items-center justify-between px-3 sm:px-4"
      style={{
        borderBottom: "1px solid var(--color-border)",
        backgroundColor: "var(--color-surface)",
      }}
    >
      <div className="flex min-w-0 items-center gap-3 sm:gap-4">
        <button
          type="button"
          onClick={toggleSidebar}
          className="rounded-lg border-none bg-transparent p-2.5 transition-colors hover:opacity-80 min-h-[44px] min-w-[44px] flex items-center justify-center"
          style={{ color: "var(--color-muted)" }}
          aria-label="Toggle sidebar"
        >
          <MenuIcon className="size-5" />
        </button>

        {/* Logo + title in navbar */}
        <a
          href="/dashboard"
          className="flex items-center gap-2 no-underline"
          aria-label="Syntera — Dashboard"
        >
          <span className="shrink-0 overflow-hidden rounded-lg bg-white shadow-sm ring-1 ring-black/5 size-9">
            <img src={logoUrl} alt="" aria-hidden className="size-full object-contain p-0.5" draggable={false} />
          </span>
          <span className="flex flex-col leading-tight">
            <span className="text-[15px] font-bold tracking-tight" style={{ color: "var(--color-text)" }}>
              SYNTERA
            </span>
            <span className="text-[9px] font-medium uppercase tracking-wider" style={{ color: "var(--color-accent)" }}>
              One Platform
            </span>
          </span>
        </a>
      </div>

      <div className="flex items-center gap-2 sm:gap-3">
        {/* Theme mode toggle (light/dark) */}
        <button
          type="button"
          onClick={toggleMode}
          className="rounded-lg p-2.5 transition-colors hover:opacity-80 min-h-[44px] min-w-[44px] flex items-center justify-center"
          style={{
            border: "1px solid var(--color-border)",
            color: "var(--color-text)",
          }}
          aria-label="Toggle dark mode"
          title={isDark ? "Switch to light mode" : "Switch to dark mode"}
        >
          {isDark ? <SunIcon className="size-4" /> : <MoonIcon className="size-4" />}
        </button>

        {/* User menu */}
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button
              type="button"
              className="flex items-center gap-2.5 rounded-lg border-none bg-transparent px-2 py-1.5 transition-colors hover:opacity-80"
              aria-label="User menu"
            >
              <Avatar className="size-[34px] rounded-full sm:size-[38px]"
                style={{ border: `2px solid var(--color-primary)` }}>
                {user.avatarUrl ? (
                  <AvatarImage src={user.avatarUrl} alt={user.name} />
                ) : null}
                <AvatarFallback
                  className="rounded-full text-sm font-bold text-white"
                  style={{ backgroundColor: "var(--color-primary)" }}
                >
                  {getInitials(user.name)}
                </AvatarFallback>
              </Avatar>
              <div className="hidden flex-col items-start md:flex">
                <span className="text-sm font-medium leading-tight" style={{ color: "var(--color-text)" }}>
                  {user.name}
                </span>
                <span className="text-xs leading-tight" style={{ color: "var(--color-muted)" }}>
                  {user.role}
                </span>
              </div>
            </button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-[220px]">
            <DropdownMenuLabel className="p-0 font-normal">
              <div className="px-4 py-3" style={{ borderBottom: "1px solid var(--color-border)" }}>
                <p className="text-sm font-medium" style={{ color: "var(--color-text)" }}>{user.name}</p>
                {user.email && (
                  <p className="text-xs" style={{ color: "var(--color-muted)" }}>{user.email}</p>
                )}
                {theme && (
                  <p className="text-xs mt-1" style={{ color: "var(--color-muted)" }}>
                    Theme: {theme.themeKey}
                  </p>
                )}
              </div>
            </DropdownMenuLabel>
            <DropdownMenuItem onClick={() => navigate("/settings")}>
              <UserIcon className="size-4" />
              Profile
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem
              variant="destructive"
              onClick={onLogout ?? (() => { window.location.assign("/login"); })}
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
