import * as React from "react";
import { createPortal } from "react-dom";
import { XIcon } from "lucide-react";
import { cn } from "../lib/utils";

export interface DrawerProps {
  isOpen: boolean;
  onClose: () => void;
  title?: string;
  children: React.ReactNode;
  width?: string;
  footer?: React.ReactNode;
  /**
   * Disable closing when user clicks the backdrop. Useful for
   * destructive-action confirmations where the user must explicitly
   * pick a button (e.g. "Save" / "Cancel"). Default false.
   */
  disableBackdropClose?: boolean;
}

/**
 * Slide-out panel from the right side.
 *
 * Phase-4 enhancements over the kalventis-ui v2.2.3 baseline:
 *   1. **Focus trap** — Tab cycles only through focusable elements
 *      inside the drawer. Shift+Tab cycles backwards.
 *   2. **Restore focus on close** — when the drawer unmounts, focus
 *      returns to the element that had focus before it opened
 *      (usually the trigger button).
 *   3. **Initial focus** — when the drawer opens, focus jumps to the
 *      first focusable element inside (or the close button if no
 *      focusable content).
 *
 * For more advanced needs, prefer the Radix-based `Sheet` component
 * from primitives (supports left/right/top/bottom sides).
 */
export const Drawer: React.FC<DrawerProps> = ({
  isOpen,
  onClose,
  title,
  children,
  width = "w-full sm:w-[450px] md:w-[600px]",
  footer,
  disableBackdropClose = false,
}) => {
  const [shouldRender, setShouldRender] = React.useState(isOpen);
  const [isAnimating, setIsAnimating] = React.useState(isOpen);

  // Refs for focus management
  const panelRef = React.useRef<HTMLDivElement>(null);
  const triggerRef = React.useRef<HTMLElement | null>(null);

  React.useEffect(() => {
    if (isOpen) {
      setShouldRender(true);
      const timer = setTimeout(() => setIsAnimating(true), 10);
      return () => clearTimeout(timer);
    } else {
      setIsAnimating(false);
      const timer = setTimeout(() => setShouldRender(false), 300);
      return () => clearTimeout(timer);
    }
  }, [isOpen]);

  React.useEffect(() => {
    if (!isOpen) return;

    // Capture the element that had focus BEFORE the drawer opened.
    triggerRef.current = document.activeElement as HTMLElement;

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        onClose();
        return;
      }

      // Focus trap inside the drawer panel
      if (e.key === "Tab" && panelRef.current) {
        const focusable = panelRef.current.querySelectorAll<HTMLElement>(
          'a[href], button:not([disabled]), textarea, input:not([disabled]), select, [tabindex]:not([tabindex="-1"])'
        );
        if (focusable.length === 0) return;

        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        const active = document.activeElement;

        if (e.shiftKey) {
          if (active === first || !panelRef.current.contains(active)) {
            e.preventDefault();
            last.focus();
          }
        } else {
          if (active === last) {
            e.preventDefault();
            first.focus();
          }
        }
      }
    };

    document.body.style.overflow = "hidden";
    window.addEventListener("keydown", handleKeyDown);

    // Initial focus into the drawer
    const focusTimer = window.setTimeout(() => {
      if (panelRef.current) {
        const focusable = panelRef.current.querySelectorAll<HTMLElement>(
          'a[href], button:not([disabled]), textarea, input:not([disabled]), select, [tabindex]:not([tabindex="-1"])'
        );
        if (focusable.length > 0) {
          focusable[0].focus();
        } else {
          panelRef.current.focus();
        }
      }
    }, 50);

    return () => {
      document.body.style.overflow = "unset";
      window.removeEventListener("keydown", handleKeyDown);
      window.clearTimeout(focusTimer);

      if (triggerRef.current && typeof triggerRef.current.focus === "function") {
        triggerRef.current.focus();
      }
      triggerRef.current = null;
    };
  }, [isOpen, onClose]);

  if (!shouldRender || typeof document === "undefined") return null;

  return createPortal(
    <>
      <div
        className={cn(
          "fixed inset-0 z-[9990] bg-black/40 transition-opacity duration-300",
          isAnimating ? "opacity-100" : "opacity-0"
        )}
        onClick={disableBackdropClose ? undefined : onClose}
      />
      <div
        ref={panelRef}
        tabIndex={-1}
        className={cn(
          "fixed right-0 top-0 z-[9999] flex h-full flex-col bg-card text-card-foreground shadow-2xl transition-transform duration-300 ease-[cubic-bezier(0.4,0,0.2,1)] outline-none",
          width,
          isAnimating ? "translate-x-0" : "translate-x-full"
        )}
      >
        <div className="flex shrink-0 items-center justify-between border-b border-border bg-surface/50 px-6 py-4">
          {title && (
            <h2 className="m-0 text-lg font-bold tracking-tight text-card-foreground">
              {title}
            </h2>
          )}
          <button
            type="button"
            onClick={onClose}
            className="rounded-full p-2 text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50"
            aria-label="Tutup"
          >
            <XIcon className="size-4" />
          </button>
        </div>

        <div className="relative flex-1 overflow-y-auto p-6 [&::-webkit-scrollbar]:hidden [-ms-overflow-style:none] [scrollbar-width:none]">
          {children}
        </div>

        {footer && (
          <div className="shrink-0 border-t border-border bg-surface px-6 py-4">
            {footer}
          </div>
        )}
      </div>
    </>,
    document.body
  );
};
