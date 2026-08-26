import * as React from "react";
import { createPortal } from "react-dom";
import { XIcon } from "lucide-react";
import { cn } from "../lib/cn";

export interface ModalProps {
  isOpen: boolean;
  onClose: () => void;
  title?: string;
  children: React.ReactNode;
  /**
   * Tailwind width class, e.g. 'w-[400px]', 'w-[600px]', 'max-w-2xl'.
   * Pass `max-w-full sm:max-w-md md:max-w-lg` for fully responsive widths.
   */
  width?: string;
  footer?: React.ReactNode;
  icon?: React.ReactNode;
  /** Hide the default close (X) button in the header. Default false. */
  hideCloseButton?: boolean;
  /**
   * Disable closing when user clicks the backdrop. Useful for
   * destructive-action confirmations where the user must explicitly
   * pick a button (e.g. "Delete" / "Cancel"). Default false.
   */
  disableBackdropClose?: boolean;
}

/**
 * Lightweight, animated modal dialog with focus trap.
 *
 * Syntera enhancements on top of the original design:
 *   1. **Focus trap** — Tab cycles only through focusable elements
 *      inside the modal. Shift+Tab cycles backwards. Focus cannot
 *      leak to the background page while the modal is open.
 *   2. **Restore focus on close** — when the modal unmounts, focus
 *      returns to the element that had focus before the modal opened
 *      (usually the trigger button). Essential for screen-reader +
 *      keyboard users.
 *   3. **Initial focus** — when the modal opens, focus jumps to the
 *      first focusable element inside (or the close button if no
 *      focusable content). Skip-to-content pattern.
 *   4. **Backdrop-click disable option** — for destructive confirmations.
 */
export const Modal: React.FC<ModalProps> = ({
  isOpen,
  onClose,
  title,
  children,
  width = "w-[400px]",
  footer,
  icon,
  hideCloseButton = false,
  disableBackdropClose = false,
}) => {
  const [shouldRender, setShouldRender] = React.useState(isOpen);
  const [isAnimating, setIsAnimating] = React.useState(isOpen);

  // Refs for focus management
  const panelRef = React.useRef<HTMLDivElement>(null);
  const triggerRef = React.useRef<HTMLElement | null>(null);

  // Mount / unmount with animation delay. The enter/exit animation
  // lifecycle is a genuine state machine (keep-mounted during the 300ms
  // exit transition) that cannot be derived during render.
  React.useEffect(() => {
    if (isOpen) {
      // oxlint-disable-next-line react/set-state-in-effect -- mount the portal when isOpen flips on; the 300ms exit transition below still needs the node rendered.
      setShouldRender(true);
      const timer = setTimeout(() => setIsAnimating(true), 10);
      return () => clearTimeout(timer);
    } else {
      setIsAnimating(false);
      const timer = setTimeout(() => setShouldRender(false), 300);
      return () => clearTimeout(timer);
    }
  }, [isOpen]);

  // Escape to close + body scroll lock + focus management
  React.useEffect(() => {
    if (!isOpen) return;

    // Capture the element that had focus BEFORE the modal opened,
    // so we can restore focus to it after close.
    triggerRef.current = document.activeElement as HTMLElement;

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        onClose();
        return;
      }

      // Focus trap: when Tab is pressed, cycle focus within the modal.
      // Without this, Tab would move focus to the background page
      // (where buttons look interactive but are not the user's
      // intent when a modal is open).
      if (e.key === "Tab" && panelRef.current) {
        const focusable = panelRef.current.querySelectorAll<HTMLElement>(
          'a[href], button:not([disabled]), textarea, input:not([disabled]), select, [tabindex]:not([tabindex="-1"])'
        );
        if (focusable.length === 0) return;

        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        const active = document.activeElement;

        if (e.shiftKey) {
          // Shift+Tab from the first element should wrap to the last
          if (active === first || !panelRef.current.contains(active)) {
            e.preventDefault();
            last.focus();
          }
        } else {
          // Tab from the last element should wrap to the first
          if (active === last) {
            e.preventDefault();
            first.focus();
          }
        }
      }
    };

    document.body.style.overflow = "hidden";
    window.addEventListener("keydown", handleKeyDown);

    // Initial focus: move focus into the modal so screen-reader users
    // hear the modal title announced. Use rAF to wait for the panel
    // to be painted first.
    const focusTimer = window.setTimeout(() => {
      if (panelRef.current) {
        const focusable = panelRef.current.querySelectorAll<HTMLElement>(
          'a[href], button:not([disabled]), textarea, input:not([disabled]), select, [tabindex]:not([tabindex="-1"])'
        );
        if (focusable.length > 0) {
          focusable[0].focus();
        } else {
          // No focusable content — focus the panel itself
          panelRef.current.focus();
        }
      }
    }, 50);

    return () => {
      document.body.style.overflow = "unset";
      window.removeEventListener("keydown", handleKeyDown);
      window.clearTimeout(focusTimer);

      // Restore focus to the trigger element after close
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
      <div className="fixed inset-0 z-[9999] flex h-full w-full items-center justify-center p-4 pointer-events-none">
        <div
          ref={panelRef}
          tabIndex={-1}
          className={cn(
            "pointer-events-auto flex max-h-full max-w-full flex-col overflow-hidden rounded-lg bg-card text-card-foreground shadow-2xl transition-all duration-300 outline-none",
            "transform",
            width,
            isAnimating
              ? "opacity-100 scale-100 translate-y-0"
              : "opacity-0 scale-95 translate-y-4"
          )}
        >
          {(title || icon || !hideCloseButton) && (
            <div className="flex shrink-0 items-center gap-3 border-b border-border p-5">
              {icon && <div className="shrink-0">{icon}</div>}
              {title && (
                <h3 className="m-0 flex-1 text-lg font-bold text-card-foreground">
                  {title}
                </h3>
              )}
              {!hideCloseButton && (
                <button
                  type="button"
                  onClick={onClose}
                  className="rounded-md p-1.5 text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50"
                  aria-label="Tutup"
                >
                  <XIcon className="size-4" />
                </button>
              )}
            </div>
          )}

          <div className="overflow-y-auto p-5 text-sm leading-relaxed text-surface-foreground max-h-[70vh]">
            {children}
          </div>

          {footer && (
            <div className="flex shrink-0 justify-end gap-3 border-t border-border bg-surface px-5 py-4">
              {footer}
            </div>
          )}
        </div>
      </div>
    </>,
    document.body
  );
};
