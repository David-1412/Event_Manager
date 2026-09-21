"use client";

import { useEffect, useRef, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { cn } from "@/lib/cn";

/**
 * Shared dialog shell for Modal (centred) and BottomSheet (mobile).
 * Traps focus, restores it to the opener, closes on Escape/backdrop, and locks
 * background scroll — spec §10 requires the sheet to trap and restore focus.
 */
export function Dialog({
  open,
  onClose,
  labelledBy,
  variant = "modal",
  children,
  className,
}: {
  open: boolean;
  onClose: () => void;
  labelledBy: string;
  variant?: "modal" | "sheet";
  children: ReactNode;
  className?: string;
}) {
  const panelRef = useRef<HTMLDivElement>(null);
  const restoreTo = useRef<HTMLElement | null>(null);

  useEffect(() => {
    if (!open) return;
    restoreTo.current = document.activeElement as HTMLElement | null;
    const { overflow } = document.body.style;
    document.body.style.overflow = "hidden";

    const panel = panelRef.current;
    panel?.querySelector<HTMLElement>("[data-autofocus]")?.focus();

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.stopPropagation();
        onClose();
        return;
      }
      if (event.key !== "Tab" || !panel) return;
      const focusables = panel.querySelectorAll<HTMLElement>(
        'a[href],button:not([disabled]),textarea,input,select,[tabindex]:not([tabindex="-1"])',
      );
      if (focusables.length === 0) return;
      const first = focusables[0];
      const last = focusables[focusables.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("keydown", onKeyDown);
      document.body.style.overflow = overflow;
      restoreTo.current?.focus();
    };
  }, [open, onClose]);

  if (!open || typeof document === "undefined") return null;

  return createPortal(
    <div className="fixed inset-0 z-50">
      <div
        className="absolute inset-0 bg-black/40"
        onClick={onClose}
        aria-hidden="true"
      />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={labelledBy}
        className={cn(
          "absolute bg-surface",
          variant === "modal"
            ? "left-1/2 top-1/2 w-[min(92vw,28rem)] -translate-x-1/2 -translate-y-1/2 rounded-lg border border-border p-5 shadow-float"
            : "inset-x-0 bottom-0 max-h-[85dvh] overflow-y-auto rounded-t-lg border-t border-border p-4 pb-[max(1rem,env(safe-area-inset-bottom))] shadow-float",
          className,
        )}
      >
        {children}
      </div>
    </div>,
    document.body,
  );
}

/** Destructive confirmation must name the consequence (spec §8). */
export function ConfirmDialog({
  open,
  titleId,
  title,
  body,
  confirmLabel,
  onConfirm,
  onClose,
  busy = false,
}: {
  open: boolean;
  titleId: string;
  title: string;
  body: ReactNode;
  confirmLabel: string;
  onConfirm: () => void;
  onClose: () => void;
  busy?: boolean;
}) {
  return (
    <Dialog open={open} onClose={onClose} labelledBy={titleId}>
      <h2 id={titleId} className="text-h3 text-fg">
        {title}
      </h2>
      <div className="mt-2 text-body text-fg-muted">{body}</div>
      <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
        <button
          type="button"
          onClick={onClose}
          className="press h-11 rounded-md border border-border bg-surface px-5 text-body font-medium text-fg hover:bg-surface-2"
        >
          Keep it
        </button>
        <button
          type="button"
          data-autofocus
          disabled={busy}
          onClick={onConfirm}
          className="press h-11 rounded-md bg-danger px-5 text-body font-medium text-white disabled:bg-surface-2 disabled:text-fg-muted"
        >
          {busy ? "Working…" : confirmLabel}
        </button>
      </div>
    </Dialog>
  );
}
