import * as React from "react";
import {
  MenuIcon,
  MoonIcon,
  SunIcon,
  LogOutIcon,
  UserIcon,
  PaletteIcon,
  CheckIcon,
} from "lucide-react";
import {
  useThemeStore,
  THEME_BRANDS,
  THEME_LABELS,
  THEME_SWATCH,
  type ThemeBrand,
} from "../../store/themeStore";
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
  const { brand, isDark, setBrand, toggleMode } = useThemeStore();

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
        {/* Theme + Mode Dropdown */}
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button
              type="button"
              className="flex items-center gap-2 rounded-lg border border-border bg-transparent px-2.5 py-1.5 text-sm transition-colors hover:bg-muted"
              aria-label="Pilih tema"
              title={`Tema: ${THEME_LABELS[brand]} — ${isDark ? "Gelap" : "Terang"}`}
            >
              <PaletteIcon className="size-4 text-muted-foreground" />
              <span
                className="size-4 rounded-full border border-black/10"
                style={{ background: THEME_SWATCH[brand] }}
              />
              <span className="hidden text-xs font-medium sm:inline">
                {THEME_LABELS[brand]}
              </span>
              {isDark ? (
                <MoonIcon className="size-3.5 text-amber-500" />
              ) : (
                <SunIcon className="size-3.5 text-amber-500" />
              )}
            </button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-[240px]">
            <DropdownMenuLabel className="text-xs uppercase tracking-wide text-muted-foreground">
              Palet merek
            </DropdownMenuLabel>
            {THEME_BRANDS.map((b: ThemeBrand) => (
              <DropdownMenuItem
                key={b}
                onClick={() => setBrand(b)}
                className="flex items-center justify-between gap-2"
              >
                <span className="flex items-center gap-2">
                  <span
                    className="size-4 rounded-full border border-black/10"
                    style={{ background: THEME_SWATCH[b] }}
                  />
                  <span className="text-sm">{THEME_LABELS[b]}</span>
                </span>
                {brand === b && <CheckIcon className="size-4 text-primary" />}
              </DropdownMenuItem>
            ))}
            <DropdownMenuSeparator />
            <DropdownMenuItem
              onClick={toggleMode}
              className="flex items-center justify-between"
            >
              <span className="flex items-center gap-2">
                {isDark ? (
                  <MoonIcon className="size-4 text-amber-500" />
                ) : (
                  <SunIcon className="size-4 text-amber-500" />
                )}
                <span className="text-sm">
                  Mode {isDark ? "Gelap" : "Terang"}
                </span>
              </span>
              <span
                className={cn(
                  "relative flex h-5 w-9 items-center rounded-full px-0.5 transition-colors",
                  isDark ? "bg-slate-700" : "bg-slate-300",
                )}
              >
                <span
                  className={cn(
                    "absolute size-4 rounded-full bg-white transition-all",
                    isDark ? "left-[calc(100%-1.125rem)]" : "left-0.5",
                  )}
                />
              </span>
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>

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
