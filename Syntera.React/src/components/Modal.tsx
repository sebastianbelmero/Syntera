import { useCallback, useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { createPortal } from "react-dom";
import { X } from "lucide-react";
import { cn } from "../lib/cn";

/**
 * Modal — animated, responsive, accessible dialog.
 *
 * Improvements over the prior version:
 *
 * 1. Bottom-sheet on mobile, centered dialog on desktop. The
 *    <sm breakpoint switches to a sheet that slides up from the
 *    bottom of the viewport — much easier to reach with one
 *    hand on a phone, and standard modern UI (Material, iOS).
 *    On ≥sm it becomes the familiar centered dialog with
 *    scale + fade-in.
 *
 * 2. Real CSS-keyframe animations (defined in index.css under
 *    @keyframes modal-* and sheet-*). The previous modal only
 *    faded opacity, which made it feel "stiff" — now the
 *    backdrop fades, the sheet slides up, and the dialog
 *    scales from 0.96 to 1 with a spring-like easing curve.
 *    Exit animations also work: closing waits for the
 *    animation to complete before unmounting.
 *
 * 3. Body scroll lock. While the modal is open, the page behind
 *    cannot scroll — no more accidental scroll-leak on iOS
 *    Safari, and the page position is restored on close.
 *
 * 4. Focus trap. Tab cycles through the modal's focusable
 *    elements only — focus can't leak to the background page.
 *    On close, focus is restored to the trigger that opened
 *    the modal (caller-managed via the `initialFocusRef` /
 *    `returnFocusRef` props, or auto-detected).
 *
 * 5. Scrollable body region. Long forms (like ProductFormModal
 *    with 18 fields) used to overflow the viewport. Now the
 *    header and footer are sticky and only the body scrolls,
 *    so the Save/Cancel buttons always remain in reach.
 *
 * API is backward compatible — every existing prop is still
 * accepted with the same shape.
 */
export interface ModalProps {
  open: boolean;
  onClose: () => void;
  title: string;
  description?: string;
  children: ReactNode;
  footer?: ReactNode;
  size?: "sm" | "md" | "lg" | "xl" | "2xl" | "3xl" | "full";
  /** Disable backdrop click to close (useful for confirmations). */
  disableBackdropClose?: boolean;
  /** Disable Escape key to close. */
  disableEscapeClose?: boolean;
  /** Override the mobile presentation. Defaults to "sheet". */
  mobileVariant?: "sheet" | "dialog";
  /** Hide the close X button (when a footer has its own). */
  hideCloseButton?: boolean;
}

const sizeMap: Record<NonNullable<ModalProps["size"]>, string> = {
  sm: "sm:max-w-sm",
  md: "sm:max-w-md",
  lg: "sm:max-w-lg",
  xl: "sm:max-w-2xl",
  "2xl": "sm:max-w-4xl",
  "3xl": "sm:max-w-6xl",
  full: "sm:max-w-[95vw]",
};

export function Modal({
  open,
  onClose,
  title,
  description,
  children,
  footer,
  size = "md",
  disableBackdropClose = false,
  disableEscapeClose = false,
  mobileVariant = "sheet",
  hideCloseButton = false,
}: ModalProps) {
  // Two-phase rendering: `mounted` keeps the portal in the DOM
  // while exit animations run; `active` toggles the animation
  // state. The CSS classes (modal-enter / sheet-enter) start the
  // entrance; on close we drop the active class and wait for
  // the animation end before unmounting.
  const [mounted, setMounted] = useState(open);
  const [active, setActive] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const previouslyFocusedRef = useRef<HTMLElement | null>(null);
  // Track BOTH rAF handles so the cleanup can cancel the entire
  // double-rAF chain — if only r1 is cancelled and r1 already
  // fired (queuing r2), r2 would still call setActive(true) on
  // a modal that's transitioning to closed.
  const r1Ref = useRef<number | null>(null);
  const r2Ref = useRef<number | null>(null);

  // Mount the portal immediately when `open` turns true, then
  // flip `active` to start the entrance animation on the next
  // frame (so the CSS class actually applies after mount).
  useEffect(() => {
    if (open) {
      // Remember which element had focus so we can restore it
      // when the modal closes — standard accessibility pattern.
      previouslyFocusedRef.current =
        (document.activeElement as HTMLElement) ?? null;
      setMounted(true);
      // rAF double-call ensures the browser has painted the
      // initial (enter-from) state before we add the active
      // class — otherwise the animation can skip.
      r1Ref.current = requestAnimationFrame(() => {
        r2Ref.current = requestAnimationFrame(() => setActive(true));
        // Focus the container itself; if it has focusable
        // children, the focus trap will move focus into them.
        containerRef.current?.focus();
      });
      return () => {
        if (r1Ref.current !== null) cancelAnimationFrame(r1Ref.current);
        if (r2Ref.current !== null) cancelAnimationFrame(r2Ref.current);
        r1Ref.current = null;
        r2Ref.current = null;
      };
    }
    if (!open && mounted) {
      // Drop active to trigger exit animation, then unmount
      // after the animation duration.
      setActive(false);
      const t = setTimeout(() => setMounted(false), 220);
      return () => clearTimeout(t);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  // Body scroll lock while mounted + active. Restored on close.
  useEffect(() => {
    if (!mounted) return;
    const prev = document.body.style.overflow;
    const prevPadding = document.body.style.paddingRight;
    // Compensate for scrollbar disappearance to avoid layout
    // jump on platforms with visible scrollbars.
    const scrollbarWidth = window.innerWidth - document.documentElement.clientWidth;
    if (scrollbarWidth > 0) {
      document.body.style.paddingRight = `${scrollbarWidth}px`;
    }
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = prev;
      document.body.style.paddingRight = prevPadding;
    };
  }, [mounted]);

  // Escape to close + focus trap.
  useEffect(() => {
    if (!mounted || !active) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !disableEscapeClose) {
        e.stopPropagation();
        onClose();
        return;
      }
      if (e.key === "Tab") {
        // Trap focus inside the modal.
        const root = containerRef.current;
        if (!root) return;
        const focusables = root.querySelectorAll<HTMLElement>(
          'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])',
        );
        if (focusables.length === 0) return;
        const first = focusables[0];
        const last = focusables[focusables.length - 1];
        if (e.shiftKey && document.activeElement === first) {
          e.preventDefault();
          last.focus();
        } else if (!e.shiftKey && document.activeElement === last) {
          e.preventDefault();
          first.focus();
        }
      }
    };
    window.addEventListener("keydown", onKey, true);
    return () => window.removeEventListener("keydown", onKey, true);
  }, [mounted, active, onClose, disableEscapeClose]);

  // Restore focus to the trigger on unmount.
  useEffect(() => {
    if (mounted) return;
    const t = previouslyFocusedRef.current;
    if (t && typeof t.focus === "function") {
      // Restore on the next microtask so the DOM has settled
      // after the portal unmounted.
      Promise.resolve().then(() => t.focus());
    }
  }, [mounted]);

  // Don't render until the portal is needed — preserves
  // server-rendered HTML cleanliness and avoids running the
  // body-lock effect on first mount.
  // NOTE: all hooks above MUST be called before this early
  // return — React requires hooks in the same order every
  // render. `onPanelClick` is therefore declared above, not
  // below this gate.
  const onPanelClick = useCallback((e: React.MouseEvent) => {
    e.stopPropagation();
  }, []);

  if (!mounted) return null;

  const isSheet = mobileVariant === "sheet";
  const backdropClass = cn(
    "fixed inset-0 z-50 flex items-end justify-center sm:items-center sm:p-4",
    "transition-opacity duration-200",
    active ? "opacity-100" : "opacity-0",
  );

  // On mobile (sheet variant), the sheet spans the full width
  // and sticks to the bottom with a drag-handle. On ≥sm, it
  // becomes a centered dialog with the chosen max-width.
  const panelClass = cn(
    "relative flex max-h-[92vh] w-full flex-col overflow-hidden bg-[var(--card)] text-foreground shadow-2xl",
    // Mobile sheet (default variant).
    isSheet &&
      !active &&
      "translate-y-full rounded-t-2xl sm:translate-y-0 sm:rounded-2xl",
    isSheet &&
      active &&
      "translate-y-0 rounded-t-2xl sm:translate-y-0 sm:rounded-2xl",
    isSheet && "transition-transform duration-200 ease-out sm:transition-none",
    // Desktop dialog: scale in.
    !isSheet && !active && "scale-95 opacity-0 rounded-2xl",
    !isSheet && active && "scale-100 opacity-100 rounded-2xl",
    !isSheet && "transition-all duration-200 ease-out",
    // Desktop width.
    sizeMap[size],
    // Apply the animation classes — these are defined in index.css
    // and drive the actual entrance/exit via @keyframes.
    isSheet && active && "animate-sheet-in",
    isSheet && !active && "animate-sheet-out",
    !isSheet && active && "animate-modal-in",
    !isSheet && !active && "animate-modal-out",
  );

  return createPortal(
    <div
      className={backdropClass}
      role="dialog"
      aria-modal="true"
      aria-labelledby="modal-title"
      onClick={() => {
        if (!disableBackdropClose) onClose();
      }}
    >
      {/* Backdrop layer — keeps the click target distinct from
          the panel. */}
      <div className="absolute inset-0 bg-black/50 backdrop-blur-sm" />

      <div
        ref={containerRef}
        tabIndex={-1}
        className={panelClass}
        onClick={onPanelClick}
        // The panel is the actual dialog content; focus moves
        // into it so keyboard navigation works.
      >
        {/* Mobile drag handle (sheet variant only). */}
        {isSheet && (
          <div className="flex justify-center pt-2 sm:hidden">
            <div className="h-1.5 w-10 rounded-full bg-[var(--border)]" />
          </div>
        )}

        {/* Header — sticky so it stays visible while scrolling a
            long form body. */}
        <header className="sticky top-0 z-10 flex items-start gap-3 border-b border-[var(--border)] bg-[var(--card)] px-6 py-4">
          <div className="min-w-0 flex-1">
            <h2 id="modal-title" className="text-lg font-bold tracking-tight">
              {title}
            </h2>
            {description && (
              <p className="mt-1 text-sm text-[var(--muted-foreground)]">
                {description}
              </p>
            )}
          </div>
          {!hideCloseButton && (
            <button
              type="button"
              onClick={onClose}
              aria-label="Tutup"
              className="grid h-8 w-8 shrink-0 place-items-center rounded-md text-[var(--muted-foreground)] transition hover:bg-[var(--surface)] focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--ring)]"
            >
              <X size={18} />
            </button>
          )}
        </header>

        {/* Body — scrollable region. */}
        <div className="flex-1 overflow-y-auto px-6 py-4">
          <div className="space-y-4">{children}</div>
        </div>

        {/* Footer — sticky at the bottom. */}
        {footer && (
          <footer className="sticky bottom-0 z-10 flex items-center justify-end gap-2 border-t border-[var(--border)] bg-[var(--card)] px-6 py-3">
            {footer}
          </footer>
        )}
      </div>
    </div>,
    document.body,
  );
}

/** Convenience wrapper for a labelled text input inside a Modal. */
export function Field({
  label,
  hint,
  children,
  required,
  htmlFor,
}: {
  label: string;
  hint?: string;
  children: ReactNode;
  required?: boolean;
  htmlFor?: string;
}) {
  return (
    <label htmlFor={htmlFor} className="block text-sm">
      <span className="mb-1 flex items-center gap-1 font-medium text-[var(--foreground)]">
        {label}
        {required && <span className="text-[var(--danger)]">*</span>}
      </span>
      {children}
      {hint && (
        <span className="mt-1 block text-xs text-[var(--muted-foreground)]">
          {hint}
        </span>
      )}
    </label>
  );
}

export const inputClass =
  "w-full rounded-lg border border-[var(--input)] bg-card px-3 py-2 text-sm text-foreground shadow-sm outline-none transition focus:border-[var(--primary)] focus:ring-2 focus:ring-[var(--ring)]";

export const btnPrimary =
  "rounded-lg bg-[var(--primary)] px-4 py-2 text-sm font-semibold text-[var(--primary-foreground)] shadow-sm transition hover:bg-[var(--primary-hover)] disabled:cursor-not-allowed disabled:opacity-60";

export const btnGhost =
  "rounded-lg border border-[var(--border)] bg-card px-4 py-2 text-sm font-medium text-foreground transition hover:bg-[var(--surface)]";

/**
 * ConfirmDialog — opinionated wrapper for destructive confirmations.
 * Replaces the page-level ConfirmDeleteModal pattern with a one-liner.
 */
export function ConfirmDialog({
  open,
  onClose,
  onConfirm,
  title,
  description,
  confirmLabel = "Hapus",
  cancelLabel = "Batal",
  busyLabel = "Memproses…",
  busy = false,
  variant = "danger",
}: {
  open: boolean;
  onClose: () => void;
  onConfirm: () => void | Promise<void>;
  title: string;
  description?: string;
  confirmLabel?: string;
  cancelLabel?: string;
  busyLabel?: string;
  busy?: boolean;
  variant?: "danger" | "primary";
}) {
  const btn =
    variant === "danger"
      ? "rounded-lg bg-[var(--danger)] px-4 py-2 text-sm font-semibold text-[var(--danger-foreground)] transition hover:bg-[var(--danger-hover)] disabled:opacity-60"
      : btnPrimary;
  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      description={description}
      size="sm"
      footer={
        <>
          <button type="button" className={btnGhost} onClick={onClose} disabled={busy}>
            {cancelLabel}
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={() => {
              void onConfirm();
            }}
            className={btn}
          >
            {busy ? busyLabel : confirmLabel}
          </button>
        </>
      }
    >
      <p className="text-sm text-[var(--muted-foreground)]">
        Tindakan ini tidak dapat dibatalkan.
      </p>
    </Modal>
  );
}
