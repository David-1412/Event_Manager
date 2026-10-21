"use client";

import { useEffect, useId, useRef, useState } from "react";
import { MoreHorizontal } from "lucide-react";
import { cn } from "@/lib/cn";

export interface ActionMenuItem {
  label: string;
  onSelect: () => void;
  /** Destructive actions (Delete) render in the danger colour. */
  danger?: boolean;
}

/**
 * A "..." button that opens a short list of actions - Edit and Delete on your own
 * post or comment. Closes on a pick, on Escape (returning focus to the button), and
 * on a click anywhere else, so it never lingers open over the thread.
 */
export function ActionMenu({
  label,
  items,
  className,
}: {
  /** What the menu acts on, for screen readers: "Post actions". */
  label: string;
  items: ActionMenuItem[];
  className?: string;
}) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);
  const menuId = useId();

  useEffect(() => {
    if (!open) return;
    const onPointer = (e: PointerEvent) => {
      if (!root.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      setOpen(false);
      button.current?.focus();
    };
    document.addEventListener("pointerdown", onPointer);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("pointerdown", onPointer);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  if (items.length === 0) return null;

  return (
    <div ref={root} className={cn("relative", className)}>
      <button
        ref={button}
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        aria-label={label}
        className="press inline-flex size-8 items-center justify-center rounded-full text-fg-muted hover:bg-surface-2 hover:text-fg"
      >
        <MoreHorizontal aria-hidden size={18} />
      </button>
      {open && (
        <div
          id={menuId}
          role="menu"
          aria-label={label}
          className="absolute right-0 z-20 mt-1 w-36 overflow-hidden rounded-md border border-border bg-surface py-1 shadow-float"
        >
          {items.map((item) => (
            <button
              key={item.label}
              type="button"
              role="menuitem"
              onClick={() => {
                setOpen(false);
                item.onSelect();
              }}
              className={cn(
                "block w-full px-3 py-2 text-left text-meta hover:bg-surface-2",
                item.danger ? "text-danger" : "text-fg",
              )}
            >
              {item.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
